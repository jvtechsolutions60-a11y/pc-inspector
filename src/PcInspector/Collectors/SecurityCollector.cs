using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Win32;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Licence Windows, antivirus, pare-feu et mises à jour en attente.</summary>
public sealed class SecurityCollector : ICollector
{
    // Identifiant de Windows dans SoftwareLicensingProduct (les autres entrées sont Office, etc.).
    private const string WindowsAppId = "55c92734-d682-4d71-983e-d6ec3f16059f";
    private static readonly TimeSpan UpdateSearchTimeout = TimeSpan.FromSeconds(90);

    public string Title => "Sécurité";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        section.Groups.Add(License());
        section.Groups.Add(Protection());
        section.Groups.Add(Updates());
        return section;
    }

    private static InfoGroup License()
    {
        var group = new InfoGroup("Licence Windows");
        var os = Wmi.Query("SELECT Caption FROM Win32_OperatingSystem").FirstOrDefault();
        group.Add("Édition", os?.Str("Caption"));

        var product = WindowsProduct("Description, LicenseStatus, PartialProductKey");
        group.Add("Activation", LicenseStatus(product?.ULong("LicenseStatus")))
             .Add("Canal", Channel(product?.Str("Description")))
             .Add("Fin de clé installée", product?.Str("PartialProductKey") is { } partial ? "XXXXX-XXXXX-XXXXX-XXXXX-" + partial : null);

        var oemKey = Wmi.QueryOptional("SELECT OA3xOriginalProductKey FROM SoftwareLicensingService").FirstOrDefault()?.Str("OA3xOriginalProductKey");
        group.Add("Clé OEM du BIOS", string.IsNullOrWhiteSpace(oemKey) ? "Aucune" : oemKey);
        return group;
    }

    private static System.Management.ManagementObject? WindowsProduct(string properties) =>
        Wmi.QueryOptional(
                $"SELECT {properties} FROM SoftwareLicensingProduct " +
                $"WHERE ApplicationID = '{WindowsAppId}' AND PartialProductKey IS NOT NULL")
            .FirstOrDefault();

    /// <summary>Vrai si Windows est activé, null si l'état n'a pas pu être lu.</summary>
    internal static bool? IsWindowsActivated() =>
        WindowsProduct("LicenseStatus")?.ULong("LicenseStatus") is { } status ? status == 1 : null;

    private static InfoGroup Protection()
    {
        var group = new InfoGroup("Protection");

        var antivirus = Wmi.QueryOptional("SELECT displayName, productState FROM AntiVirusProduct", @"root\SecurityCenter2");
        if (antivirus.Count == 0) group.Add("Antivirus", "Aucun détecté");
        foreach (var av in antivirus)
        {
            // productState : octet du milieu = activé (0x10), octet bas = définitions à jour (0x00).
            var state = av.ULong("productState") ?? 0;
            var enabled = ((state >> 8) & 0xFF) is 0x10 or 0x11;
            var upToDate = (state & 0xFF) == 0x00;
            group.Add("Antivirus", $"{av.Str("displayName")} · {(enabled ? "activé" : "désactivé")} · {(upToDate ? "à jour" : "définitions périmées")}");
        }

        var firewall = Wmi.QueryOptional("SELECT displayName FROM FirewallProduct", @"root\SecurityCenter2");
        foreach (var fw in firewall)
            group.Add("Pare-feu tiers", fw.Str("displayName"));

        group.Add("Pare-feu Windows", WindowsFirewall());
        group.Add("BitLocker (disque système)", BitLocker());
        return group;
    }

    private static string WindowsFirewall()
    {
        var off = DisabledFirewallProfiles();
        return off.Count == 0 ? "Activé sur tous les profils" : "Désactivé pour : " + string.Join(", ", off);
    }

    /// <summary>Profils du pare-feu Windows désactivés (domaine, privé, public).</summary>
    internal static List<string> DisabledFirewallProfiles()
    {
        var profiles = new (string Key, string Name)[] { ("DomainProfile", "domaine"), ("StandardProfile", "privé"), ("PublicProfile", "public") };
        var off = new List<string>();
        foreach (var (key, name) in profiles)
        {
            using var reg = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{key}");
            if (reg?.GetValue("EnableFirewall") is int enabled && enabled == 0) off.Add(name);
        }
        return off;
    }

    private static string? BitLocker()
    {
        var drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var volume = Wmi.QueryOptional($"SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = '{drive}'",
            @"root\CIMV2\Security\MicrosoftVolumeEncryption").FirstOrDefault();
        return volume?.ULong("ProtectionStatus") switch
        {
            1 => "Activé (penser à la clé de récupération avant toute intervention)",
            0 => "Désactivé",
            _ => null,
        };
    }

    private static InfoGroup Updates()
    {
        var group = new InfoGroup("Mises à jour Windows");
        group.Add("Redémarrage en attente", Wmi.YesNo(IsRebootPending()));

        var search = Task.Run(PendingUpdates);
        if (!search.Wait(UpdateSearchTimeout))
        {
            group.Add("En attente", "Recherche trop longue, réessayer plus tard");
        }
        else if (search.Result is null)
        {
            group.Add("En attente", "Service Windows Update indisponible");
        }
        else
        {
            var titles = search.Result;
            group.Add("En attente", titles.Count == 0 ? "Aucune, le PC est à jour" : $"{titles.Count} mise(s) à jour");
            foreach (var title in titles) group.Add("·", title);
        }
        return group;
    }

    /// <summary>Interroge l'agent Windows Update par COM (sans référence au type, par réflexion).</summary>
    private static List<string>? PendingUpdates()
    {
        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType is null) return null;
            var session = Activator.CreateInstance(sessionType);
            var searcher = Call(session, "CreateUpdateSearcher");
            var result = Call(searcher, "Search", "IsInstalled=0 and IsHidden=0 and Type='Software'");
            var updates = Get(result, "Updates");
            var count = Convert.ToInt32(Get(updates, "Count"));
            var titles = new List<string>();
            for (int i = 0; i < count; i++)
                titles.Add(Get(Get(updates, "Item", i), "Title")?.ToString() ?? "?");
            return titles;
        }
        catch (Exception ex) when (ex is TargetInvocationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static object? Call(object? target, string method, params object[] args) =>
        target?.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);

    private static object? Get(object? target, string property, params object[] args) =>
        target?.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, args);

    /// <summary>Windows note dans ces clés qu'un redémarrage est nécessaire pour finir une installation.</summary>
    internal static bool IsRebootPending()
    {
        string[] keys =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
        };
        foreach (var path in keys)
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key is not null) return true;
        }
        return false;
    }

    private static string? LicenseStatus(ulong? status) => status switch
    {
        1 => "Activé",
        0 => "Non activé",
        2 or 3 => "Période de grâce",
        4 => "Non authentique",
        5 => "Notification (non activé)",
        6 => "Grâce prolongée",
        _ => null,
    };

    private static string? Channel(string? description)
    {
        if (description is null) return null;
        if (description.IndexOf("OEM", StringComparison.OrdinalIgnoreCase) >= 0) return "OEM (liée à la carte mère)";
        if (description.IndexOf("RETAIL", StringComparison.OrdinalIgnoreCase) >= 0) return "Retail (transférable)";
        if (description.IndexOf("VOLUME", StringComparison.OrdinalIgnoreCase) >= 0) return "Volume (entreprise)";
        return description;
    }
}
