using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>
/// Températures disponibles sans pilote : cartes graphiques (comme le Gestionnaire des tâches), zones thermiques
/// ACPI (souvent proches du CPU) et disques. La sonde précise du CPU demande un pilote noyau : non installé, par choix.
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

        var gpus = new InfoGroup("Cartes graphiques");
        foreach (var gpu in GpuSensors.Read())
        {
            var level = gpu.TemperatureC >= 88 ? "critical" : gpu.TemperatureC >= 80 ? "warning" : "ok";
            gpus.AddBar(gpu.Name, $"{gpu.TemperatureC:0} °C" + (gpu.FanRpm is { } rpm ? $" · ventilateur {rpm} tr/min" : ""), gpu.TemperatureC / 100, level);
        }
        if (gpus.Items.Count > 0) section.Groups.Add(gpus);

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
            .Add("Processeur", "La sonde interne du processeur ne se lit qu'avec un pilote noyau (WinRing0 est signalé par les antivirus). "
                 + "PC Inspector n'installe rien : on garde les zones ACPI et, dans l'onglet Performances, le test gaming détecte la surchauffe par la baisse de fréquence. "
                 + "Pour la valeur exacte, lancer HWiNFO à côté.")
            .Add("Carte graphique", "Lue sans pilote supplémentaire (NVIDIA et AMD). Une carte intégrée au processeur n'a souvent pas de sonde, et la valeur au repos est normale entre 30 et 50 °C."));

        return section;
    }
}
