using System;
using System.Collections.Generic;
using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>
/// Écran d'accueil "Bilan" : passe en revue les points qu'un dépanneur vérifie en premier
/// et donne une note globale. Le détail de chaque point reste dans son onglet.
/// </summary>
public sealed class HealthCollector : ICollector
{
    private const ulong Gb = 1024UL * 1024 * 1024;
    private static readonly TimeSpan CrashWindow = TimeSpan.FromDays(30);

    public string Title => "Bilan";
    public string Icon => "\uE95E";

    /// <summary>Dernier bilan calculé, affiché par l'écran d'accueil.</summary>
    public HealthReport? Report { get; private set; }

    public InfoSection Collect()
    {
        var report = Evaluate();
        Report = report;

        // Version texte du bilan pour le rapport client.
        var section = new InfoSection(Title);
        section.Groups.Add(new InfoGroup("Note globale")
            .Add("Note", $"{report.Score}/100 · {report.Verdict}")
            .Add("Résultat", report.Counts));
        var points = new InfoGroup("Points vérifiés");
        foreach (var finding in report.Findings)
            points.Add(finding.Area, $"{finding.Mark} {finding.Message}");
        section.Groups.Add(points);
        return section;
    }

    public static HealthReport Evaluate()
    {
        var findings = new List<Finding>();

        // winget peut prendre plusieurs secondes : il tourne pendant les autres contrôles.
        var winget = Winget.ReadUpdatesAsync();

        // Un contrôle qui échoue (classe WMI absente, accès refusé) ne doit pas bloquer les autres.
        void Run(Action check)
        {
            try { check(); }
            catch (Exception ex) when (ex is System.Management.ManagementException
                                          or System.Runtime.InteropServices.COMException
                                          or UnauthorizedAccessException
                                          or InvalidOperationException) { }
        }

        void Add(Severity severity, string area, string message, string tab) =>
            findings.Add(new Finding(severity, area, message, tab));

        Run(() => Disks(Add));
        Run(() => SystemDriveSpace(Add));
        Run(() => Memory(Add));
        Run(() => Battery(Add));
        Run(() => Devices(Add));
        Run(() => Crashes(Add));
        Run(() => Antivirus(Add));
        Run(() => Firewall(Add));
        Run(() => Activation(Add));
        Run(() => Updates(Add));
        Run(() => WindowsVersion(Add));
        SoftwareUpdates(winget.Result, Add);

        return new HealthReport(findings);
    }

    private delegate void AddFinding(Severity severity, string area, string message, string tab);

    private static void Disks(AddFinding add)
    {
        var counters = Wmi.QueryOptional("SELECT DeviceId, Temperature, Wear FROM MSFT_StorageReliabilityCounter", Wmi.StorageScope)
            .Where(r => r.Str("DeviceId") is not null)
            .GroupBy(r => r.Str("DeviceId")!)
            .ToDictionary(g => g.Key, g => g.First());

        var disks = Wmi.QueryOptional("SELECT DeviceId, FriendlyName, MediaType, HealthStatus FROM MSFT_PhysicalDisk", Wmi.StorageScope);
        if (disks.Count == 0) return;

        var systemLetter = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd(':');
        var systemDisk = Wmi.QueryOptional($"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '{systemLetter}'", Wmi.StorageScope)
            .FirstOrDefault()?.Str("DiskNumber");

        var smart = DiskHealth.ReadAll();

        bool problem = false;
        foreach (var disk in disks)
        {
            var name = disk.Str("FriendlyName") ?? "Disque";
            var id = disk.Str("DeviceId");
            switch (disk.ULong("HealthStatus"))
            {
                case 2:
                    add(Severity.Critical, "Disque", $"{name} est en mauvais état : sauvegarder immédiatement et le remplacer.", "Stockage");
                    problem = true;
                    break;
                case 1:
                    add(Severity.Warning, "Disque", $"{name} signale un avertissement : sauvegarder et prévoir son remplacement.", "Stockage");
                    problem = true;
                    break;
            }

            // Les compteurs SMART repèrent une panne qui arrive alors que Windows affiche encore « sain ».
            if (id is not null && smart.TryGetValue(id, out var health))
            {
                foreach (var (severity, message) in health.Problems)
                {
                    add(severity, "Disque", $"{name} {message}", "Stockage");
                    problem = true;
                }
            }

            if (id is not null && counters.TryGetValue(id, out var c))
            {
                if (c.ULong("Wear") is >= 100 and var worn)
                {
                    add(Severity.Critical, "Disque", $"{name} est usé à {worn} %, au-delà de sa durée de vie prévue : sauvegarder et le remplacer.", "Stockage");
                    problem = true;
                }
                else if (c.ULong("Wear") is >= 80 and var wear)
                {
                    add(Severity.Warning, "Disque", $"{name} est usé à {wear} % : prévoir son remplacement.", "Stockage");
                    problem = true;
                }
                if (c.ULong("Temperature") is > 60 and var temp)
                {
                    add(Severity.Warning, "Température", $"{name} chauffe ({temp} °C) : vérifier la ventilation.", "Températures");
                    problem = true;
                }
            }

            if (id == systemDisk && disk.ULong("MediaType") == 3)
            {
                add(Severity.Warning, "Disque système", $"{name} est un disque dur mécanique : un SSD rendra le PC beaucoup plus rapide.", "Stockage");
                problem = true;
            }
        }

        var checkedSmart = smart.Count > 0 ? " (compteurs SMART vérifiés)" : "";
        if (!problem) add(Severity.Ok, "Disque", (disks.Count == 1 ? "Disque en bon état" : $"{disks.Count} disques en bon état") + checkedSmart + ".", "Stockage");
    }

