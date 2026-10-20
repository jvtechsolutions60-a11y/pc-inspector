using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PcInspector.Collectors;

namespace PcSecurite.Checks;

/// <summary>La liste des contrôles, dans l'ordre d'affichage. Chacun est indépendant et ne modifie rien sur le PC.</summary>
public static partial class SecurityChecks
{
    public static IReadOnlyList<(string Name, Func<SecurityCheck> Run)> All { get; } = new List<(string, Func<SecurityCheck>)>
    {
        ("Version de Windows", WindowsSupport),
        ("Antivirus", Antivirus),
        ("Microsoft Defender", Defender),
        ("Pare-feu", Firewall),
        ("Dernière mise à jour", LastUpdate),
        ("Activation de Windows", Activation),
        ("Contrôle de compte (UAC)", Uac),
        ("SmartScreen", SmartScreen),
        ("Intégrité de la mémoire", MemoryIntegrity),
        ("Protection LSA", LsaProtection),
        ("Blocage des pilotes vulnérables", DriverBlocklist),
        ("Mots de passe en mémoire (WDigest)", WDigest),
        ("Sauvegarde des données", Backup),
        ("Chiffrement BitLocker", BitLocker),
        ("Secure Boot", SecureBoot),
        ("Certificats Secure Boot 2023", SecureBootCertificates),
        ("Puce TPM", Tpm),
        ("BIOS", Bios),
        ("Protocole SMB1", Smb1),
        ("PowerShell 2.0", PowerShell2),
        ("Bureau à distance", RemoteDesktop),
        ("Logiciels de prise en main à distance", RemoteAccessTools),
        ("Redirections réseau (hosts, proxy)", NetworkRedirects),
        ("Serveurs DNS", DnsServers),
        ("Réseaux Wi-Fi enregistrés", WifiProfiles),
        ("Comptes utilisateurs", Accounts),
        ("Connexion automatique", AutoLogon),
        ("Navigateurs internet", Browsers),
        ("Logiciels avec failles connues", VulnerableSoftware),
        ("Logiciels abandonnés", ObsoleteSoftware),
        ("Tâches planifiées", ScheduledTasks),
        // En dernier : peut prendre jusqu'à 90 secondes.
        ("Mises à jour en attente", PendingUpdates),
    };

    /// <summary>Exécute un contrôle sans jamais faire planter l'analyse.</summary>
    public static SecurityCheck RunSafely(string name, Func<SecurityCheck> run)
    {
        try { return run(); }
        catch (Exception ex) { return SecurityCheck.Info(name, "Impossible de vérifier : " + ex.Message); }
    }

    // Fin du support des versions de Windows (éditions Famille et Pro).
    private static readonly (int Build, string Name, DateTime End)[] Support =
    {
        (26200, "Windows 11 25H2", new DateTime(2027, 10, 12)),
        (26100, "Windows 11 24H2", new DateTime(2026, 10, 13)),
        (22631, "Windows 11 23H2", new DateTime(2025, 11, 11)),
        (22621, "Windows 11 22H2", new DateTime(2024, 10, 8)),
        (22000, "Windows 11 21H2", new DateTime(2023, 10, 10)),
        (0, "Windows 10", new DateTime(2025, 10, 14)),
    };

