using System;
using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Onglet "Résumé" : tout le matériel du PC sur une seule page, le détail restant dans chaque onglet.</summary>
public sealed class SummaryCollector : ICollector
{
    public string Title => "Résumé";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);

        // Ordinateur
        var pc = new InfoGroup("Ordinateur")
            .Add("Modèle", MachineIdentity.Model())
            .Add("N° de série", MachineIdentity.Serial());
        var os = Wmi.Query("SELECT Caption, Version, OSArchitecture FROM Win32_OperatingSystem").FirstOrDefault();
        pc.Add("Système", os is null ? null : $"{os.Str("Caption")?.Replace("Microsoft ", "")} {os.Str("OSArchitecture")} ({os.Str("Version")})");
        section.Groups.Add(pc);

        // Processeur
        foreach (var cpu in Wmi.Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
        {
            var mhz = cpu.ULong("MaxClockSpeed");
            section.Groups.Add(new InfoGroup("Processeur")
                .Add("Modèle", cpu.Str("Name"))
                .Add("Cœurs / threads", $"{cpu.ULong("NumberOfCores")} cœurs · {cpu.ULong("NumberOfLogicalProcessors")} threads")
                .Add("Fréquence de base", mhz is > 0 ? $"{mhz / 1000.0:0.00} GHz" : null));
        }

        // Carte mère et BIOS
        var board = Wmi.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        var bios = Wmi.Query("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
        var biosDate = bios?.Str("ReleaseDate") is { Length: >= 8 } d ? $" du {d.Substring(6, 2)}/{d.Substring(4, 2)}/{d.Substring(0, 4)}" : "";
        section.Groups.Add(new InfoGroup("Carte mère")
            .Add("Modèle", board is null ? null : $"{board.Str("Manufacturer")} {board.Str("Product")}")
            .Add("BIOS", bios is null ? null : $"{bios.Str("SMBIOSBIOSVersion")}{biosDate} · {(Firmware.IsUefi() ? "UEFI" : "Legacy")}")
            .Add("Secure Boot", Firmware.SecureBootText()));

        // Mémoire
        var modules = Wmi.Query("SELECT Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
        var total = modules.Aggregate(0UL, (sum, m) => sum + (m.ULong("Capacity") ?? 0));
        var slots = Wmi.QueryOptional("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray").FirstOrDefault()?.ULong("MemoryDevices");
        var first = modules.FirstOrDefault();
        var speed = first?.ULong("ConfiguredClockSpeed") is > 0 and var s ? s : first?.ULong("Speed");
        var ram = new InfoGroup("Mémoire")
            .Add("Total", total > 0 ? Wmi.FormatBytes(total) : null)
            .Add("Type", string.Join(" · ", new[] { MemoryCollector.MemoryType(first?.ULong("SMBIOSMemoryType")), speed is > 0 ? $"{speed} MT/s" : null }.Where(x => x is not null)))
            .Add("Barrettes", modules.Count == 0 ? null
                : string.Join(" + ", modules.Select(m => Wmi.FormatBytes(m.ULong("Capacity"))))
                  + (slots is { } n && n >= (ulong)modules.Count ? $" ({(ulong)modules.Count}/{n} emplacements occupés)" : ""));
        section.Groups.Add(ram);

        // Carte graphique et écrans
        var gpu = new InfoGroup("Graphique");
        foreach (var card in Wmi.Query("SELECT Name, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController"))
        {
            var name = card.Str("Name") ?? "Carte graphique";
            var vram = GpuCollector.VideoMemory(name);
            gpu.Add("Carte", name + (vram is > 0 ? $" ({Wmi.FormatBytes(vram)})" : ""));
            if (card.ULong("CurrentHorizontalResolution") is > 0 and var w)
                gpu.Add("Affichage", $"{w} × {card.ULong("CurrentVerticalResolution")} à {card.ULong("CurrentRefreshRate")} Hz");
        }
        section.Groups.Add(gpu);

        // Stockage
        var storage = new InfoGroup("Stockage");
        var physical = Wmi.QueryOptional("SELECT FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk", Wmi.StorageScope);
        if (physical.Count > 0)
        {
            foreach (var disk in physical)
            {
                var health = disk.ULong("HealthStatus") switch { 0 => "bon état", 1 => "avertissement", 2 => "mauvais état", _ => null };
                storage.Add(StorageCollector.MediaType(disk.ULong("MediaType"), disk.ULong("BusType")) ?? "Disque",
                    $"{disk.Str("FriendlyName")} · {Wmi.FormatBytes(disk.ULong("Size"))}" + (health is null ? "" : $" · {health}"));
            }
        }
        else
        {
            foreach (var disk in Wmi.Query("SELECT Model, Size FROM Win32_DiskDrive"))
                storage.Add("Disque", $"{disk.Str("Model")} ({Wmi.FormatBytes(disk.ULong("Size"))})");
        }
        section.Groups.Add(storage);

        // Réseau
        var network = new InfoGroup("Réseau");
        foreach (var adapter in Wmi.Query("SELECT Name, NetConnectionID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE"))
        {
            var name = adapter.Str("Name") ?? "";
            if (name.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Bluetooth Device", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            network.Add(adapter.Str("NetConnectionID") ?? "Carte", name);
        }
        if (network.Items.Count > 0) section.Groups.Add(network);

        // Batterie (portables)
        if (BatteryCollector.WearPercent() is { } wear)
            section.Groups.Add(new InfoGroup("Batterie")
                .Add("Usure", $"{Math.Max(0, wear):0} %" + (BatteryCollector.Cycles() is { } cycles ? $" · {cycles} cycles" : "")));

        return section;
    }
}
