using System;
using System.Linq;
using Microsoft.Win32;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Programmes lancés à l'ouverture de session (clés Run et dossiers Démarrage).</summary>
public sealed class StartupCollector : ICollector
{
    private const string ApprovedPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public string Title => "Démarrage";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        var entries = Wmi.Query("SELECT Name, Command, Location, User FROM Win32_StartupCommand")
            .OrderBy(e => e.Str("Name"))
            .ToList();

        var enabled = new InfoGroup("Activés");
        var disabled = new InfoGroup("Désactivés (dans le Gestionnaire des tâches)");
        foreach (var entry in entries)
        {
            var name = entry.Str("Name") ?? "?";
            var location = entry.Str("Location") ?? "";
            var value = $"{entry.Str("Command")} · {Where(location)}";
            (IsDisabled(name, location) ? disabled : enabled).Add(name, value);
        }

        section.Groups.Add(new InfoGroup("Vue d'ensemble")
            .Add("Programmes au démarrage", enabled.Items.Count.ToString())
            .Add("Désactivés", disabled.Items.Count.ToString()));
        if (enabled.Items.Count > 0) section.Groups.Add(enabled);
        if (disabled.Items.Count > 0) section.Groups.Add(disabled);
        return section;
    }

    private static string Where(string location)
    {
        if (location.StartsWith("HKU", StringComparison.OrdinalIgnoreCase) || location.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase))
            return "utilisateur (registre)";
        if (location.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase))
            return "tous les utilisateurs (registre)";
        if (location.IndexOf("Common", StringComparison.OrdinalIgnoreCase) >= 0)
            return "dossier Démarrage commun";
        if (location.IndexOf("Startup", StringComparison.OrdinalIgnoreCase) >= 0)
            return "dossier Démarrage";
        return location;
    }

    /// <summary>
    /// Le Gestionnaire des tâches note les éléments désactivés dans StartupApproved :
    /// premier octet pair = activé, impair = désactivé.
    /// </summary>
    private static bool IsDisabled(string name, string location)
    {
        var isFolder = location.IndexOf("Startup", StringComparison.OrdinalIgnoreCase) >= 0 && !location.StartsWith("HK", StringComparison.OrdinalIgnoreCase);
        var subKeys = isFolder ? new[] { "StartupFolder" } : new[] { "Run", "Run32" };
        var hives = location.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) || location.IndexOf("Common", StringComparison.OrdinalIgnoreCase) >= 0
            ? new[] { Registry.LocalMachine }
            : new[] { Registry.CurrentUser };

        // Pour les dossiers Démarrage, la valeur porte le nom du raccourci (avec .lnk).
        var candidates = isFolder ? new[] { name, name + ".lnk" } : new[] { name };

        foreach (var hive in hives)
        foreach (var sub in subKeys)
        {
            using var key = hive.OpenSubKey($@"{ApprovedPath}\{sub}");
            if (key is null) continue;
            foreach (var candidate in candidates)
            {
                if (key.GetValue(candidate) is byte[] data && data.Length > 0)
                    return data[0] % 2 == 1;
            }
        }
        return false;
    }
}