    private static void SystemDriveSpace(AddFinding add)
    {
        var letter = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var volume = Wmi.Query($"SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DeviceID = '{letter}'").FirstOrDefault();
        if (volume?.ULong("Size") is not { } size || size == 0 || volume.ULong("FreeSpace") is not { } free) return;

        var percent = free * 100 / size;
        var text = $"{Wmi.FormatBytes(free)} libres sur {Wmi.FormatBytes(size)} ({percent} %)";
        if (percent < 5) add(Severity.Critical, "Espace disque", $"{letter} presque plein, {text} : nettoyer ou agrandir le disque.", "Stockage");
        else if (percent < 15) add(Severity.Warning, "Espace disque", $"{letter} se remplit, {text} : prévoir un nettoyage.", "Stockage");
        else add(Severity.Ok, "Espace disque", $"{letter} : {text}.", "Stockage");
    }

    private static void Memory(AddFinding add)
    {
        var ram = Wmi.Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault()?.ULong("TotalPhysicalMemory") ?? 0;
        if (ram == 0) return;
        var text = Wmi.FormatBytes(ram);
        // La mémoire réservée au matériel fait afficher un peu moins que la quantité installée.
        if (ram < 3.5 * Gb) add(Severity.Critical, "Mémoire", $"{text} seulement : insuffisant pour Windows, passer à 8 Go minimum.", "Mémoire");
        else if (ram < 7.5 * Gb) add(Severity.Warning, "Mémoire", $"{text} : passer à 8 ou 16 Go pour un usage confortable.", "Mémoire");
        else add(Severity.Ok, "Mémoire", $"{text} de mémoire.", "Mémoire");
    }

    private static void Battery(AddFinding add)
    {
        if (BatteryCollector.WearPercent() is not { } wear) return;
        if (wear >= 60) add(Severity.Critical, "Batterie", $"Usée à {wear:0} % : la remplacer.", "Batterie");
        else if (wear >= 40) add(Severity.Warning, "Batterie", $"Usée à {wear:0} % : proposer son remplacement.", "Batterie");
        else add(Severity.Ok, "Batterie", $"Usure de {Math.Max(0, wear):0} %.", "Batterie");
    }

