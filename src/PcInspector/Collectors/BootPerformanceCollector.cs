using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Win32;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Durée des démarrages et des arrêts, et ce qui les ralentit (comme BootPerformanceView).</summary>
public sealed class BootPerformanceCollector : ICollector
{
    private const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";
    private const int MaxBoots = 10;
    private const int MaxCulprits = 10;
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    public string Title => "Temps de démarrage";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        List<Event> events;
        try
        {
            events = ReadEvents();
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            section.Error = "Journal des performances illisible (lancer en administrateur) : " + ex.Message;
            return section;
        }

        var boots = events.Where(e => e.Id == 100).Take(MaxBoots).ToList();
        var summary = new InfoGroup("Résumé");
        if (boots.Count == 0)
        {
            summary.Add("Résultat", "Aucun démarrage complet enregistré");
        }
        else
        {
            var average = boots.Average(b => b.Ms("BootTime") ?? 0);
            summary.Add("Durée moyenne", $"{Seconds(average)} sur les {boots.Count} derniers démarrages")
                   .Add("Verdict", Verdict(average));
        }
        summary.Add("Démarrage rapide de Windows", FastStartup() switch
        {
            true => "Activé : seuls les redémarrages complets sont mesurés ici",
            false => "Désactivé",
            null => null,
        });
        section.Groups.Add(summary);

        if (boots.Count > 0)
        {
            var list = new InfoGroup("Derniers démarrages");
            foreach (var boot in boots)
            {
                var total = boot.Ms("BootTime");
                var main = boot.Ms("MainPathBootTime");
                var post = boot.Ms("BootPostBootTime");
                list.Add(boot.When, $"{Seconds(total)} au total · bureau affiché après {Seconds(main)} · utilisable {Seconds(post)} plus tard");
            }
            section.Groups.Add(list);
        }

        section.Groups.Add(Culprits("Ce qui ralentit le démarrage", events.Where(e => e.Id is >= 101 and <= 110)));

        var shutdowns = events.Where(e => e.Id == 200).Take(MaxBoots).ToList();
        var shutdown = new InfoGroup("Arrêts");
        if (shutdowns.Count == 0) shutdown.Add("Résultat", "Aucun arrêt enregistré");
        else shutdown.Add("Durée moyenne", $"{Seconds(shutdowns.Average(s => s.Ms("ShutdownTime") ?? 0))} sur les {shutdowns.Count} derniers arrêts");
        section.Groups.Add(shutdown);
        section.Groups.Add(Culprits("Ce qui ralentit l'arrêt", events.Where(e => e.Id is >= 201 and <= 210)));

        section.Groups.Add(Advice(boots.Count > 0 ? boots.Average(b => b.Ms("BootTime") ?? 0) : (double?)null,
            events.Any(e => e.Id == 101)));
        return section;
    }

    private static List<Event> ReadEvents()
    {
        var result = new List<Event>();
        var query = new EventLogQuery(LogName, PathType.LogName, "*[System[(EventID>=100 and EventID<=110) or (EventID>=200 and EventID<=210)]]")
        {
            ReverseDirection = true,
        };
        using var reader = new EventLogReader(query);
        var since = DateTime.Now.AddDays(-60);
        for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
        {
            using (record)
            {
                if (record.TimeCreated is { } time && time < since) break;
                result.Add(new Event(record.Id, record.TimeCreated, Fields(record.ToXml())));
            }
        }
        return result;
    }

    private static Dictionary<string, string> Fields(string xml)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in XDocument.Parse(xml).Descendants(Ns + "Data"))
        {
            var name = (string?)node.Attribute("Name");
            if (!string.IsNullOrEmpty(name)) data[name!] = node.Value;
        }
        return data;
    }

    /// <summary>Regroupe les applications, pilotes et services signalés lents par Windows, du plus pénalisant au moins pénalisant.</summary>
    private static InfoGroup Culprits(string title, IEnumerable<Event> events)
    {
        var group = new InfoGroup(title);
        var worst = events
            .Select(e => (Kind: Kind(e.Id), Name: e.Text("FriendlyName") ?? e.Text("Name") ?? e.Text("FileName") ?? "Inconnu",
                          Delay: e.Ms("DegradationTime") ?? e.Ms("TotalTime") ?? 0))
            .GroupBy(c => (c.Kind, c.Name))
            .Select(g => (g.Key.Kind, g.Key.Name, Count: g.Count(), Average: g.Average(c => c.Delay)))
            .OrderByDescending(c => c.Average * c.Count)
            .Take(MaxCulprits);

        foreach (var c in worst)
            group.Add($"{c.Kind} · {c.Name}", $"+{Seconds(c.Average)} en moyenne, signalé {c.Count} fois en 60 jours");
        if (group.Items.Count == 0) group.Add("Résultat", "Rien de signalé par Windows");
        return group;
    }

    private static string Kind(int id) => (id % 100) switch
    {
        1 => "Application",
        2 => "Pilote",
        3 => "Service",
        6 => "Tâche de fond",
        9 => "Périphérique",
        _ => "Autre",
    };

    private static bool? FastStartup()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
        return key?.GetValue("HiberbootEnabled") is int value ? value == 1 : null;
    }

    private static string Verdict(double ms) => (ms / 1000) switch
    {
        < 30 => "Rapide",
        < 60 => "Correct",
        < 120 => "Lent",
        _ => "Très lent",
    };

    private static InfoGroup Advice(double? averageMs, bool slowApps)
    {
        var group = new InfoGroup("Conseils");
        if (averageMs is > 60_000)
            group.Add("Disque", "Si le PC a encore un disque dur classique, passer sur un SSD divise souvent le temps de démarrage par 3 ou 4.");
        if (slowApps)
            group.Add("Applications", "Désactiver au démarrage les applications listées qui ne servent pas tous les jours : Gestionnaire des tâches > Applications de démarrage.");
        if (group.Items.Count == 0) group.Add("Résultat", "Rien à signaler");
        return group;
    }

    private static string Seconds(double? ms) => ms is null ? "?" : $"{ms.Value / 1000:0.#} s";

    private sealed class Event
    {
        private readonly Dictionary<string, string> _fields;

        public Event(int id, DateTime? time, Dictionary<string, string> fields)
        {
            Id = id;
            _fields = fields;
            When = time?.ToString("dd/MM/yyyy HH:mm") ?? "Date inconnue";
        }

        public int Id { get; }
        public string When { get; }

        public string? Text(string name) => _fields.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        public double? Ms(string name) => double.TryParse(Text(name), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
