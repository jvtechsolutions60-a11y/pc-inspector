using System;
using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Pilotes des fabricants (hors pilotes génériques Microsoft), classés par type de périphérique.</summary>
public sealed class DriversCollector : ICollector
{
    private const int OldDriverYears = 5;

    public string Title => "Pilotes";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        var drivers = Wmi.Query("SELECT DeviceName, DeviceClass, DriverVersion, DriverDate, DriverProviderName, IsSigned FROM Win32_PnPSignedDriver")
            .Where(d => !string.IsNullOrWhiteSpace(d.Str("DeviceName")))
            .ToList();

        var thirdParty = drivers
            .Where(d => !(d.Str("DriverProviderName") ?? "").StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var unsigned = drivers.Count(d => d.Bool("IsSigned") == false);
        var old = thirdParty.Count(d => IsOld(d.Str("DriverDate")));
        section.Groups.Add(new InfoGroup("Vue d'ensemble")
            .Add("Pilotes installés", drivers.Count.ToString())
            .Add("Pilotes de fabricants", thirdParty.Count.ToString())
            .Add("Pilotes de plus de 5 ans", old == 0 ? "Aucun" : $"{old} (voir « ancien » ci-dessous)")
            .Add("Pilotes non signés", unsigned == 0 ? "Aucun" : unsigned.ToString()));

        foreach (var byClass in thirdParty.GroupBy(d => ClassName(d.Str("DeviceClass"))).OrderBy(g => g.Key))
        {
            var group = new InfoGroup(byClass.Key);
            foreach (var d in byClass.OrderBy(d => d.Str("DeviceName")))
            {
                var date = d.Date("DriverDate");
                var flags = (IsOld(d.Str("DriverDate")) ? " · ancien" : "") + (d.Bool("IsSigned") == false ? " · non signé" : "");
                group.Add(d.Str("DeviceName")!, $"{d.Str("DriverVersion")} · {date} · {d.Str("DriverProviderName")}{flags}");
            }
            section.Groups.Add(group);
        }

        return section;
    }

    private static bool IsOld(string? wmiDate)
    {
        if (string.IsNullOrEmpty(wmiDate)) return false;
        try { return System.Management.ManagementDateTimeConverter.ToDateTime(wmiDate) < DateTime.Now.AddYears(-OldDriverYears); }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static string ClassName(string? deviceClass) => deviceClass?.ToUpperInvariant() switch
    {
        "DISPLAY" => "Affichage",
        "NET" => "Réseau",
        "MEDIA" => "Son et vidéo",
        "SYSTEM" => "Système",
        "HDC" or "SCSIADAPTER" => "Contrôleurs de stockage",
        "USB" => "USB",
        "BLUETOOTH" => "Bluetooth",
        "HIDCLASS" or "KEYBOARD" or "MOUSE" => "Clavier, souris et HID",
        "PRINTER" or "PRINTQUEUE" => "Imprimantes",
        "CAMERA" or "IMAGE" => "Caméras",
        "BIOMETRIC" => "Biométrie",
        "SOFTWARECOMPONENT" or "SOFTWAREDEVICE" => "Composants logiciels",
        "FIRMWARE" => "Firmware",
        "MONITOR" => "Écrans",
        null or "" => "Autres",
        _ => deviceClass!,
    };
}