    private static void Devices(AddFinding add)
    {
        var errors = Wmi.Query("SELECT Name FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
        if (errors.Count == 0)
        {
            add(Severity.Ok, "Périphériques", "Aucun périphérique en erreur.", "Diagnostic");
            return;
        }
        var names = string.Join(", ", errors.Select(e => e.Str("Name")).Where(n => !string.IsNullOrEmpty(n)).Take(3));
        add(Severity.Warning, "Périphériques", $"{errors.Count} en erreur" + (names.Length > 0 ? $" ({names}{(errors.Count > 3 ? "…" : "")})" : "") + " : vérifier les pilotes.", "Diagnostic");
    }

    private static void Crashes(AddFinding add)
    {
        if (DiagnosticCollector.RecentCrashCount(CrashWindow) is not { } count) return;
        if (count >= 3) add(Severity.Critical, "Plantages", $"{count} écrans bleus ou arrêts brutaux en 30 jours : chercher la cause (RAM, disque, pilote, alimentation).", "Diagnostic");
        else if (count > 0) add(Severity.Warning, "Plantages", $"{count} écran(s) bleu(s) ou arrêt(s) brutal(aux) en 30 jours.", "Diagnostic");
        else add(Severity.Ok, "Plantages", "Aucun plantage en 30 jours.", "Diagnostic");
    }

    private static void Antivirus(AddFinding add)
    {
        var products = Wmi.QueryOptional("SELECT displayName, productState FROM AntiVirusProduct", @"root\SecurityCenter2");
        if (products.Count == 0)
        {
            add(Severity.Critical, "Antivirus", "Aucun antivirus détecté : réactiver Windows Defender ou installer une protection.", "Sécurité");
            return;
        }

        // productState : octet du milieu = activé (0x10), octet bas = définitions à jour (0x00).
        var active = products.Where(av => (((av.ULong("productState") ?? 0) >> 8) & 0xFF) is 0x10 or 0x11).ToList();
        if (active.Count == 0)
        {
            add(Severity.Critical, "Antivirus", "Aucun antivirus actif : réactiver Windows Defender ou installer une protection.", "Sécurité");
            return;
        }

        var outdated = active.Where(av => ((av.ULong("productState") ?? 0) & 0xFF) != 0x00).ToList();
        var name = (outdated.Count > 0 ? outdated : active)[0].Str("displayName") ?? "L'antivirus";
        if (outdated.Count > 0) add(Severity.Warning, "Antivirus", $"{name} est activé mais ses définitions sont périmées : lancer une mise à jour.", "Sécurité");
        else add(Severity.Ok, "Antivirus", $"{name} activé et à jour.", "Sécurité");
    }

    private static void Firewall(AddFinding add)
    {
        var off = SecurityCollector.DisabledFirewallProfiles();
        if (off.Count == 0)
        {
            add(Severity.Ok, "Pare-feu", "Pare-feu Windows activé.", "Sécurité");
            return;
        }
        var thirdParty = Wmi.QueryOptional("SELECT displayName FROM FirewallProduct", @"root\SecurityCenter2").FirstOrDefault()?.Str("displayName");
        if (thirdParty is not null) add(Severity.Ok, "Pare-feu", $"Pare-feu {thirdParty} en place.", "Sécurité");
        else add(Severity.Critical, "Pare-feu", "Pare-feu Windows désactivé pour : " + string.Join(", ", off) + ". Le réactiver.", "Sécurité");
    }

    private static void Activation(AddFinding add)
    {
        switch (SecurityCollector.IsWindowsActivated())
        {
            case true: add(Severity.Ok, "Licence", "Windows est activé.", "Sécurité"); break;
            case false: add(Severity.Warning, "Licence", "Windows n'est pas activé.", "Sécurité"); break;
        }
    }

    private static void Updates(AddFinding add)
    {
        // La liste des mises à jour en attente (longue à obtenir) reste dans l'onglet Sécurité.
        if (SecurityCollector.IsRebootPending())
            add(Severity.Warning, "Mises à jour", "Un redémarrage est nécessaire pour terminer des mises à jour.", "Sécurité");
    }

    private static void SoftwareUpdates(Winget.Result result, AddFinding add)
    {
        // Sans winget, rien n'est affiché : l'onglet explique comment l'installer.
        if (result.Updates is not { } updates) return;
        const string tab = "Mises à jour logiciels";
        if (updates.Count == 0)
        {
            add(Severity.Ok, "Logiciels", "Logiciels à jour (vérifié avec winget).", tab);
            return;
        }
        var names = string.Join(", ", updates.Take(3).Select(u => u.Name));
        add(Severity.Warning, "Logiciels",
            $"{updates.Count} logiciel(s) à mettre à jour ({names}{(updates.Count > 3 ? "…" : "")}) : commande conseillée « winget upgrade --all ».", tab);
    }

    private static void WindowsVersion(AddFinding add)
    {
        var os = Wmi.Query("SELECT Caption, BuildNumber FROM Win32_OperatingSystem").FirstOrDefault();
        if (!int.TryParse(os?.Str("BuildNumber"), out var build)) return;
        if (build >= 22000)
        {
            add(Severity.Ok, "Windows", $"{os!.Str("Caption")?.Replace("Microsoft ", "")} installé.", "Système");
            return;
        }
        // Les versions serveur et plus anciennes ne sont pas évaluées ici.
        if (os!.Str("Caption")?.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) is not >= 0) return;

        var advice = Windows11Collector.IsCompatible() switch
        {
            true => "ce PC est compatible, proposer la mise à niveau gratuite vers Windows 11.",
            false => "ce PC n'est pas compatible Windows 11, proposer un remplacement ou les mises à jour étendues (ESU).",
            null => "vérifier la compatibilité Windows 11 (processeur non reconnu).",
        };
        add(Severity.Warning, "Windows", "Windows 10 n'est plus maintenu par Microsoft depuis octobre 2025 : " + advice, "Windows 11");
    }
}
