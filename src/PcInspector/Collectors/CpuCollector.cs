using PcInspector.Models;

namespace PcInspector.Collectors;

public sealed class CpuCollector : ICollector
{
    public string Title => "Processeur";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);

        foreach (var cpu in Wmi.Query("SELECT * FROM Win32_Processor"))
        {
            var group = new InfoGroup(cpu.Str("Name") ?? "Processeur");
            group.Add("Fabricant", cpu.Str("Manufacturer"))
                 .Add("Socket", cpu.Str("SocketDesignation"))
                 .Add("Cœurs", cpu.Str("NumberOfCores"))
                 .Add("Threads", cpu.Str("NumberOfLogicalProcessors"))
                 .Add("Fréquence max", Mhz(cpu.ULong("MaxClockSpeed")))
                 .Add("Fréquence actuelle", Mhz(cpu.ULong("CurrentClockSpeed")))
                 .Add("Cache L2", Kb(cpu.ULong("L2CacheSize")))
                 .Add("Cache L3", Kb(cpu.ULong("L3CacheSize")))
                 .Add("Architecture", Architecture(cpu.ULong("Architecture")))
                 .Add("Virtualisation activée", Wmi.YesNo(cpu.Bool("VirtualizationFirmwareEnabled")))
                 .Add("Charge actuelle", cpu.ULong("LoadPercentage") is { } load ? $"{load} %" : null)
                 .Add("Identifiant", cpu.Str("ProcessorId"));
            section.Groups.Add(group);
        }

        return section;
    }

    private static string? Mhz(ulong? mhz) => mhz is null ? null : mhz >= 1000 ? $"{mhz / 1000.0:0.00} GHz" : $"{mhz} MHz";

    private static string? Kb(ulong? kb) => kb is null or 0 ? null : Wmi.FormatBytes(kb * 1024);

    private static string? Architecture(ulong? code) => code switch
    {
        0 => "x86",
        5 => "ARM",
        9 => "x64",
        12 => "ARM64",
        _ => code?.ToString(),
    };
}
