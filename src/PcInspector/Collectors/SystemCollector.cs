using System;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Version de Windows, installation, compte et réglages système utiles en intervention.</summary>
public sealed class SystemCollector : ICollector
{
    public string Title => "Système";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);

        var os = Wmi.Query("SELECT * FROM Win32_OperatingSystem").FirstOrDefault();
        using var nt = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var displayVersion = nt?.GetValue("DisplayVersion") as string ?? nt?.GetValue("ReleaseId") as string;
        var ubr = nt?.GetValue("UBR");

        section.Groups.Add(new InfoGroup("Windows")
            .Add("Édition", os?.Str("Caption"))
            .Add("Version", displayVersion)
            .Add("Build", os?.Str("BuildNumber") + (ubr is null ? "" : "." + ubr))
            .Add("Architecture", os?.Str("OSArchitecture"))
            .Add("Installé le", os?.Date("InstallDate"))
            .Add("Propriétaire enregistré", os?.Str("RegisteredUser"))
            .Add("Langue", CultureInfo.InstalledUICulture.DisplayName)
            .Add("Fuseau horaire", TimeZoneInfo.Local.DisplayName)
            .Add(".NET Framework", DotNetFramework()));

        var cs = Wmi.Query("SELECT Name, UserName, Domain, PartOfDomain FROM Win32_ComputerSystem").FirstOrDefault();
        section.Groups.Add(new InfoGroup("Ordinateur et compte")
            .Add("Nom du PC", cs?.Str("Name"))
            .Add("Utilisateur connecté", cs?.Str("UserName") ?? Environment.UserName)
            .Add(cs?.Bool("PartOfDomain") == true ? "Domaine" : "Groupe de travail", cs?.Str("Domain"))
            .Add("Comptes locaux", LocalAccounts()));

        var plan = Wmi.QueryOptional("SELECT ElementName FROM Win32_PowerPlan WHERE IsActive = TRUE", @"root\CIMV2\power").FirstOrDefault();
        section.Groups.Add(new InfoGroup("Réglages")
            .Add("Mode d'alimentation", plan?.Str("ElementName"))
            .Add("Démarrage rapide", FastStartup())
            .Add("Contrôle de compte (UAC)", Uac())
            .Add("Fichier d'échange", PageFile()));

        return section;
    }

    private static string? DotNetFramework()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
        return key?.GetValue("Release") is int release ? release switch
        {
            >= 533320 => "4.8.1",
            >= 528040 => "4.8",
            >= 461808 => "4.7.2",
            >= 461308 => "4.7.1",
            >= 460798 => "4.7",
            _ => "4.6 ou antérieur",
        } : null;
    }

    private static string? LocalAccounts()
    {
        var accounts = Wmi.QueryOptional("SELECT Name, Disabled FROM Win32_UserAccount WHERE LocalAccount = TRUE")
            .Where(a => a.Bool("Disabled") != true)
            .Select(a => a.Str("Name"))
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
        return accounts.Count == 0 ? null : string.Join(", ", accounts);
    }

    private static string FastStartup()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
        return key?.GetValue("HiberbootEnabled") is int value && value == 1
            ? "Activé (le PC ne redémarre pas vraiment à l'arrêt)"
            : "Désactivé";
    }

    private static string Uac()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
        return key?.GetValue("EnableLUA") is int value && value == 0 ? "Désactivé" : "Activé";
    }

    private static string? PageFile()
    {
        var files = Wmi.QueryOptional("SELECT Name, AllocatedBaseSize FROM Win32_PageFileUsage");
        if (files.Count == 0) return "Aucun";
        return string.Join(", ", files.Select(f => $"{f.Str("Name")} ({Wmi.FormatBytes(f.ULong("AllocatedBaseSize") * 1024 * 1024)})"));
    }
}