    private static SecurityCheck WindowsSupport()
    {
        const string name = "Version de Windows";
        var os = Wmi.Query("SELECT Caption, BuildNumber FROM Win32_OperatingSystem").FirstOrDefault();
        if (!int.TryParse(os?.Str("BuildNumber"), out var build))
            return SecurityCheck.Info(name, os?.Str("Caption") ?? "Version inconnue");

        var caption = os!.Str("Caption") ?? "Windows";
        var (_, versionName, end) = Support.First(s => build >= s.Build);
        var isBusiness = caption.IndexOf("Enterprise", StringComparison.OrdinalIgnoreCase) >= 0
                         || caption.IndexOf("Entreprise", StringComparison.OrdinalIgnoreCase) >= 0
                         || caption.IndexOf("Education", StringComparison.OrdinalIgnoreCase) >= 0;
        // Entreprise et Éducation ont un an de support en plus pour Windows 11.
        if (isBusiness && build >= 22000) end = end.AddYears(1);

        var detail = $"{caption} ({versionName}, build {build})";
        if (caption.IndexOf("LTSC", StringComparison.OrdinalIgnoreCase) >= 0 || caption.IndexOf("LTSB", StringComparison.OrdinalIgnoreCase) >= 0)
            return SecurityCheck.Info(name, detail + " : édition LTSC, support étendu propre à chaque version");
        if (DateTime.Today > end)
            return SecurityCheck.Danger(name, $"{detail} : plus de mises à jour de sécurité depuis le {end:dd/MM/yyyy}",
                build >= 22000 ? "Installer la dernière version de Windows 11 via Windows Update." : "Passer à Windows 11 (voir PC Inspector, onglet Windows 11), ou changer de PC s'il n'est pas compatible. Les mises à jour étendues (ESU) pour les particuliers ont pris fin le 13/10/2026.",
                build >= 22000 ? CheckKind.Fix : CheckKind.Change);
        if (DateTime.Today > end.AddDays(-90))
            return SecurityCheck.Warning(name, $"{detail} : fin du support le {end:dd/MM/yyyy}", "Prévoir la mise à niveau vers la version suivante.");
        return SecurityCheck.Ok(name, $"{detail} : supporté jusqu'au {end:dd/MM/yyyy}");
    }

    private static SecurityCheck Antivirus()
    {
        const string name = "Antivirus";
        var products = Wmi.QueryOptional("SELECT displayName, productState FROM AntiVirusProduct", @"root\SecurityCenter2");
        if (products.Count == 0)
            return SecurityCheck.Danger(name, "Aucun antivirus détecté", "Réactiver Microsoft Defender ou installer un antivirus.");

        // productState : octet du milieu 0x10/0x11 = activé, octet bas 0x00 = définitions à jour.
        var states = products.Select(p => (Name: p.Str("displayName") ?? "?", State: p.ULong("productState") ?? 0)).ToList();
        bool Enabled(ulong s) => ((s >> 8) & 0xFF) is 0x10 or 0x11;
        bool UpToDate(ulong s) => (s & 0xFF) == 0x00;

        var active = states.Where(s => Enabled(s.State)).ToList();
        if (active.Count == 0)
            return SecurityCheck.Danger(name, "Désactivé : " + string.Join(", ", states.Select(s => s.Name)), "Réactiver la protection en temps réel.");
        var outdated = active.Where(s => !UpToDate(s.State)).ToList();
        if (outdated.Count == active.Count)
            return SecurityCheck.Warning(name, $"{string.Join(", ", active.Select(s => s.Name))} actif, mais définitions périmées", "Lancer une mise à jour de l'antivirus.");
        var detail = string.Join(", ", active.Select(s => s.Name)) + " actif et à jour";
        return active.Count > 1
            ? SecurityCheck.Warning(name, detail + " (plusieurs antivirus actifs)", "Garder un seul antivirus : plusieurs en même temps ralentissent le PC et se gênent.")
            : SecurityCheck.Ok(name, detail);
    }

