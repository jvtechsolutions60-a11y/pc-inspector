using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using PcInspector;

namespace JvtechServeur.Pxe;

/// <summary>
/// Prépare le serveur à partir d'une ISO de Windows 11, avec les seuls outils de Windows (DISM, bcdedit, PowerShell) :
/// fichiers d'installation partagés en lecture, Windows PE de l'ISO qui lance l'installation automatique depuis ce
/// partage, fichiers de démarrage TFTP signés Microsoft (compatibles Secure Boot), pare-feu.
/// </summary>
internal static class ImageBuilder
{
    private static readonly TimeSpan Long = TimeSpan.FromHours(3);

    /// <summary>Éditions gardées : celles des licences des PC du commerce. Les autres alourdiraient chaque mise à jour.</summary>
    private static readonly string[] Editions = { "Core", "CoreSingleLanguage", "Professional" };

    /// <summary>Renvoie null si tout s'est bien passé, sinon la raison de l'échec.</summary>
    public static string? Prepare(string iso, IPAddress server, ServerConfig config, Action<string> log, Action<string> status)
    {
        foreach (var folder in new[] { ServerConfig.InstallFolder, ServerConfig.TftpFolder, Path.Combine(ServerConfig.TftpFolder, "Boot"), ServerConfig.WorkFolder, ServerConfig.ReportsFolder })
            Directory.CreateDirectory(folder);

        status("Compte de lecture du partage…");
        if (config.SharePassword.Length == 0) config.SharePassword = ServerConfig.NewPassword();
        config.Save();
        if (!CreateAccount(config.SharePassword, log)) return "Impossible de créer le compte de partage « jvtechpxe ».";

        status("Ouverture de l'ISO…");
        var drive = MountIso(iso, log);
        if (drive is null) return "Impossible d'ouvrir l'ISO.";
        try
        {
            if (!File.Exists(Path.Combine(drive, "setup.exe")) || !File.Exists(Path.Combine(drive, @"sources\boot.wim")))
                return "Ce fichier n'est pas une ISO d'installation de Windows 11.";

            status("Copie des fichiers d'installation (5 à 10 minutes)…");
            var (copy, _) = Tools.Run(Tools.System32("robocopy.exe"), $"\"{drive.TrimEnd('\\')}\" \"{ServerConfig.InstallFolder}\" /MIR /NFL /NDL /NJH /NP /R:1 /W:1", null, Long);
            // robocopy : 0 à 7 = réussi (avec ou sans copie), 8 et plus = erreurs.
            if (copy is null or >= 8) return $"La copie de l'ISO a échoué (robocopy {copy}).";
            Tools.Run(Tools.System32("attrib.exe"), $"-r \"{ServerConfig.InstallFolder}\\*\" /s /d", null, TimeSpan.FromMinutes(10));
            log("Fichiers d'installation copiés dans " + ServerConfig.InstallFolder);
        }
        finally
        {
            DismountIso(iso, log);
        }

        status("Éditions Famille et Professionnel (10 à 20 minutes)…");
        if (!KeepEditions(log)) return "Impossible de préparer les éditions de Windows (install.wim).";

        status("Fichiers de préparation et partage…");
        CopyKit(server, log);
        if (!Share(log)) return "Impossible de créer le partage JVTECH-Install ou les règles du pare-feu.";

        status("Windows PE pour le démarrage réseau (5 minutes)…");
        var pe = BuildBootWim(server, config.SharePassword, log);
        if (pe is not null) return pe;

        status("Fichiers de démarrage et BCD…");
        var boot = BuildBootFiles(log);
        if (boot is not null) return boot;

        config.ServerAddress = server.ToString();
        config.LastUpdate = "";
        config.LastUpdateDate = null;
        config.Save();
        status("Serveur prêt.");
        return null;
    }

