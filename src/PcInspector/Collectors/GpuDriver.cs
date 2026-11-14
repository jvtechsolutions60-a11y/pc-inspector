using System;
using System.Linq;
using System.Management;
using Microsoft.Win32;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Verdict sur le pilote d'une carte graphique : version lisible, âge, conseil.</summary>
internal sealed record GpuDriverVerdict(string Vendor, string? Version, int? AgeDays, Severity Severity, string Message, string? DownloadUrl);

/// <summary>
/// Le pilote de la carte graphique est-il récent ? Hors ligne et sans rien installer : on lit la version et la date du
/// pilote installé (WMI), on les juge, et on donne le lien officiel du fabricant. Aucun site de fabricant ne publie
/// la « dernière version » de façon fiable pour un programme : l'âge du pilote sert de repère.
/// </summary>
internal static class GpuDriver
{
    public const string NvidiaUrl = "https://www.nvidia.com/fr-fr/drivers/";
    public const string AmdUrl = "https://www.amd.com/fr/support/download/drivers.html";
    public const string IntelUrl = "https://www.intel.fr/content/www/fr/fr/support/detect.html";

    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    /// <summary>Cartes à juger (les cartes virtuelles et de bureau à distance sont ignorées).</summary>
    public static System.Collections.Generic.List<GpuDriverVerdict> ReadAll(DateTime today)
    {
        var verdicts = new System.Collections.Generic.List<GpuDriverVerdict>();
        foreach (var gpu in Wmi.Query("SELECT Name, AdapterCompatibility, DriverVersion, DriverDate FROM Win32_VideoController"))
        {
            var name = gpu.Str("Name") ?? "";
            DateTime? date = null;
            var raw = gpu.Str("DriverDate");
            if (!string.IsNullOrEmpty(raw))
            {
                try { date = ManagementDateTimeConverter.ToDateTime(raw); }
                catch (ArgumentOutOfRangeException) { }
            }
            var verdict = Judge(name, gpu.Str("AdapterCompatibility"), gpu.Str("DriverVersion"), date, today, RadeonVersion(name));
            if (verdict is not null) verdicts.Add(verdict);
        }
        return verdicts;
    }

    /// <summary>
    /// Règles (sans réseau) :
    /// - pilote de base de Windows : urgent, la carte n'a pas son vrai pilote ;
    /// - NVIDIA ou AMD (cartes de jeu) : à jour sous 6 mois, à mettre à jour au-delà, très ancien au-delà d'un an ;
    /// - Intel et autres puces intégrées : signalé seulement au-delà de 2 ans.
    /// Null pour une carte virtuelle ou de bureau à distance (rien à juger).
    /// </summary>
    internal static GpuDriverVerdict? Judge(string name, string? compatibility, string? driverVersion, DateTime? driverDate, DateTime today, string? radeonVersion = null)
    {
        var text = (name + " " + compatibility).ToLowerInvariant();
        if (text.Contains("basic display") || text.Contains("carte vidéo de base") || text.Contains("affichage de base"))
            return new GpuDriverVerdict("Windows", driverVersion, null, Severity.Critical,
                "pilote de base de Windows : la carte graphique n'a pas son vrai pilote (jeux et écrans limités). Installer le pilote du fabricant.", null);
        if (text.Contains("remote") || text.Contains("hyper-v") || text.Contains("vmware") || text.Contains("virtualbox") || text.Contains("parsec") || text.Contains("indirect"))
            return null;

        var vendor = text.Contains("nvidia") || text.Contains("geforce") || text.Contains("quadro") ? "NVIDIA"
            : text.Contains("radeon") || text.Contains("amd") || text.Contains("advanced micro devices") ? "AMD"
            : text.Contains("intel") ? "Intel" : "Autre";
        var url = vendor switch { "NVIDIA" => NvidiaUrl, "AMD" => AmdUrl, "Intel" => IntelUrl, _ => null };
        var version = vendor switch
        {
            "NVIDIA" => NvidiaVersion(driverVersion) ?? driverVersion,
            "AMD" when !string.IsNullOrWhiteSpace(radeonVersion) => $"Adrenalin {radeonVersion}",
            _ => driverVersion,
        };
        var label = version is null ? "le pilote" : $"le pilote {version}";

        if (driverDate is not { } date || date.Year < 2000)
            return new GpuDriverVerdict(vendor, version, null, Severity.Warning, $"date de {label} inconnue : vérifier sur le site du fabricant.", url);

        var age = (int)(today.Date - date.Date).TotalDays;
        var when = date.ToString("dd/MM/yyyy");
        var gaming = vendor is "NVIDIA" or "AMD";
        var stable = vendor switch
        {
            "NVIDIA" => "pilote « Game Ready » ou « Studio » stable (application NVIDIA ou nvidia.com)",
            "AMD" => "pilote Adrenalin « Recommandé (WHQL) », pas la version optionnelle (logiciel AMD ou amd.com)",
            "Intel" => "pilote Intel (Intel Driver & Support Assistant)",
            _ => "pilote du fabricant",
        };
        if (gaming && age > 365)
            return new GpuDriverVerdict(vendor, version, age, Severity.Warning, $"{label} date du {when} (plus d'un an) : installer le dernier {stable}.", url);
        if (gaming && age > 180)
            return new GpuDriverVerdict(vendor, version, age, Severity.Warning, $"{label} date du {when} (plus de 6 mois) : pour les jeux récents, installer le dernier {stable}.", url);
        if (!gaming && age > 730)
            return new GpuDriverVerdict(vendor, version, age, Severity.Warning, $"{label} date du {when} (plus de 2 ans) : vérifier la mise à jour du {stable}.", url);
        return new GpuDriverVerdict(vendor, version, age, Severity.Ok, $"{label} date du {when} : récent.", url);
    }

    /// <summary>
    /// Numéro NVIDIA (ex. 560.94) tiré de la version Windows du pilote (ex. 32.0.15.6094) : les 5 derniers chiffres
    /// des deux derniers nombres. Null si la forme n'est pas reconnue.
    /// </summary>
    internal static string? NvidiaVersion(string? windowsVersion)
    {
        var parts = (windowsVersion ?? "").Split('.');
        if (parts.Length != 4 || !parts.All(p => p.Length > 0 && p.All(char.IsDigit))) return null;
        // Forme NVIDIA : 3e nombre sur 1 ou 2 chiffres (ex. 15), 4e sur 4 chiffres au plus (ex. 6094).
        if (parts[2].Length > 2 || parts[3].Length > 4) return null;
        var digits = parts[2] + parts[3].PadLeft(4, '0');
        if (digits.Length < 5) return null;
        var last = digits.Substring(digits.Length - 5);
        return $"{int.Parse(last.Substring(0, 3))}.{last.Substring(3)}";
    }

    /// <summary>Version « Adrenalin » écrite par le pilote AMD dans le registre (lecture seule).</summary>
    private static string? RadeonVersion(string gpuName)
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (classKey is null) return null;
            foreach (var subKeyName in classKey.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
            {
                using var adapter = classKey.OpenSubKey(subKeyName);
                if (adapter?.GetValue("DriverDesc") as string != gpuName) continue;
                return adapter.GetValue("RadeonSoftwareVersion") as string;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
        return null;
    }
}
