using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>
/// Devis d'upgrade indicatif : la barrette ou le SSD exactement compatibles avec ce PC, et une fourchette de prix.
/// Les prix sont modifiables dans prix.ini (bouton « Modifier les prix »).
/// </summary>
public sealed class UpgradeQuoteCollector : ICollector
{
    private const ulong Gb = 1024UL * 1024 * 1024;

    public string Title => "Devis d'upgrade";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        var prices = Prices.Load();
        var lines = new List<(string Label, string Reference, (int Min, int Max)? Price)>();

        Memory(section, prices, lines);
        Disk(section, prices, lines);

        var summary = new InfoGroup("Total indicatif");
        if (lines.Count == 0)
        {
            summary.Add("Résultat", "✔ Rien à prévoir : la mémoire et le disque sont suffisants pour ce PC.");
        }
        else
        {
            foreach (var (label, reference, price) in lines)
                summary.Add(label, reference + (price is { } p ? $" : {p.Min} à {p.Max} €" : ""));
            if (prices.Labour is { } labour) summary.Add("Main d'œuvre", $"{labour.Min} à {labour.Max} €");
            var known = lines.Where(l => l.Price is not null).Select(l => l.Price!.Value).ToList();
            if (prices.Labour is { } l2) known.Add(l2);
            if (known.Count > 0) summary.Add("Total", $"{known.Sum(p => p.Min)} à {known.Sum(p => p.Max)} € TTC environ");
        }
        summary.Add("Prix", "Fourchettes indicatives, à vérifier avant de donner un prix ferme.")
            .AddLink("Mes prix", "Modifier les prix et la main d'œuvre", Prices.EditLink);
        section.Groups.Insert(0, summary);
        return section;
    }

    private static void Memory(InfoSection section, Prices prices, List<(string, string, (int, int)?)> lines)
    {
        var modules = Wmi.Query("SELECT Capacity, SMBIOSMemoryType, FormFactor, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
        var total = modules.Sum(m => (double)(m.ULong("Capacity") ?? 0)) / Gb;
        var array = Wmi.QueryOptional("SELECT MemoryDevices, MaxCapacity, MaxCapacityEx FROM Win32_PhysicalMemoryArray").FirstOrDefault();
        var slots = (int)(array?.ULong("MemoryDevices") ?? 0);
        ulong maxKb = array?.ULong("MaxCapacityEx") ?? 0;
        if (maxKb == 0) maxKb = array?.ULong("MaxCapacity") ?? 0;
        var maxGb = maxKb / 1024.0 / 1024.0;
        var type = modules.Select(m => m.ULong("SMBIOSMemoryType")).FirstOrDefault(t => t is > 0);
        var formFactor = modules.Select(m => m.ULong("FormFactor")).FirstOrDefault(f => f is > 0);
        var speed = modules.Select(m => m.ULong("Speed") ?? m.ULong("ConfiguredClockSpeed")).FirstOrDefault(s => s is > 0);

        var group = new InfoGroup("Mémoire (RAM)")
            .Add("Installée", $"{total:0.#} Go sur {modules.Count} barrette(s)")
            .Add("Emplacements", slots > 0 ? $"{slots} au total, {Math.Max(0, slots - modules.Count)} libre(s)" : "Inconnu")
            .Add("Maximum de la carte mère", maxGb >= 1 ? $"{maxGb:0} Go" : "Inconnu");
        section.Groups.Add(group);

        var typeName = type switch { 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", _ => null };
        if (type is 27 or 28 or 29 or 30 or 35)
        {
            group.Add("Conseil", "Mémoire LPDDR soudée à la carte mère : pas d'ajout possible.");
            return;
        }
        if (total >= 15.5)
        {
            group.Add("Conseil", "✔ 16 Go ou plus : suffisant pour un usage courant.");
            return;
        }
        if (typeName is null)
        {
            group.Add("Conseil", "! Type de mémoire non reconnu : vérifier sur la fiche du fabricant avant de commander.");
            return;
        }

        var target = 16;
        if (maxGb >= 1 && target > maxGb) target = (int)maxGb;
        var free = slots - modules.Count;
        var form = formFactor == 12 ? "SO-DIMM" : "DIMM";
        var speedText = speed is { } s ? $" {s} MHz" : "";
        int size;
        int count;
        string action;
        if (free > 0)
        {
            // Ajouter une barrette identique : double canal et quantité doublée.
            size = Math.Max(4, (int)Math.Round(target - total));
            count = 1;
            action = $"Ajouter 1 barrette de {size} Go dans l'emplacement libre";
        }
        else
        {
            count = Math.Max(1, Math.Min(2, slots));
            size = Math.Max(4, target / count);
            action = count == 1 ? $"Remplacer la barrette par une de {size} Go" : $"Remplacer les barrettes par un kit de {count} × {size} Go";
        }

        var reference = $"{typeName} {form}{speedText} {size} Go" + (count > 1 ? $" (kit {count} × {size} Go)" : "");
        var price = prices.Ram(typeName, size) is { } unit ? (unit.Min * count, unit.Max * count) : ((int, int)?)null;
        var after = free > 0 ? total + size : size * count;
        group.Add("Conseil", $"{action} pour passer à {after:0} Go.")
            .Add("Référence à commander", reference)
            .AddLink("Comparer les prix", "Rechercher sur LDLC", "https://www.ldlc.com/recherche/" + Uri.EscapeDataString($"{typeName} {form}{speedText} {size}Go") + "/");
        lines.Add(("Mémoire", reference, price));
    }

    private static void Disk(InfoSection section, Prices prices, List<(string, string, (int, int)?)> lines)
    {
        var letter = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd(':');
        var number = Wmi.QueryOptional($"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '{letter}'", Wmi.StorageScope)
            .FirstOrDefault()?.Str("DiskNumber");
        var disk = Wmi.QueryOptional("SELECT DeviceId, FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk", Wmi.StorageScope)
            .FirstOrDefault(d => d.Str("DeviceId") == number);
        var volume = Wmi.Query($"SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DeviceID = '{letter}:'").FirstOrDefault();
        var size = (disk?.ULong("Size") ?? volume?.ULong("Size") ?? 0) / (double)Gb;
        double volumeSize = volume?.ULong("Size") ?? 0;
        double freeRatio = volumeSize > 0 ? (volume?.ULong("FreeSpace") ?? 0) * 100.0 / volumeSize : 100;
        var media = disk?.ULong("MediaType");
        var nvme = disk?.ULong("BusType") == 17;

        var group = new InfoGroup("Disque système")
            .Add("Disque", disk?.Str("FriendlyName") ?? "Inconnu")
            .Add("Type", media == 3 ? "Disque dur mécanique" : media == 4 ? (nvme ? "SSD NVMe (M.2)" : "SSD SATA") : "Inconnu")
            .Add("Capacité", size >= 1 ? $"{size:0} Go, {freeRatio:0} % libre" : "Inconnue");
        section.Groups.Add(group);

        string? reference = null;
        string kind = "SATA";
        int capacity = size > 400 ? 1000 : 500;
        if (media == 3)
        {
            reference = $"SSD SATA 2,5\" {Capacity(capacity)}";
            group.Add("Conseil", "! Remplacer le disque dur par un SSD : démarrage et ouverture des logiciels 3 à 5 fois plus rapides. Cloner le disque pour garder les données.");
        }
        else if (media == 4 && (size < 300 || freeRatio < 15))
        {
            kind = nvme ? "NVMe" : "SATA";
            capacity = size < 600 ? 1000 : 2000;
            reference = nvme ? $"SSD NVMe M.2 2280 {Capacity(capacity)}" : $"SSD SATA 2,5\" {Capacity(capacity)}";
            group.Add("Conseil", "! Disque presque plein ou petit : passer à un SSD plus grand (clonage du disque actuel).");
        }
        else
        {
            group.Add("Conseil", "✔ Rien à prévoir pour le disque.");
        }

        if (reference is null) return;
        group.Add("Référence à commander", reference)
            .AddLink("Comparer les prix", "Rechercher sur LDLC", "https://www.ldlc.com/recherche/" + Uri.EscapeDataString(reference.Replace("\"", "")) + "/");
        lines.Add(("Disque", reference, prices.Ssd(kind, capacity)));
    }

    private static string Capacity(int gb) => gb >= 1000 ? $"{gb / 1000} To" : $"{gb} Go";
}

/// <summary>Fourchettes de prix TTC, en euros, lues dans prix.ini (une ligne « clé=min-max »).</summary>
public sealed class Prices
{
    // Valeurs de départ, à ajuster : les prix de la mémoire et des SSD varient beaucoup d'un mois à l'autre.
    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["DDR3-4"] = "10-20", ["DDR3-8"] = "15-30",
        ["DDR4-4"] = "15-25", ["DDR4-8"] = "25-45", ["DDR4-16"] = "45-80",
        ["DDR5-8"] = "35-60", ["DDR5-16"] = "60-110",
        ["SSD-SATA-500"] = "40-60", ["SSD-SATA-1000"] = "65-100", ["SSD-SATA-2000"] = "120-180",
        ["SSD-NVMe-500"] = "40-65", ["SSD-NVMe-1000"] = "65-110", ["SSD-NVMe-2000"] = "120-190",
        ["Main-oeuvre"] = "",
    };

    private readonly Dictionary<string, string> _values;

    private Prices(Dictionary<string, string> values) => _values = values;

    public static string FilePath => Path.Combine(AppPaths.SettingsFolder, "prix.ini");

    /// <summary>Lien interne : la fenêtre crée prix.ini si besoin et l'ouvre dans le Bloc-notes.</summary>
    public const string EditLink = "jvtech:prix";

    public static Prices Load()
    {
        var values = new Dictionary<string, string>(Defaults, StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(FilePath))
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length == 2 && !line.TrimStart().StartsWith("#")) values[parts[0].Trim()] = parts[1].Trim();
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new Prices(values);
    }

    /// <summary>Crée prix.ini avec les valeurs actuelles s'il n'existe pas, pour que le technicien l'ouvre et l'ajuste.</summary>
    public static string EnsureFile()
    {
        if (File.Exists(FilePath)) return FilePath;
        AppPaths.EnsureFolder(AppPaths.SettingsFolder);
        var lines = new List<string>
        {
            "# Prix TTC en euros, « min-max ». Exemple : DDR4-8=25-45 (barrette DDR4 de 8 Go).",
            "# Main-oeuvre : ton tarif de pose, par exemple 30-50. Laisser vide pour ne pas l'afficher.",
        };
        lines.AddRange(Defaults.Select(d => $"{d.Key}={d.Value}"));
        File.WriteAllLines(FilePath, lines);
        return FilePath;
    }

    public (int Min, int Max)? Ram(string type, int sizeGb) => Range($"{type}-{sizeGb}");

    public (int Min, int Max)? Ssd(string kind, int capacityGb) => Range($"SSD-{kind}-{capacityGb}");

    public (int Min, int Max)? Labour => Range("Main-oeuvre");

    private (int Min, int Max)? Range(string key)
    {
        if (!_values.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Split('-');
        if (parts.Length != 2
            || !int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var min)
            || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var max)) return null;
        return (min, max);
    }
}
