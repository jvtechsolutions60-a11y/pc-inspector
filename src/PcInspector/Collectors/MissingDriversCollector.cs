using System;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>
/// Périphériques sans pilote (ou avec un pilote en erreur), avec leur identifiant matériel et un lien de recherche
/// du bon pilote. Lecture seule : rien n'est téléchargé ni installé.
/// </summary>
public sealed class MissingDriversCollector : ICollector
{
    // 22 = désactivé volontairement, 45 = débranché : pas un problème de pilote.
    private static readonly ulong[] NotDriverProblems = { 22, 45 };

    public string Title => "Pilotes manquants";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        var devices = Wmi.Query("SELECT Name, PNPClass, DeviceID, HardwareID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0")
            .Where(d => !NotDriverProblems.Contains(d.ULong("ConfigManagerErrorCode") ?? 0))
            .ToList();

        if (devices.Count == 0)
        {
            section.Groups.Add(new InfoGroup("Résultat").Add("Pilotes", "✔ Tous les périphériques ont un pilote qui fonctionne"));
            return section;
        }

        section.Groups.Add(new InfoGroup("Vue d'ensemble")
            .Add("Périphériques concernés", $"! {devices.Count}")
            .Add("Conseil", "Chercher d'abord sur le site du fabricant du PC (page support avec le numéro de série), "
                + "sinon avec l'identifiant matériel ci-dessous. Windows Update (Paramètres > Mises à jour facultatives) propose aussi souvent le pilote."));

        foreach (var device in devices)
        {
            var name = device.Str("Name");
            var id = BestId(device);
            var group = new InfoGroup(string.IsNullOrWhiteSpace(name) ? "Périphérique inconnu" : name!)
                .Add("Problème", "! " + DiagnosticCollector.DeviceError(device.ULong("ConfigManagerErrorCode")))
                .Add("Type", device.Str("PNPClass") ?? "Inconnu")
                .Add("Identifiant matériel", id ?? device.Str("DeviceID"));
            if (Vendor(id) is { } vendor) group.Add("Fabricant de la puce", vendor);
            if (id is not null)
                group.AddLink("Rechercher le pilote", "Recherche avec l'identifiant", "https://www.google.com/search?q=" + Uri.EscapeDataString(id + " driver"));
            section.Groups.Add(group);
        }
        return section;
    }

    /// <summary>L'identifiant le plus utile à chercher : PCI\VEN_8086&amp;DEV_A0E8 ou USB\VID_0BDA&amp;PID_8153, sans le reste.</summary>
    private static string? BestId(ManagementBaseObject device)
    {
        var ids = (device["HardwareID"] as string[]) ?? Array.Empty<string>();
        foreach (var raw in ids.Concat(new[] { device.Str("DeviceID") ?? "" }))
        {
            var match = Regex.Match(raw, @"^(PCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}|USB\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4}|HDAUDIO\\FUNC_\d+&VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}|ACPI\\[A-Z0-9]{4,8})", RegexOptions.IgnoreCase);
            if (match.Success) return match.Value.ToUpperInvariant();
        }
        return ids.FirstOrDefault();
    }

    /// <summary>Les fabricants de puces les plus courants, d'après le code VEN ou VID.</summary>
    private static string? Vendor(string? id)
    {
        var code = id is null ? null : Regex.Match(id, @"(?:VEN|VID)_([0-9A-F]{4})", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant();
        return code switch
        {
            "8086" => "Intel",
            "1022" => "AMD",
            "1002" => "AMD (Radeon)",
            "10DE" => "NVIDIA",
            "10EC" or "0BDA" => "Realtek",
            "14E4" or "0A5C" => "Broadcom",
            "168C" => "Qualcomm Atheros",
            "17CB" => "Qualcomm",
            "1969" => "Qualcomm Atheros (Ethernet)",
            "14C3" => "MediaTek",
            "1B21" => "ASMedia",
            "1217" => "O2 Micro",
            "8087" => "Intel (Bluetooth)",
            "1814" => "Ralink / MediaTek",
            "04F2" => "Chicony (webcam)",
            "13D3" => "IMC Networks / AzureWave",
            "06CB" => "Synaptics",
            "04F3" => "Elan",
            "138A" => "Validity / Synaptics (lecteur d'empreinte)",
            "27C6" => "Goodix (lecteur d'empreinte)",
            _ => null,
        };
    }
}
