using System;
using System.Linq;
using Microsoft.Win32;
using PcInspector.Models;

namespace PcInspector.Collectors;

public sealed class GpuCollector : ICollector
{
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public string Title => "Carte graphique";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);

        foreach (var gpu in Wmi.Query("SELECT * FROM Win32_VideoController"))
        {
            var name = gpu.Str("Name") ?? "Carte graphique";
            var width = gpu.ULong("CurrentHorizontalResolution");
            var height = gpu.ULong("CurrentVerticalResolution");
            var refresh = gpu.ULong("CurrentRefreshRate");

            section.Groups.Add(new InfoGroup(name)
                .Add("Fabricant", gpu.Str("AdapterCompatibility"))
                .Add("Mémoire vidéo", Wmi.FormatBytes(VideoMemory(name) ?? gpu.ULong("AdapterRAM")))
                .Add("Processeur vidéo", gpu.Str("VideoProcessor"))
                .Add("Version du pilote", gpu.Str("DriverVersion"))
                .Add("Date du pilote", gpu.Date("DriverDate"))
                .Add("Résolution", width is > 0 && height is > 0 ? $"{width} × {height}" + (refresh is > 0 ? $" à {refresh} Hz" : "") : null)
                .Add("Statut", gpu.Str("Status")));
        }

        var monitors = Wmi.QueryOptional("SELECT * FROM WmiMonitorID", Wmi.WmiScope);
        foreach (var monitor in monitors)
        {
            var week = monitor.ULong("WeekOfManufacture");
            var year = monitor.ULong("YearOfManufacture");
            section.Groups.Add(new InfoGroup("Écran : " + (monitor.CharArray("UserFriendlyName") is { Length: > 0 } n ? n : "sans nom"))
                .Add("Fabricant", monitor.CharArray("ManufacturerName"))
                .Add("Modèle", monitor.CharArray("ProductCodeID"))
                .Add("Numéro de série", monitor.CharArray("SerialNumberID"))
                .Add("Fabriqué", year is > 0 ? (week is > 0 ? $"semaine {week} de {year}" : year.ToString()) : null));
        }

        return section;
    }

    /// <summary>
    /// Win32_VideoController plafonne la mémoire à 4 Go : on lit la vraie valeur écrite par le pilote dans le registre.
    /// </summary>
    internal static ulong? VideoMemory(string gpuName)
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (classKey is null) return null;

            foreach (var subKeyName in classKey.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
            {
                using var adapter = classKey.OpenSubKey(subKeyName);
                if (adapter?.GetValue("DriverDesc") as string != gpuName) continue;

                switch (adapter.GetValue("HardwareInformation.qwMemorySize"))
                {
                    case long qw when qw > 0: return (ulong)qw;
                }
                switch (adapter.GetValue("HardwareInformation.MemorySize"))
                {
                    case int dw when dw != 0: return unchecked((uint)dw);
                    case byte[] bytes when bytes.Length >= 4: return BitConverter.ToUInt32(bytes, 0);
                }
            }
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }
}
