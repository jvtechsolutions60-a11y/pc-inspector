using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>
/// Températures disponibles sans pilote : zones thermiques ACPI (souvent proches du CPU) et disques.
/// Les sondes précises du CPU et du GPU demandent un pilote noyau, prévu pour plus tard.
/// </summary>
public sealed class TemperatureCollector : ICollector
{
    public string Title => "Températures";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);

        var zones = new InfoGroup("Zones thermiques (carte mère / CPU)");
        foreach (var zone in Wmi.QueryOptional("SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", Wmi.WmiScope))
        {
            // Valeur en dixièmes de kelvin.
            if (zone.ULong("CurrentTemperature") is not { } tenthsKelvin || tenthsKelvin == 0) continue;
            var celsius = tenthsKelvin / 10.0 - 273.15;
            var name = zone.Str("InstanceName")?.Split('\\').LastOrDefault() ?? "Zone";
            zones.Add(name, $"{celsius:0} °C");
        }
        if (zones.Items.Count > 0) section.Groups.Add(zones);

        var names = Wmi.QueryOptional("SELECT DeviceId, FriendlyName FROM MSFT_PhysicalDisk", Wmi.StorageScope)
            .Where(d => d.Str("DeviceId") is not null)
            .GroupBy(d => d.Str("DeviceId")!)
            .ToDictionary(g => g.Key, g => g.First().Str("FriendlyName"));

        var disks = new InfoGroup("Disques");
        foreach (var counter in Wmi.QueryOptional("SELECT DeviceId, Temperature FROM MSFT_StorageReliabilityCounter", Wmi.StorageScope))
        {
            if (counter.ULong("Temperature") is not { } t || t == 0) continue;
            var id = counter.Str("DeviceId") ?? "";
            disks.Add(names.TryGetValue(id, out var n) && n is not null ? n : $"Disque {id}", $"{t} °C");
        }
        if (disks.Items.Count > 0) section.Groups.Add(disks);

        if (section.Groups.Count == 0)
            section.Error = "Ce PC n'expose aucune sonde de température lisible sans pilote.";

        section.Groups.Add(new InfoGroup("À savoir")
            .Add("CPU et GPU", "Les températures précises du processeur et de la carte graphique demandent un pilote. Elles seront ajoutées dans une prochaine version."));

        return section;
    }
}
