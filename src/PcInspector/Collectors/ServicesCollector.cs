using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Services ajoutés par des logiciels tiers (ceux de Windows sont masqués) et services en échec.</summary>
public sealed class ServicesCollector : ICollector
{
    public string Title => "Services";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        var services = Wmi.Query("SELECT Name, DisplayName, State, StartMode, PathName, ExitCode FROM Win32_Service");

        var thirdParty = services
            .Select(s => (Service: s, Company: Company(s.Str("PathName"))))
            .Where(x => !x.Company.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Service.Str("DisplayName"))
            .ToList();

        var failed = services
            .Where(s => s.Str("StartMode") == "Auto" && s.Str("State") == "Stopped" && s.ULong("ExitCode") is { } code && code != 0 && code != 1077)
            .ToList();

        section.Groups.Add(new InfoGroup("Vue d'ensemble")
            .Add("Services", services.Count.ToString())
            .Add("En cours d'exécution", services.Count(s => s.Str("State") == "Running").ToString())
            .Add("Services tiers", thirdParty.Count.ToString())
            .Add("Automatiques en échec", failed.Count == 0 ? "Aucun" : failed.Count.ToString()));

        if (failed.Count > 0)
        {
            var group = new InfoGroup("Services automatiques arrêtés en erreur");
            foreach (var s in failed)
                group.Add(s.Str("DisplayName") ?? s.Str("Name") ?? "?", $"code de sortie {s.ULong("ExitCode")}");
            section.Groups.Add(group);
        }

        var third = new InfoGroup("Services tiers");
        foreach (var (s, company) in thirdParty)
        {
            var details = new[] { State(s.Str("State")), StartMode(s.Str("StartMode")), company.Length > 0 ? company : null }
                .Where(d => d is not null);
            third.Add(s.Str("DisplayName") ?? s.Str("Name") ?? "?", string.Join(" · ", details));
        }
        if (third.Items.Count > 0) section.Groups.Add(third);

        return section;
    }

    /// <summary>Éditeur du programme du service, lu dans les propriétés du fichier .exe.</summary>
    private static string Company(string? pathName)
    {
        var path = ExecutablePath(pathName);
        if (path is null) return "";
        try
        {
            return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim() ?? "" : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "";
        }
    }

    private static string? ExecutablePath(string? pathName)
    {
        if (string.IsNullOrWhiteSpace(pathName)) return null;
        var p = pathName!.Trim();
        if (p.StartsWith("\""))
        {
            var end = p.IndexOf('"', 1);
            return end > 1 ? p.Substring(1, end - 1) : null;
        }
        // Chemin sans guillemets : on coupe après « .exe » (les arguments suivent).
        var exe = p.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        var path = exe > 0 ? p.Substring(0, exe + 4) : p.Split(' ')[0];
        return Environment.ExpandEnvironmentVariables(path.Replace(@"\SystemRoot\", @"%SystemRoot%\"));
    }

    private static string? State(string? state) => state switch
    {
        "Running" => "en cours",
        "Stopped" => "arrêté",
        "Paused" => "en pause",
        _ => state,
    };

    private static string? StartMode(string? mode) => mode switch
    {
        "Auto" => "automatique",
        "Manual" => "manuel",
        "Disabled" => "désactivé",
        "Boot" or "System" => "démarrage système",
        _ => mode,
    };
}
