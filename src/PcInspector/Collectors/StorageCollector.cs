using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

public sealed class StorageCollector : ICollector
{
    public string Title => "Stockage";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);

        // Les compteurs SMART (température, usure, heures) sont reliés aux disques par DeviceId.
        var reliability = Wmi.QueryOptional("SELECT * FROM MSFT_StorageReliabilityCounter", Wmi.StorageScope)
            .Where(r => r.Str("DeviceId") is not null)
            .GroupBy(r => r.Str("DeviceId")!)
            .ToDictionary(g => g.Key, g => g.First());

        var disks = Wmi.QueryOptional("SELECT * FROM MSFT_PhysicalDisk", Wmi.StorageScope);
        foreach (var disk in disks.OrderBy(d => d.ULong("DeviceId")))
        {
            var group = new InfoGroup(disk.Str("FriendlyName") ?? "Disque")
                .Add("Type", MediaType(disk.ULong("MediaType"), disk.ULong("BusType")))
                .Add("Interface", BusType(disk.ULong("BusType")))
                .Add("Capacité", Wmi.FormatBytes(disk.ULong("Size")))
                .Add("État de santé", Health(disk.ULong("HealthStatus")))
                .Add("Numéro de série", disk.Str("SerialNumber"))
                .Add("Firmware", disk.Str("FirmwareVersion"));

            if (disk.Str("DeviceId") is { } id && reliability.TryGetValue(id, out var counters))
            {
                group.Add("Température", counters.ULong("Temperature") is > 0 and var t ? $"{t} °C" : null)
                     .Add("Température max", counters.ULong("TemperatureMax") is > 0 and var tm ? $"{tm} °C" : null)
                     .Add("Usure", counters.ULong("Wear") is { } wear ? $"{wear} %" : null)
                     .Add("Heures de fonctionnement", counters.ULong("PowerOnHours") is { } h ? $"{h} h" : null)
                     .Add("Démarrages", counters.Str("StartStopCycleCount"))
                     .Add("Erreurs de lecture", counters.Str("ReadErrorsTotal"))
                     .Add("Erreurs d'écriture", counters.Str("WriteErrorsTotal"));
            }

            section.Groups.Add(group);
        }

        // Repli pour les anciennes versions de Windows sans l'API de stockage moderne.
        if (disks.Count == 0)
        {
            foreach (var disk in Wmi.Query("SELECT * FROM Win32_DiskDrive"))
            {
                section.Groups.Add(new InfoGroup(disk.Str("Model") ?? "Disque")
                    .Add("Interface", disk.Str("InterfaceType"))
                    .Add("Capacité", Wmi.FormatBytes(disk.ULong("Size")))
                    .Add("État", disk.Str("Status"))
                    .Add("Numéro de série", disk.Str("SerialNumber"))
                    .Add("Firmware", disk.Str("FirmwareRevision")));
            }
        }

        var volumes = new InfoGroup("Partitions");
        foreach (var volume in Wmi.Query("SELECT * FROM Win32_LogicalDisk WHERE DriveType = 3"))
        {
            var size = volume.ULong("Size");
            var free = volume.ULong("FreeSpace");
            var label = volume.Str("VolumeName");
            volumes.Add(
                volume.Str("DeviceID") + (string.IsNullOrEmpty(label) ? "" : $" ({label})"),
                $"{Wmi.FormatBytes(free)} libres sur {Wmi.FormatBytes(size)} · {volume.Str("FileSystem")}");
        }
        if (volumes.Items.Count > 0) section.Groups.Add(volumes);

        return section;
    }

    private static string? MediaType(ulong? media, ulong? bus) => (media, bus) switch
    {
        (_, 17) => "SSD NVMe",
        (4, _) => "SSD",
        (3, _) => "Disque dur (HDD)",
        (5, _) => "SCM",
        _ => null,
    };

    private static string? BusType(ulong? bus) => bus switch
    {
        1 => "SCSI",
        3 => "ATA",
        7 => "USB",
        8 => "RAID",
        10 => "SAS",
        11 => "SATA",
        12 => "Carte SD",
        13 => "MMC",
        15 => "Virtuel",
        17 => "NVMe",
        _ => bus?.ToString(),
    };

    private static string? Health(ulong? status) => status switch
    {
        0 => "Bon",
        1 => "Avertissement",
        2 => "Mauvais",
        _ => null,
    };
}