    private static bool CreateAccount(string password, Action<string> log)
    {
        var script = $@"
$pw = ConvertTo-SecureString {Tools.Quote(password)} -AsPlainText -Force
$u = Get-LocalUser -Name '{ServerConfig.ShareUser}' -ErrorAction SilentlyContinue
if ($u) {{ Set-LocalUser -Name '{ServerConfig.ShareUser}' -Password $pw -PasswordNeverExpires $true; Enable-LocalUser -Name '{ServerConfig.ShareUser}' }}
else {{ New-LocalUser -Name '{ServerConfig.ShareUser}' -Password $pw -PasswordNeverExpires -UserMayNotChangePassword -Description 'Lecture du partage JVTECH-Install (PXE)' | Out-Null }}
# Compte technique : caché de l'écran de connexion.
$list = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList'
New-Item -Path $list -Force | Out-Null
New-ItemProperty -Path $list -Name '{ServerConfig.ShareUser}' -Value 0 -PropertyType DWord -Force | Out-Null
if (Get-LocalUser -Name '{ServerConfig.ShareUser}' -ErrorAction SilentlyContinue) {{ 'OK' }}";
        var (_, lines) = Tools.PowerShell(script, null, TimeSpan.FromMinutes(2));
        foreach (var line in lines.Where(l => l != "OK")) log(line);
        return lines.Contains("OK");
    }