    private static SecurityCheck Firewall()
    {
        const string name = "Pare-feu";
        var thirdParty = Wmi.QueryOptional("SELECT displayName, productState FROM FirewallProduct", @"root\SecurityCenter2")
            .Where(f => (((f.ULong("productState") ?? 0) >> 8) & 0xFF) is 0x10 or 0x11)
            .Select(f => f.Str("displayName"))
            .ToList();
        if (thirdParty.Count > 0)
            return SecurityCheck.Ok(name, string.Join(", ", thirdParty) + " actif");

        var off = new List<string>();
        foreach (var (key, label) in new[] { ("DomainProfile", "domaine"), ("StandardProfile", "privé"), ("PublicProfile", "public") })
        {
            using var reg = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{key}");
            if (reg?.GetValue("EnableFirewall") is int enabled && enabled == 0) off.Add(label);
        }
        if (off.Count == 0) return SecurityCheck.Ok(name, "Pare-feu Windows activé sur tous les réseaux");
        var detail = "Pare-feu Windows désactivé pour le réseau " + string.Join(", ", off);
        return off.Contains("public")
            ? SecurityCheck.Danger(name, detail, "Réactiver le pare-feu, surtout pour les réseaux publics (Wi-Fi d'hôtel, gare…).")
            : SecurityCheck.Warning(name, detail, "Réactiver le pare-feu sur tous les profils.");
    }

    private static SecurityCheck LastUpdate()
    {
        const string name = "Dernière mise à jour";
        var last = WindowsUpdateInfo.LastInstalled();
        if (last is null) return SecurityCheck.Info(name, "Date inconnue");
        var days = (int)(DateTime.Today - last.Value.Date).TotalDays;
        var detail = $"Dernière mise à jour installée le {last:dd/MM/yyyy} (il y a {days} jours)";
        if (days > 60) return SecurityCheck.Danger(name, detail, "Windows ne se met plus à jour : vérifier Windows Update et l'espace disque.");
        if (days > 35) return SecurityCheck.Warning(name, detail, "Lancer Windows Update : une mise à jour mensuelle a probablement été manquée.");
        return SecurityCheck.Ok(name, detail);
    }

    private static SecurityCheck PendingUpdates()
    {
        const string name = "Mises à jour en attente";
        var reboot = WindowsUpdateInfo.IsRebootPending();
        var pending = WindowsUpdateInfo.PendingUpdates();
        if (pending is null)
            return SecurityCheck.Info(name, "Recherche impossible (Windows Update indisponible ou trop lent)" + (reboot ? " · redémarrage en attente" : ""));
        if (pending.Count == 0 && !reboot) return SecurityCheck.Ok(name, "Aucune, Windows est à jour");

        var parts = new List<string>();
        if (pending.Count > 0) parts.Add($"{pending.Count} mise(s) à jour : {string.Join(" ; ", pending.Take(5))}{(pending.Count > 5 ? " ; …" : "")}");
        if (reboot) parts.Add("redémarrage nécessaire pour finir une installation");
        return SecurityCheck.Warning(name, string.Join(" · ", parts), "Installer les mises à jour puis redémarrer.");
    }

    private static SecurityCheck Activation()
    {
        const string name = "Activation de Windows";
        var product = Wmi.QueryOptional(
                "SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID = '55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL")
            .FirstOrDefault();
        return product?.ULong("LicenseStatus") switch
        {
            1 => SecurityCheck.Ok(name, "Windows est activé"),
            null => SecurityCheck.Info(name, "État inconnu"),
            _ => SecurityCheck.Warning(name, "Windows n'est pas activé", "Activer Windows avec une licence valide (clé OEM du BIOS visible dans PC Inspector)."),
        };
    }

    private static SecurityCheck Uac()
    {
        const string name = "Contrôle de compte (UAC)";
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
        if (key?.GetValue("EnableLUA") is int lua && lua == 0)
            return SecurityCheck.Danger(name, "UAC désactivé : tout programme peut modifier le système sans confirmation", "Réactiver l'UAC (redémarrage nécessaire).");
        if (key?.GetValue("ConsentPromptBehaviorAdmin") is int prompt && prompt == 0)
            return SecurityCheck.Warning(name, "UAC réglé sur « Ne jamais m'avertir »", "Remettre le curseur UAC au niveau par défaut.");
        return SecurityCheck.Ok(name, "Activé");
    }

