using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

public sealed class BatteryCollector : ICollector
{
    public string Title => "Batterie";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        var batteries = Wmi.QueryOptional("SELECT * FROM Win32_Battery");

        if (batteries.Count == 0)
        {
            section.Groups.Add(new InfoGroup("Batterie").Add("Statut", "Aucune batterie détectée (PC fixe)"));
            return section;
        }

        var report = Wear();
        foreach (var battery in batteries)
        {
            var group = new InfoGroup(battery.Str("Name") ?? "Batterie")
                .Add("Charge actuelle", battery.ULong("EstimatedChargeRemaining") is { } c ? $"{c} %" : null)
                .Add("État", Status(battery.ULong("BatteryStatus")))
                .Add("Chimie", Chemistry(battery.ULong("Chemistry")));

            if (report.Designed is > 0 && report.Full is > 0)
            {
                var wear = 100.0 - report.Full.Value * 100.0 / report.Designed.Value;
                group.Add("Capacité d'origine", $"{report.Designed} mWh")
                     .Add("Capacité actuelle", $"{report.Full} mWh")
                     .Add("Usure", $"{(wear < 0 ? 0 : wear):0} %" + (wear >= 40 ? " · batterie à remplacer" : ""));
            }
            group.Add("Cycles de charge", report.Cycles is > 0 ? report.Cycles.ToString() : null);
            section.Groups.Add(group);
        }

        return section;
    }

    private static (ulong? Designed, ulong? Full, ulong? Cycles) Wear()
    {
        var designed = Wmi.QueryOptional("SELECT DesignedCapacity FROM BatteryStaticData", Wmi.WmiScope).FirstOrDefault()?.ULong("DesignedCapacity");
        var full = Wmi.QueryOptional("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", Wmi.WmiScope).FirstOrDefault()?.ULong("FullChargedCapacity");
        var cycles = Wmi.QueryOptional("SELECT CycleCount FROM BatteryCycleCount", Wmi.WmiScope).FirstOrDefault()?.ULong("CycleCount");
        return (designed, full, cycles);
    }

    /// <summary>Nombre de cycles de charge, quand la batterie le donne.</summary>
    public static ulong? Cycles() => Wear().Cycles is > 0 and var cycles ? cycles : null;

    /// <summary>Pourcentage d'usure, utilisé aussi par les conseils.</summary>
    public static double? WearPercent()
    {
        var (designed, full, _) = Wear();
        if (designed is not > 0 || full is not > 0) return null;
        return 100.0 - full.Value * 100.0 / designed.Value;
    }

    private static string? Status(ulong? status) => status switch
    {
        1 => "Sur batterie",
        2 => "Sur secteur",
        3 => "Chargée",
        4 => "Faible",
        5 => "Critique",
        6 or 7 or 8 or 9 => "En charge",
        11 => "Partiellement chargée",
        _ => null,
    };

    private static string? Chemistry(ulong? chemistry) => chemistry switch
    {
        3 => "Plomb",
        4 => "Nickel-cadmium",
        5 => "Nickel-métal hydrure",
        6 => "Lithium-ion",
        8 => "Lithium-polymère",
        _ => null,
    };
}
