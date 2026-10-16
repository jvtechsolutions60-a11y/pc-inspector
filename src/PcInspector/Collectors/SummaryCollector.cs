using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Onglet "Résumé" : une ligne par composant principal, comme la page d'accueil de Speccy.</summary>
public sealed class SummaryCollector : ICollector
{
    public string Title => "Résumé";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        var group = new InfoGroup("Vue d'ensemble");
        section.Groups.Add(group);

        var os = Wmi.Query("SELECT Caption, Version, OSArchitecture FROM Win32_OperatingSystem").FirstOrDefault();
        group.Add("Système", os is null ? null : $"{os.Str("Caption")} {os.Str("OSArchitecture")} ({os.Str("Version")})");

        foreach (var cpu in Wmi.Query("SELECT Name FROM Win32_Processor"))
            group.Add("Processeur", cpu.Str("Name"));

        var cs = Wmi.Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault();
        group.Add("Mémoire", Wmi.FormatBytes(cs?.ULong("TotalPhysicalMemory")));

        var board = Wmi.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        group.Add("Carte mère", board is null ? null : $"{board.Str("Manufacturer")} {board.Str("Product")}");

        foreach (var gpu in Wmi.Query("SELECT Name FROM Win32_VideoController"))
            group.Add("Carte graphique", gpu.Str("Name"));

        foreach (var disk in Wmi.Query("SELECT Model, Size FROM Win32_DiskDrive"))
            group.Add("Stockage", $"{disk.Str("Model")} ({Wmi.FormatBytes(disk.ULong("Size"))})");

        return section;
    }
}
