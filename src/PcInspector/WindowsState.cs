using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace PcInspector;

/// <summary>Version de Windows lue dans le registre (instantané, sans WMI).</summary>
public sealed record WindowsVersionInfo(string Name, string? DisplayVersion, int Build)
{
    public bool IsWindows11 => Build >= 22000;
}

/// <summary>
/// État de Windows lu de la même façon par PC Inspector, PC Sécurité et PC Maintenance : version, activation,
/// redémarrage en attente, pare-feu. Lecture seule.
/// </summary>
public static class WindowsState
{
    // Identifiant de Windows dans SoftwareLicensingProduct (les autres entrées sont Office, etc.).
    public const string WindowsAppId = "55c92734-d682-4d71-983e-d6ec3f16059f";

    public static WindowsVersionInfo Version()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var name = key?.GetValue("ProductName") as string ?? "Windows";
        int.TryParse(key?.GetValue("CurrentBuild") as string, out var build);
        // Windows 11 garde « Windows 10 » dans ProductName : seul le numéro de build fait foi.
        if (build >= 22000) name = name.Replace("Windows 10", "Windows 11");
        return new WindowsVersionInfo(name, key?.GetValue("DisplayVersion") as string, build);
    }

    /// <summary>LicenseStatus de Windows (1 = activé), null si l'état n'a pas pu être lu.</summary>
    public static ulong? LicenseStatus()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID = '{WindowsAppId}' AND PartialProductKey IS NOT NULL");
            var product = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
            return product?["LicenseStatus"] is { } status ? Convert.ToUInt64(status) : null;
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Vrai si Windows est activé, null si l'état n'a pas pu être lu.</summary>
    public static bool? IsActivated() => LicenseStatus() is { } status ? status == 1 : null;

    /// <summary>Windows note dans ces clés qu'un redémarrage est nécessaire pour finir une installation.</summary>
    public static bool IsRebootPending()
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

    /// <summary>Profils du pare-feu Windows désactivés (domaine, privé, public).</summary>
    public static List<string> DisabledFirewallProfiles()
    {
        var off = new List<string>();
        foreach (var (key, name) in new[] { ("DomainProfile", "domaine"), ("StandardProfile", "privé"), ("PublicProfile", "public") })
        {
            using var reg = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{key}");
            if (reg?.GetValue("EnableFirewall") is int enabled && enabled == 0) off.Add(name);
        }
        return off;
    }
}
