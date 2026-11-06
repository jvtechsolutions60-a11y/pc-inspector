using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PcMaintenance.Preparation;

namespace PcMaintenance.Maintenance;

/// <summary>
/// Mise à niveau vers Windows 11 d'un PC refusé par le contrôle de compatibilité (TPM 2.0, processeur, Secure Boot),
/// depuis une ISO posée sur le PC. Les fichiers, logiciels et réglages sont gardés.
/// Seule limite infranchissable : depuis 24H2, Windows 11 ne démarre pas sans les instructions SSE4.2 et POPCNT.
/// </summary>
internal static class Windows11Upgrade
{
    private const int SSE42 = 38; // PF_SSE4_2_INSTRUCTIONS_AVAILABLE

    [DllImport("kernel32.dll")]
    private static extern bool IsProcessorFeaturePresent(int feature);

    /// <summary>Raisons qui empêchent la mise à niveau (vide si elle est possible).</summary>
    public static List<string> Blockers()
    {
        var blockers = new List<string>();
        if (!Environment.Is64BitOperatingSystem)
            blockers.Add("Windows est en 32 bits : Windows 11 n'existe qu'en 64 bits (installation propre obligatoire, données à sauvegarder).");
        if (!IsProcessorFeaturePresent(SSE42))
            blockers.Add("Le processeur n'a pas les instructions SSE4.2 / POPCNT : Windows 11 (24H2 et suivants) ne peut pas démarrer dessus.");
        return blockers;
    }

    /// <summary>Points à vérifier sans bloquer : place disque.</summary>
    public static List<string> Warnings()
    {
        var warnings = new List<string>();
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            var free = drive.AvailableFreeSpace / 1e9;
            if (free < 25) warnings.Add($"Seulement {free:0} Go libres sur {drive.Name.TrimEnd('\\')} : il en faut environ 25.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
        return warnings;
    }

    /// <summary>Ouvre l'ISO, lève le contrôle de compatibilité et lance l'installation. Renvoie null si elle a démarré.</summary>
    public static string? Start(string iso, IProgress<string> log)
    {
        log.Report("Ouverture de l'ISO…");
        var lines = PowerShell.Run($@"
$img = Get-DiskImage -ImagePath {PowerShell.Quote(iso)}
if (-not $img.Attached) {{ $img = Mount-DiskImage -ImagePath {PowerShell.Quote(iso)} -PassThru }}
'LETTRE|' + ($img | Get-Volume).DriveLetter", TimeSpan.FromMinutes(3));
        var letter = lines.Where(l => l.StartsWith("LETTRE|")).Select(l => l.Substring(7).Trim()).FirstOrDefault(l => l.Length == 1);
        if (letter is null) return "Impossible d'ouvrir l'ISO.";
        var root = letter + @":\";
        var setup = Path.Combine(root, "setup.exe");
        if (!File.Exists(setup) || !File.Exists(Path.Combine(root, @"sources\setupprep.exe")))
            return "Cette ISO n'est pas une ISO d'installation de Windows 11.";
        log.Report($"ISO ouverte sur {letter}:.");

        // Réglage documenté par Microsoft pour les PC sans TPM 2.0 ou avec un processeur non listé.
        using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\Setup\MoSetup"))
            key?.SetValue("AllowUpgradesWithUnsupportedTPMOrCPU", 1, RegistryValueKind.DWord);
        // Résultat du contrôle de compatibilité remplacé par « compatible » (TPM 2, Secure Boot, 8 Go).
        using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\HwReqChk"))
            key?.SetValue("HwReqChkVars", new[] { "SQ_SecureBootCapable=TRUE", "SQ_SecureBootEnabled=TRUE", "SQ_TpmVersion=2", "SQ_RamMB=8192" }, RegistryValueKind.MultiString);
        log.Report("Contrôle de compatibilité levé.");

        // « /product server » : le programme d'installation saute le contrôle du matériel, mais installe bien Windows 11
        // (l'édition vient de l'ISO). Pas de mise à jour dynamique, qui retéléchargerait le contrôle.
        const string arguments = "/product server /auto upgrade /compat ignorewarning /dynamicupdate disable /eula accept /migratedrivers all";
        try
        {
            Process.Start(new ProcessStartInfo(setup, arguments) { UseShellExecute = true, WorkingDirectory = root });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return "Impossible de lancer l'installation : " + ex.Message;
        }
        log.Report($"Installation lancée : setup.exe {arguments}");
        log.Report("Le PC va redémarrer plusieurs fois (30 à 90 minutes). Fichiers, logiciels et réglages sont gardés.");
        return null;
    }
}