    private static SecurityCheck SmartScreen()
    {
        const string name = "SmartScreen";
        using var policy = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System");
        if (policy?.GetValue("EnableSmartScreen") is int enabled && enabled == 0)
            return SecurityCheck.Danger(name, "Désactivé par une stratégie", "Réactiver SmartScreen, qui bloque les programmes et sites malveillants connus.");
        using var explorer = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer");
        if (string.Equals(explorer?.GetValue("SmartScreenEnabled") as string, "Off", StringComparison.OrdinalIgnoreCase))
            return SecurityCheck.Warning(name, "Désactivé pour les applications", "Réactiver « Vérifier les applications et les fichiers » dans Sécurité Windows.");
        return SecurityCheck.Ok(name, "Activé");
    }

    private static SecurityCheck BitLocker()
    {
        const string name = "Chiffrement BitLocker";
        var drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var volume = Wmi.QueryOptional($"SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = '{drive}'",
            @"root\CIMV2\Security\MicrosoftVolumeEncryption").FirstOrDefault();
        if (volume is null) return SecurityCheck.Info(name, "Non disponible sur cette édition de Windows");
        var isLaptop = Wmi.QueryOptional("SELECT Name FROM Win32_Battery").Count > 0;
        return volume.ULong("ProtectionStatus") switch
        {
            1 => SecurityCheck.Ok(name, $"Disque {drive} chiffré (noter la clé de récupération avant toute intervention)"),
            _ when isLaptop => SecurityCheck.Warning(name, $"Disque {drive} non chiffré sur un portable", "Activer BitLocker : en cas de vol, les données resteront illisibles."),
            _ => SecurityCheck.Info(name, $"Disque {drive} non chiffré"),
        };
    }

    private static SecurityCheck SecureBoot() => Firmware.SecureBootEnabled() switch
    {
        true => SecurityCheck.Ok("Secure Boot", "Activé"),
        false => SecurityCheck.Warning("Secure Boot", "Pris en charge mais désactivé", "Activer Secure Boot dans le BIOS : il bloque les logiciels malveillants au démarrage."),
        null => SecurityCheck.Warning("Secure Boot", "Non pris en charge (démarrage Legacy)", "Passer en démarrage UEFI si la carte mère le permet."),
    };

    private static SecurityCheck Tpm()
    {
        const string name = "Puce TPM";
        var tpm = Wmi.QueryOptional("SELECT SpecVersion, IsEnabled_InitialValue FROM Win32_Tpm", @"root\CIMV2\Security\MicrosoftTpm").FirstOrDefault();
        if (tpm is null) return SecurityCheck.Warning(name, "Aucun TPM détecté", "Activer le TPM dans le BIOS (fTPM chez AMD, PTT chez Intel) s'il existe.");
        var version = tpm.Str("SpecVersion")?.Split(',').FirstOrDefault()?.Trim() ?? "?";
        if (tpm.Bool("IsEnabled_InitialValue") != true) return SecurityCheck.Warning(name, $"TPM {version} désactivé", "Activer le TPM dans le BIOS.");
        return version == "2.0" ? SecurityCheck.Ok(name, "TPM 2.0 activé") : SecurityCheck.Warning(name, $"TPM {version} (ancienne version)", "Le TPM 2.0 est requis par Windows 11 : PC à remplacer à terme.", CheckKind.Change);
    }

    private static SecurityCheck Smb1()
    {
        const string name = "Protocole SMB1";
        var feature = Wmi.QueryOptional("SELECT InstallState FROM Win32_OptionalFeature WHERE Name = 'SMB1Protocol'").FirstOrDefault();
        return feature?.ULong("InstallState") == 1
            ? SecurityCheck.Danger(name, "Activé (vieux protocole de partage utilisé par WannaCry)", "Désactiver SMB1 dans « Fonctionnalités de Windows », sauf vieux NAS ou imprimante qui en dépend.")
            : SecurityCheck.Ok(name, "Désactivé");
    }