    private static string? MountIso(string iso, Action<string> log)
    {
        var (_, lines) = Tools.PowerShell($@"
$img = Get-DiskImage -ImagePath {Tools.Quote(iso)}
if (-not $img.Attached) {{ $img = Mount-DiskImage -ImagePath {Tools.Quote(iso)} -PassThru }}
$letter = ($img | Get-Volume).DriveLetter
'LETTRE|' + $letter", log, TimeSpan.FromMinutes(3));
        var letter = lines.Select(l => l.StartsWith("LETTRE|") ? l.Substring(7).Trim() : null).FirstOrDefault(l => !string.IsNullOrEmpty(l));
        return letter is null ? null : letter + @":\";
    }

    private static void DismountIso(string iso, Action<string> log) =>
        Tools.PowerShell($"Dismount-DiskImage -ImagePath {Tools.Quote(iso)} | Out-Null", log, TimeSpan.FromMinutes(2));

    private static bool KeepEditions(Action<string> log)
    {
        var sources = Path.Combine(ServerConfig.InstallFolder, "sources");
        var script = $@"
$dir = {Tools.Quote(sources)}
$src = Join-Path $dir 'install.wim'
if (-not (Test-Path $src)) {{ $src = Join-Path $dir 'install.esd' }}
$dst = Join-Path $dir 'install-jvtech.wim'
if (Test-Path $dst) {{ Remove-Item $dst -Force }}
$keep = @({string.Join(",", Editions.Select(Tools.Quote))})
foreach ($img in Get-WindowsImage -ImagePath $src) {{
  $info = Get-WindowsImage -ImagePath $src -Index $img.ImageIndex
  if ($keep -contains $info.EditionId) {{
    Export-WindowsImage -SourceImagePath $src -SourceIndex $img.ImageIndex -DestinationImagePath $dst -CompressionType max | Out-Null
    'EDITION|' + $img.ImageName
  }}
}}
if (Test-Path $dst) {{
  Remove-Item $src -Force
  Rename-Item $dst 'install.wim'
  'OK'
}}";
        var (_, lines) = Tools.PowerShell(script, null, Long);
        foreach (var line in lines)
        {
            if (line.StartsWith("EDITION|")) log("Édition gardée : " + line.Substring(8));
            else if (line != "OK") log(line);
        }
        return lines.Contains("OK");
    }

    /// <summary>
    /// Fichier de réponse et PC Maintenance (dossier Kit à côté du serveur), recopiés par Windows dans C:\JVTECH
    /// grâce au dossier sources\$OEM$\$1. serveur.ini dit à PC Maintenance où envoyer l'avancement.
    /// Refait à chaque démarrage du serveur : un changement d'options.ini s'applique aux PC suivants.
    /// </summary>
    public static void CopyKit(IPAddress server, Action<string> log)
    {
        var kit = Path.Combine(AppPaths.ExeFolder, "Kit");
        var oem = Path.Combine(ServerConfig.InstallFolder, @"sources\$OEM$\$1\JVTECH");
        Directory.CreateDirectory(oem);
        File.Copy(Path.Combine(kit, "autounattend.xml"), Path.Combine(ServerConfig.InstallFolder, "autounattend.xml"), true);
        foreach (var file in Directory.GetFiles(Path.Combine(kit, "JVTECH")))
            File.Copy(file, Path.Combine(oem, Path.GetFileName(file)), true);
        File.WriteAllLines(Path.Combine(oem, "serveur.ini"), new[]
        {
            "# Écrit par JVTECH Serveur PXE : où PC Maintenance envoie l'avancement et le rapport.",
            $"Url=http://{server}:{ServerConfig.StatusPort}/jvtech/",
            $"Nom={Environment.MachineName}",
        });
        log("Fichier de réponse et PC Maintenance ajoutés à l'installation.");
    }

    /// <summary>Partage en lecture seule pour le compte jvtechpxe, et pare-feu ouvert pour le PXE et le suivi.</summary>
    private static bool Share(Action<string> log)
    {
        var exe = AppInfo.Entry.Location;
        var script = $@"
$path = {Tools.Quote(ServerConfig.InstallFolder)}
$account = $env:COMPUTERNAME + '\{ServerConfig.ShareUser}'
icacls $path /grant ($account + ':(OI)(CI)RX') | Out-Null
if (Get-SmbShare -Name '{ServerConfig.ShareName}' -ErrorAction SilentlyContinue) {{ Remove-SmbShare -Name '{ServerConfig.ShareName}' -Force }}
New-SmbShare -Name '{ServerConfig.ShareName}' -Path $path -ReadAccess $account | Out-Null
Get-NetFirewallRule -DisplayName 'JVTECH PXE*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName 'JVTECH PXE (DHCP et TFTP)' -Direction Inbound -Protocol UDP -LocalPort 67,69,4011 -Action Allow -Profile Any | Out-Null
New-NetFirewallRule -DisplayName 'JVTECH PXE (transferts TFTP)' -Direction Inbound -Protocol UDP -Program {Tools.Quote(exe)} -Action Allow -Profile Any | Out-Null
New-NetFirewallRule -DisplayName 'JVTECH PXE (suivi des postes)' -Direction Inbound -Protocol TCP -LocalPort {ServerConfig.StatusPort} -Action Allow -Profile Any | Out-Null
# Partage de fichiers et d'imprimantes, et réseau de l'atelier en « privé » pour qu'il soit accessible.
Enable-NetFirewallRule -Group '@FirewallAPI.dll,-28502' -ErrorAction SilentlyContinue
Get-NetConnectionProfile | Where-Object {{ $_.NetworkCategory -eq 'Public' }} | Set-NetConnectionProfile -NetworkCategory Private -ErrorAction SilentlyContinue
if (Get-SmbShare -Name '{ServerConfig.ShareName}' -ErrorAction SilentlyContinue) {{ 'OK' }}";
        var (_, lines) = Tools.PowerShell(script, null, TimeSpan.FromMinutes(3));
        foreach (var line in lines.Where(l => l != "OK")) log(line);
        if (lines.Contains("OK")) log($"Partage \\\\{Environment.MachineName}\\{ServerConfig.ShareName} prêt (lecture seule).");
        return lines.Contains("OK");
    }

    /// <summary>
    /// Windows PE de l'ISO (boot.wim, image 2 « Windows Setup ») modifié : au démarrage, il se connecte au partage
    /// du serveur et lance l'installation avec autounattend.xml, au lieu de l'assistant habituel.
    /// </summary>
    private static string? BuildBootWim(IPAddress server, string password, Action<string> log)
    {
        var dism = Tools.System32("dism.exe");
        var work = Path.Combine(ServerConfig.WorkFolder, "boot.wim");
        var mount = Path.Combine(ServerConfig.WorkFolder, "montage-pe");
        // Un montage resté ouvert après un arrêt brutal bloquerait la suite.
        Tools.Run(dism, $"/Unmount-Image /MountDir:\"{mount}\" /Discard", null, TimeSpan.FromMinutes(10));
        Tools.Run(dism, "/Cleanup-Wim", null, TimeSpan.FromMinutes(10));
        Directory.CreateDirectory(mount);
        File.Copy(Path.Combine(ServerConfig.InstallFolder, @"sources\boot.wim"), work, true);
        File.SetAttributes(work, FileAttributes.Normal);

        var (mounted, _) = Tools.Run(dism, $"/Mount-Image /ImageFile:\"{work}\" /Index:2 /MountDir:\"{mount}\"", log, TimeSpan.FromMinutes(20));
        if (mounted != 0) return "Impossible d'ouvrir Windows PE (boot.wim) avec DISM.";
        try
        {
            var system32 = Path.Combine(mount, @"Windows\System32");
            File.WriteAllText(Path.Combine(system32, "winpeshl.ini"), string.Join("\r\n",
                "[LaunchApps]",
                @"%SYSTEMROOT%\System32\wpeinit.exe",
                @"%SYSTEMROOT%\System32\cmd.exe, /c %SYSTEMROOT%\System32\jvtech.cmd"), Encoding.ASCII);
            File.WriteAllText(Path.Combine(system32, "jvtech.cmd"), WinPeScript(server, password), Encoding.ASCII);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Tools.Run(dism, $"/Unmount-Image /MountDir:\"{mount}\" /Discard", log, TimeSpan.FromMinutes(10));
            return "Impossible de modifier Windows PE : " + ex.Message;
        }

        var (saved, _) = Tools.Run(dism, $"/Unmount-Image /MountDir:\"{mount}\" /Commit", log, TimeSpan.FromMinutes(20));
        if (saved != 0) return "Impossible d'enregistrer Windows PE modifié.";
        File.Copy(work, Path.Combine(ServerConfig.TftpFolder, @"Boot\boot.wim"), true);
        log("Windows PE prêt pour le démarrage réseau.");
        return null;
    }

    /// <summary>Script lancé par Windows PE : connexion au partage (par nom puis par adresse) et installation.</summary>
    private static string WinPeScript(IPAddress server, string password)
    {
        var name = Environment.MachineName;
        return string.Join("\r\n",
            "@echo off",
            "title Installation automatique JVTECH",
            "color 1F",
            "echo.",
            "echo   INSTALLATION AUTOMATIQUE JVTECH",
            "echo   Ce PC va etre EFFACE puis Windows 11 reinstalle.",
            "echo   Pour annuler : eteindre le PC maintenant.",
            "echo.",
            "wpeutil WaitForNetwork >nul 2>&1",
            "set ESSAI=0",
            ":connexion",
            $@"net use J: \\{name}\{ServerConfig.ShareName} /user:{name}\{ServerConfig.ShareUser} {password} >nul 2>&1 && goto installer",
            $@"net use J: \\{server}\{ServerConfig.ShareName} /user:{name}\{ServerConfig.ShareUser} {password} >nul 2>&1 && goto installer",
            "set /a ESSAI+=1",
            "if %ESSAI% GEQ 40 goto echec",
            "echo   Connexion au serveur JVTECH... (essai %ESSAI%)",
            "ping -n 6 127.0.0.1 >nul",
            "goto connexion",
            ":installer",
            "echo   Serveur trouve : installation en cours.",
            @"J:\setup.exe /unattend:J:\autounattend.xml",
            "wpeutil reboot",
            ":echec",
            "echo.",
            "echo   Impossible de joindre le serveur JVTECH : verifier le cable reseau et que le serveur est demarre.",
            "pause",
            "wpeutil reboot",
            "");
    }

    /// <summary>
    /// bootmgfw.efi (gestionnaire de démarrage UEFI signé Microsoft), boot.sdi, polices, et un BCD qui démarre
    /// Windows PE en mémoire. Blocs TFTP de 1456 octets par fenêtres de 16 pour un chargement rapide.
    /// </summary>
    private static string? BuildBootFiles(Action<string> log)
    {
        var install = ServerConfig.InstallFolder;
        var tftp = ServerConfig.TftpFolder;
        var boot = Path.Combine(tftp, "Boot");
        try
        {
            File.Copy(Path.Combine(install, @"efi\boot\bootx64.efi"), Path.Combine(tftp, ProxyDhcp.BootFile), true);
            File.Copy(Path.Combine(install, @"boot\boot.sdi"), Path.Combine(boot, "boot.sdi"), true);
            var fonts = Path.Combine(install, @"efi\microsoft\boot\fonts");
            if (Directory.Exists(fonts))
            {
                Directory.CreateDirectory(Path.Combine(boot, "Fonts"));
                foreach (var font in Directory.GetFiles(fonts)) File.Copy(font, Path.Combine(boot, "Fonts", Path.GetFileName(font)), true);
            }
            foreach (var file in Directory.GetFiles(tftp, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Fichiers de démarrage introuvables dans l'ISO : " + ex.Message;
        }

        var bcd = Path.Combine(boot, "BCD");
        if (File.Exists(bcd)) File.Delete(bcd);
        var bcdedit = Tools.System32("bcdedit.exe");
        string? Edit(string arguments)
        {
            var (code, lines) = Tools.Run(bcdedit, $"/store \"{bcd}\" {arguments}", null, TimeSpan.FromMinutes(1));
            if (code != 0) log($"bcdedit {arguments} : {string.Join(" ", lines)}");
            return code == 0 ? string.Join(" ", lines) : null;
        }

        if (Tools.Run(bcdedit, $"/createstore \"{bcd}\"", log, TimeSpan.FromMinutes(1)).Code != 0) return "Impossible de créer le BCD.";
        Edit("/create {ramdiskoptions} /d \"Ramdisk JVTECH\"");
        Edit("/set {ramdiskoptions} ramdisksdidevice boot");
        Edit(@"/set {ramdiskoptions} ramdisksdipath \Boot\boot.sdi");
        Edit("/set {ramdiskoptions} ramdisktftpblocksize 1456");
        Edit("/set {ramdiskoptions} ramdisktftpwindowsize 16");
        var created = Edit("/create /d \"Installation automatique JVTECH\" /application osloader");
        var guid = created is null ? null : Regex.Match(created, @"\{[0-9a-fA-F\-]{36}\}").Value;
        if (string.IsNullOrEmpty(guid)) return "Impossible de créer l'entrée Windows PE dans le BCD.";
        var device = $@"ramdisk=[boot]\Boot\boot.wim,{{ramdiskoptions}}";
        var ok = new[]
        {
            Edit($"/set {guid} device {device}"),
            Edit($"/set {guid} osdevice {device}"),
            Edit($@"/set {guid} path \windows\system32\boot\winload.efi"),
            Edit($@"/set {guid} systemroot \windows"),
            Edit($"/set {guid} detecthal yes"),
            Edit($"/set {guid} winpe yes"),
            Edit("/create {bootmgr} /d \"JVTECH\""),
            Edit($"/set {{bootmgr}} default {guid}"),
            Edit($"/set {{bootmgr}} displayorder {guid}"),
            Edit("/set {bootmgr} timeout 0"),
        };
        if (ok.Any(r => r is null)) return "Le BCD n'a pas pu être entièrement configuré (voir le journal).";
        log("Fichiers de démarrage réseau prêts.");
        return null;
    }
}
