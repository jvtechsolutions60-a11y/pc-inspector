using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using Microsoft.Win32;
using PcInspector.Collectors;

namespace PcSecurite.Checks;

/// <summary>Protections avancées de Windows et du démarrage, souvent désactivées sans que le client le sache.</summary>
public static partial class SecurityChecks
{
    private static SecurityCheck Defender()
    {
        const string name = "Microsoft Defender";
        const string scope = @"root\Microsoft\Windows\Defender";
        var status = Wmi.QueryOptional("SELECT * FROM MSFT_MpComputerStatus", scope).FirstOrDefault();
        if (status is null) return SecurityCheck.Info(name, "Non disponible");
        var mode = status.Str("AMRunningMode") ?? "Normal";
        if (status.Bool("AMServiceEnabled") != true || !mode.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            return SecurityCheck.Info(name, "En retrait : un autre antivirus assure la protection");

        var problems = new List<string>();
        var danger = false;
        if (status.Bool("RealTimeProtectionEnabled") == false) { problems.Add("protection en temps réel désactivée"); danger = true; }
        if (status.Bool("IsTamperProtected") == false) problems.Add("protection contre les falsifications désactivée");
        if (status.ULong("AntivirusSignatureAge") is { } age and > 7) problems.Add($"définitions vieilles de {age} jours");

        var preferences = Wmi.QueryOptional("SELECT PUAProtection, ExclusionPath, ExclusionProcess FROM MSFT_MpPreference", scope).FirstOrDefault();
        if (preferences?.ULong("PUAProtection") == 0) problems.Add("blocage des applications indésirables désactivé");
        // Un logiciel malveillant s'ajoute souvent aux exclusions pour ne plus être analysé.
        var exclusions = new[] { "ExclusionPath", "ExclusionProcess" }
            .Select(p => preferences?.Str(p))
            .Where(e => !string.IsNullOrWhiteSpace(e) && !e!.StartsWith("N/A", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exclusions.Count > 0) problems.Add("exclusions : " + string.Join(", ", exclusions));

        if (problems.Count == 0) return SecurityCheck.Ok(name, "Toutes les protections sont actives");
        const string advice = "Dans Sécurité Windows, réactiver les protections désactivées et supprimer les exclusions que le client ne connaît pas.";
        var detail = string.Join(" · ", problems);
        return danger ? SecurityCheck.Danger(name, detail, advice) : SecurityCheck.Warning(name, detail, advice);
    }

    private static SecurityCheck MemoryIntegrity()
    {
        const string name = "Intégrité de la mémoire";
        var guard = Wmi.QueryOptional("SELECT SecurityServicesRunning FROM Win32_DeviceGuard", @"root\Microsoft\Windows\DeviceGuard").FirstOrDefault();
        if (guard is null) return SecurityCheck.Info(name, "Non disponible sur cette version de Windows");
        // 1 = Credential Guard, 2 = intégrité de la mémoire (HVCI).
        var running = (guard.Str("SecurityServicesRunning") ?? "").Split(',').Select(s => s.Trim()).ToList();
        return running.Contains("2")
            ? SecurityCheck.Ok(name, "Activée (les pilotes malveillants ne peuvent pas modifier le noyau)")
            : SecurityCheck.Warning(name, "Désactivée",
                "Activer « Intégrité de la mémoire » dans Sécurité Windows > Sécurité de l'appareil > Isolation du noyau. Si elle est grisée : activer la virtualisation dans le BIOS ou mettre à jour le pilote incompatible signalé.");
    }

    private static SecurityCheck LsaProtection()
    {
        const string name = "Protection LSA";
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa");
        return key?.GetValue("RunAsPPL") is int ppl && ppl is 1 or 2
            ? SecurityCheck.Ok(name, "Activée (les mots de passe de session ne peuvent pas être aspirés)")
            : SecurityCheck.Warning(name, "Désactivée : un logiciel malveillant peut voler les mots de passe de session (type Mimikatz)",
                "Activer « Protection de l'autorité de sécurité locale » dans Sécurité Windows > Sécurité de l'appareil > Isolation du noyau, puis redémarrer.");
    }

    private static SecurityCheck DriverBlocklist()
    {
        const string name = "Blocage des pilotes vulnérables";
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Config");
        return key?.GetValue("VulnerableDriverBlocklistEnable") is int enabled && enabled == 0
            ? SecurityCheck.Warning(name, "Désactivé : des pilotes connus pour être utilisés par les rançongiciels peuvent se charger",
                "Réactiver « Liste de blocage des pilotes vulnérables Microsoft » dans Sécurité Windows > Isolation du noyau.")
            : SecurityCheck.Ok(name, "Activé");
    }

    private static SecurityCheck WDigest()
    {
        const string name = "Mots de passe en mémoire (WDigest)";
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\WDigest");
        return key?.GetValue("UseLogonCredential") is int value && value == 1
            ? SecurityCheck.Danger(name, "Mots de passe de session gardés en clair en mémoire (réglage typique d'une intrusion)",
                "Mettre UseLogonCredential à 0 dans le registre, redémarrer, faire une analyse antivirus complète et changer les mots de passe.")
            : SecurityCheck.Ok(name, "Désactivé");
    }

    private static SecurityCheck SecureBootCertificates()
    {
        const string name = "Certificats Secure Boot 2023";
        if (Firmware.SecureBootEnabled() != true) return SecurityCheck.Info(name, "Secure Boot inactif, non concerné");
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\Servicing");
        return (key?.GetValue("UEFICA2023Status") as string) switch
        {
            "Updated" => SecurityCheck.Ok(name, "Nouveaux certificats installés"),
            "InProgress" => SecurityCheck.Warning(name, "Installation des nouveaux certificats en cours", "Redémarrer le PC (parfois deux fois) pour terminer."),
            _ => SecurityCheck.Warning(name, "Certificats de 2011 expirés depuis juin 2026, ceux de 2023 ne sont pas installés",
                "Installer toutes les mises à jour Windows et le dernier BIOS du fabricant. Sinon le PC ne reçoit plus les correctifs de sécurité du démarrage."),
        };
    }

    private static SecurityCheck Bios()
    {
        const string name = "BIOS";
        var bios = Wmi.QueryOptional("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
        var raw = bios?.Str("ReleaseDate");
        if (string.IsNullOrEmpty(raw)) return SecurityCheck.Info(name, "Date inconnue");
        DateTime date;
        try { date = ManagementDateTimeConverter.ToDateTime(raw); }
        catch (ArgumentOutOfRangeException) { return SecurityCheck.Info(name, "Date inconnue"); }

        var years = (DateTime.Today - date).TotalDays / 365.25;
        var detail = $"Version {bios!.Str("SMBIOSBIOSVersion")} du {date:dd/MM/yyyy}";
        return years > 3
            ? SecurityCheck.Warning(name, $"{detail} (plus de {(int)years} ans)",
                "Chercher un BIOS plus récent sur le site du fabricant (failles du processeur, LogoFAIL, certificats Secure Boot). S'il n'y en a plus, le PC n'est plus suivi.")
            : SecurityCheck.Ok(name, detail);
    }

    private static SecurityCheck PowerShell2()
    {
        const string name = "PowerShell 2.0";
        var feature = Wmi.QueryOptional("SELECT InstallState FROM Win32_OptionalFeature WHERE Name = 'MicrosoftWindowsPowerShellV2Root'").FirstOrDefault();
        return feature?.ULong("InstallState") == 1
            ? SecurityCheck.Warning(name, "Installé (ancienne version sans journal, utilisée pour contourner l'antivirus)", "Le désactiver dans « Fonctionnalités de Windows ».")
            : SecurityCheck.Ok(name, "Absent ou désactivé");
    }
}
