using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using PcInspector;
using PcInspector.Collectors;

namespace PcSecurite.Checks;

/// <summary>
/// Durcissement comparé aux références de sécurité de Microsoft et du CIS (même principe que HardeningKitty) et
/// connexions réseau actives. Lecture seule : rien n'est modifié.
/// </summary>
public static partial class SecurityChecks
{
    // ---------- Score de durcissement ----------

    /// <summary>Un réglage de la référence : son nom, s'il est conforme, et quoi faire sinon.</summary>
    private sealed record BaselineItem(string Name, bool Compliant, string Fix);

    private static int? Dword(RegistryKey root, string path, string value)
    {
        using var key = root.OpenSubKey(path);
        return key?.GetValue(value) is int v ? v : null;
    }

    private static SecurityCheck Baseline()
    {
        const string name = "Durcissement (références Microsoft et CIS)";
        var lm = Registry.LocalMachine;
        var cu = Registry.CurrentUser;
        var items = new List<BaselineItem>
        {
            new("Résolution de noms LLMNR désactivée",
                Dword(lm, @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast") == 0,
                "Stratégie « Désactiver la résolution de noms multidiffusion » (vol de mots de passe sur le réseau local)"),
            new("Signature SMB exigée",
                Dword(lm, @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "RequireSecuritySignature") == 1
                || Dword(lm, @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "RequireSecuritySignature") == 1,
                "Exiger la signature des échanges de partage de fichiers (SMB)"),
            new("Authentification NTLMv2 uniquement",
                (Dword(lm, @"SYSTEM\CurrentControlSet\Control\Lsa", "LmCompatibilityLevel") ?? 3) >= 5,
                "Niveau d'authentification LAN Manager : « Envoyer uniquement NTLMv2, refuser LM et NTLM »"),
            new("Empreintes LM non enregistrées",
                (Dword(lm, @"SYSTEM\CurrentControlSet\Control\Lsa", "NoLMHash") ?? 1) == 1,
                "Ne pas stocker l'empreinte LAN Manager des mots de passe"),
            new("Accès anonyme aux comptes refusé",
                (Dword(lm, @"SYSTEM\CurrentControlSet\Control\Lsa", "RestrictAnonymousSAM") ?? 1) == 1
                && (Dword(lm, @"SYSTEM\CurrentControlSet\Control\Lsa", "RestrictAnonymous") ?? 0) >= 1,
                "Interdire l'énumération anonyme des comptes et des partages"),
            new("Exécution automatique des clés USB désactivée",
                Dword(lm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun") == 255
                || Dword(lm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoAutorun") == 1
                || Dword(cu, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", "DisableAutoplay") == 1,
                "Paramètres > Bluetooth et appareils > Exécution automatique : désactivée"),
            new("Assistance à distance désactivée",
                Dword(lm, @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp") == 0,
                "Propriétés système > Utilisation à distance : décocher « Autoriser les connexions d'assistance à distance »"),
            new("Journal des scripts PowerShell activé",
                Dword(lm, @"SOFTWARE\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging", "EnableScriptBlockLogging") == 1,
                "Stratégie « Activer la journalisation des blocs de scripts PowerShell » (traces en cas d'attaque)"),
            new("Macros Office non autorisées",
                !new[] { "Word", "Excel", "PowerPoint" }.Any(app => Dword(cu, $@"Software\Microsoft\Office\16.0\{app}\Security", "VBAWarnings") == 1),
                "Centre de gestion de la confidentialité d'Office : « Désactiver les macros avec notification »"),
            new("Accès contrôlé aux dossiers (anti-rançongiciel)",
                DefenderPreference("EnableControlledFolderAccess") == 1,
                "Sécurité Windows > Protection contre les rançongiciels : activer l'accès contrôlé aux dossiers"),
            new("Protection réseau de Defender",
                DefenderPreference("EnableNetworkProtection") == 1,
                "Activer la protection réseau de Microsoft Defender (bloque les sites malveillants pour tous les programmes)"),
            new("Règles de réduction de la surface d'attaque",
                DefenderAsrRules() > 0,
                "Activer les règles ASR de Defender (macros, scripts, programmes lancés depuis les e-mails)"),
            new("Registre à distance arrêté",
                ServiceStopped("RemoteRegistry"),
                "Service « Registre à distance » : type de démarrage Désactivé"),
            new("Gestion à distance WinRM arrêtée",
                ServiceStopped("WinRM"),
                "Service « Gestion à distance de Windows (WinRM) » : inutile sur un PC de particulier ou de TPE"),
            new("Écran verrouillé à la sortie de veille",
                Dword(cu, @"Control Panel\Desktop", "DelayLockInterval") != -1
                && (Dword(lm, @"SOFTWARE\Policies\Microsoft\Power\PowerSettings\0e796bdb-100d-47d6-a2d5-f7d2daa51f51", "ACSettingIndex") ?? 1) == 1,
                "Paramètres > Comptes > Options de connexion : demander la connexion à la sortie de veille"),
        };

        var compliant = items.Count(i => i.Compliant);
        var percent = compliant * 100 / items.Count;
        var missing = items.Where(i => !i.Compliant).ToList();
        var detail = $"{compliant}/{items.Count} réglages conformes ({percent} %)"
                     + (missing.Count == 0 ? "" : " · À renforcer : " + string.Join(", ", missing.Select(i => i.Name)));
        if (missing.Count == 0) return SecurityCheck.Ok(name, detail);
        var advice = "Réglages à appliquer (vérifier qu'aucun logiciel du client n'en dépend) : "
                     + string.Join(" ; ", missing.Select(i => $"{i.Name} → {i.Fix}")) + ".";
        return percent >= 60 ? SecurityCheck.Info(name, detail + ". " + advice) : SecurityCheck.Warning(name, detail, advice);
    }

    private static ulong? DefenderPreference(string property) =>
        Wmi.QueryOptional($"SELECT {property} FROM MSFT_MpPreference", @"root\Microsoft\Windows\Defender").FirstOrDefault()?.ULong(property);

    private static int DefenderAsrRules()
    {
        var actions = Wmi.QueryOptional("SELECT AttackSurfaceReductionRules_Actions FROM MSFT_MpPreference", @"root\Microsoft\Windows\Defender")
            .FirstOrDefault()?.Str("AttackSurfaceReductionRules_Actions");
        // 1 = bloquer, 6 = avertir ; 0 = désactivée, 2 = audit seulement.
        return string.IsNullOrEmpty(actions) ? 0 : actions!.Split(',').Count(a => a.Trim() is "1" or "6");
    }

    private static bool ServiceStopped(string service)
    {
        var svc = Wmi.QueryOptional($"SELECT State, StartMode FROM Win32_Service WHERE Name = '{service}'").FirstOrDefault();
        if (svc is null) return true; // service absent
        return svc.Str("State") == "Stopped" && svc.Str("StartMode") != "Auto";
    }

    // ---------- Connexions actives ----------

    private sealed record Connection(int Pid, IPAddress Local, int LocalPort, IPAddress Remote, int RemotePort, int State);

    private const int StateListen = 2, StateEstablished = 5;

    private static SecurityCheck ActiveConnections()
    {
        const string name = "Connexions réseau actives";
        var connections = TcpTable();
        if (connections.Count == 0) return SecurityCheck.Unknown(name, "Table des connexions illisible");

        var paths = new Dictionary<int, string?>();
        string? PathOf(int pid)
        {
            if (!paths.TryGetValue(pid, out var path)) paths[pid] = path = ProcessPath(pid);
            return path;
        }
        var signatures = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool Signed(string path)
        {
            if (!signatures.TryGetValue(path, out var ok)) signatures[path] = ok = Authenticode.IsSigned(path);
            return ok;
        }

        var outside = connections.Where(c => c.State == StateEstablished && !IPAddress.IsLoopback(c.Remote) && !IsPrivate(c.Remote)).ToList();
        var listening = connections.Where(c => c.State == StateListen && (c.Local.Equals(IPAddress.Any) || !IPAddress.IsLoopback(c.Local))).ToList();

        var dangers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var remote = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool UserFolder(string path) =>
            path.IndexOf(@"\AppData\", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf(@"\Temp\", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf(@"\Downloads\", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf(@"\Téléchargements\", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf(@"\Users\Public\", StringComparison.OrdinalIgnoreCase) >= 0;

        foreach (var c in outside)
        {
            var path = PathOf(c.Pid);
            if (path is null) continue;
            var exe = Path.GetFileName(path);
            if (RemoteTools.Any(t => exe.Replace(" ", "").IndexOf(t.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) >= 0))
                remote.Add($"{exe} → {c.Remote}");
            if (Signed(path)) continue;
            // Programme non signé qui parle à internet depuis un dossier utilisateur : profil typique d'un logiciel malveillant.
            if (UserFolder(path)) dangers.Add($"{exe} ({FolderOf(path)}) → {c.Remote}:{c.RemotePort}");
            else warnings.Add($"{exe} → {c.Remote}:{c.RemotePort}");
        }
        foreach (var c in listening)
        {
            var path = PathOf(c.Pid);
            if (path is null || Signed(path)) continue;
            warnings.Add($"{Path.GetFileName(path)} attend des connexions sur le port {c.LocalPort}");
        }

        var summary = $"{outside.Count} connexion(s) vers internet, {listening.Count} port(s) ouvert(s) sur le réseau";
        if (dangers.Count > 0)
            return SecurityCheck.Danger(name, summary + " · Programme non signé lancé depuis un dossier utilisateur : " + string.Join(", ", dangers.Take(5)),
                "Identifier ce programme (nom, dossier, date) : s'il est inconnu du client, couper internet, lancer AdwCleaner et une analyse Defender hors ligne, puis changer les mots de passe.");
        if (remote.Count > 0)
            return SecurityCheck.Warning(name, summary + " · Prise en main à distance connectée en ce moment : " + string.Join(", ", remote.Take(3)),
                "Demander au client s'il est en cours d'assistance. Sinon, fermer le logiciel et vérifier qu'il n'a pas été installé par un faux support technique.");
        if (warnings.Count > 0)
            return SecurityCheck.Info(name, summary + " · Programmes non signés à vérifier : " + string.Join(", ", warnings.Take(6)));
        return SecurityCheck.Ok(name, summary + " · tous les programmes connectés sont signés");
    }

    private static string FolderOf(string path)
    {
        var folder = Path.GetDirectoryName(path) ?? "";
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return folder.StartsWith(profile, StringComparison.OrdinalIgnoreCase) ? "%USERPROFILE%" + folder.Substring(profile.Length) : folder;
    }

    private static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || b[0] == 0);
    }

    private static string? ProcessPath(int pid)
    {
        if (pid <= 4) return null; // System
        var handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally { CloseHandle(handle); }
    }

    /// <summary>Connexions TCP IPv4 avec le programme propriétaire (comme « netstat -ano »).</summary>
    private static List<Connection> TcpTable()
    {
        var list = new List<Connection>();
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, true, 2 /* AF_INET */, 5 /* TCP_TABLE_OWNER_PID_ALL */, 0);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, true, 2, 5, 0) != 0) return list;
            var count = Marshal.ReadInt32(buffer);
            var row = buffer + 4;
            for (var i = 0; i < count; i++)
            {
                var state = Marshal.ReadInt32(row);
                var localAddr = (uint)Marshal.ReadInt32(row, 4);
                var localPort = Port(Marshal.ReadInt32(row, 8));
                var remoteAddr = (uint)Marshal.ReadInt32(row, 12);
                var remotePort = Port(Marshal.ReadInt32(row, 16));
                var pid = Marshal.ReadInt32(row, 20);
                list.Add(new Connection(pid, new IPAddress(localAddr), localPort, new IPAddress(remoteAddr), remotePort, state));
                row += 24;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return list;
    }

    // Le port est en ordre réseau dans les 2 premiers octets.
    private static int Port(int raw) => ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int addressFamily, int tableClass, uint reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
}
