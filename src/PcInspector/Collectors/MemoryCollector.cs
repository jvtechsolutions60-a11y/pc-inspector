using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

public sealed class MemoryCollector : ICollector
{
    public string Title => "Mémoire";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        var modules = Wmi.Query("SELECT * FROM Win32_PhysicalMemory");

        var os = Wmi.Query("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem").FirstOrDefault();
        var array = Wmi.QueryOptional("SELECT MemoryDevices, MaxCapacityEx FROM Win32_PhysicalMemoryArray").FirstOrDefault();
        ulong installed = modules.Aggregate(0UL, (sum, m) => sum + (m.ULong("Capacity") ?? 0));
        var total = os?.ULong("TotalVisibleMemorySize") * 1024;
        var free = os?.ULong("FreePhysicalMemory") * 1024;

        section.Groups.Add(new InfoGroup("Vue d'ensemble")
            .Add("Installée", Wmi.FormatBytes(installed))
            .Add("Utilisable", Wmi.FormatBytes(total))
            .Add("Utilisée", total is { } t and > 0 && free is { } f ? $"{Wmi.FormatBytes(t - f)} ({(t - f) * 100 / t} %)" : null)
            .Add("Type", modules.Select(m => MemoryType(m.ULong("SMBIOSMemoryType"))).FirstOrDefault(t => t is not null))
            .Add("Emplacements", array?.ULong("MemoryDevices") is { } slots ? $"{modules.Count} utilisé(s) sur {slots}" : $"{modules.Count} utilisé(s)")
            .Add("Maximum supporté", array?.ULong("MaxCapacityEx") is { } max and > 0 ? Wmi.FormatBytes(max * 1024) : null));

        foreach (var module in modules)
        {
            var title = module.Str("DeviceLocator") ?? module.Str("BankLabel") ?? "Barrette";
            var configured = module.ULong("ConfiguredClockSpeed");
            var rated = module.ULong("Speed");
            section.Groups.Add(new InfoGroup(title)
                .Add("Capacité", Wmi.FormatBytes(module.ULong("Capacity")))
                .Add("Type", MemoryType(module.ULong("SMBIOSMemoryType")))
                .Add("Vitesse", configured is > 0 ? $"{configured} MT/s" : rated is > 0 ? $"{rated} MT/s" : null)
                .Add("Vitesse nominale", rated is > 0 ? $"{rated} MT/s" : null)
                .Add("Fabricant", module.Str("Manufacturer"))
                .Add("Référence", module.Str("PartNumber"))
                .Add("Numéro de série", module.Str("SerialNumber"))
                .Add("Banque", module.Str("BankLabel")));
        }

        return section;
    }

    private static string? MemoryType(ulong? smbiosType) => smbiosType switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => null,
    };
}