    private static SecurityCheck RemoteDesktop()
    {
        const string name = "Bureau à distance";
        using var ts = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server");
        if (ts?.GetValue("fDenyTSConnections") is not int deny || deny != 0) return SecurityCheck.Ok(name, "Désactivé");
        using var rdp = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp");
        if (rdp?.GetValue("UserAuthentication") is int nla && nla == 0)
            return SecurityCheck.Danger(name, "Activé sans authentification réseau (NLA)", "Désactiver le Bureau à distance s'il n'est pas utilisé, sinon activer la NLA.");
        return SecurityCheck.Warning(name, "Activé", "Désactiver le Bureau à distance s'il n'est pas utilisé.");
    }

    private static SecurityCheck Accounts()
    {
        const string name = "Comptes utilisateurs";
        var accounts = Wmi.QueryOptional("SELECT Name, SID, Disabled FROM Win32_UserAccount WHERE LocalAccount = TRUE")
            .Where(a => a.Bool("Disabled") != true)
            .Select(a => (Name: a.Str("Name") ?? "", Sid: a.Str("SID") ?? ""))
            .Where(a => a.Name.Length > 0)
            .ToList();

        var problems = new List<string>();
        var danger = false;
        if (accounts.Any(a => a.Sid.EndsWith("-501"))) { problems.Add("compte Invité activé"); danger = true; }
        if (accounts.Any(a => a.Sid.EndsWith("-500"))) problems.Add("compte Administrateur intégré activé");

        var blank = accounts.Where(a => !a.Sid.EndsWith("-501") && HasBlankPassword(a.Name)).Select(a => a.Name).ToList();
        if (blank.Count > 0) problems.Add("sans mot de passe : " + string.Join(", ", blank));

        if (problems.Count == 0) return SecurityCheck.Ok(name, $"{accounts.Count} compte(s) actif(s), tous protégés par un mot de passe ou un code PIN");
        var detail = string.Join(" · ", problems);
        return danger
            ? SecurityCheck.Danger(name, detail, "Désactiver le compte Invité et mettre un mot de passe sur chaque compte.")
            : SecurityCheck.Warning(name, detail, "Mettre un mot de passe (ou un code PIN Windows Hello) sur chaque compte.");
    }

    private static SecurityCheck AutoLogon()
    {
        const string name = "Connexion automatique";
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
        if (key?.GetValue("AutoAdminLogon") as string != "1") return SecurityCheck.Ok(name, "Désactivée");
        var user = key.GetValue("DefaultUserName") as string;
        return key.GetValue("DefaultPassword") is string pwd && pwd.Length > 0
            ? SecurityCheck.Danger(name, $"Activée pour {user}, mot de passe stocké en clair dans le registre", "Désactiver la connexion automatique (netplwiz) ou utiliser Autologon de Sysinternals.")
            : SecurityCheck.Warning(name, $"Activée pour {user}", "Toute personne qui allume le PC accède à la session : désactiver si le PC sort du domicile.");
    }

    /// <summary>
    /// Un mot de passe vide est détecté par une tentative d'ouverture de session avec un mot de passe vide :
    /// Windows répond « restriction de compte » (1327) quand le mot de passe est réellement vide.
    /// </summary>
    private static bool HasBlankPassword(string user)
    {
        if (LogonUser(user, ".", "", LOGON32_LOGON_NETWORK, LOGON32_PROVIDER_DEFAULT, out var token))
        {
            CloseHandle(token);
            return true;
        }
        return Marshal.GetLastWin32Error() == ERROR_ACCOUNT_RESTRICTION;
    }

    private const int LOGON32_LOGON_NETWORK = 3;
    private const int LOGON32_PROVIDER_DEFAULT = 0;
    private const int ERROR_ACCOUNT_RESTRICTION = 1327;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(string user, string domain, string password, int logonType, int provider, out IntPtr token);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
