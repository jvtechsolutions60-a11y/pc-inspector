using System;
using System.Collections.Generic;
using System.Linq;
using PcInspector.Models;

namespace PcMigration.Migration;

/// <summary>Ce qui a été transféré, pour la fiche client « ce qui a été transféré ».</summary>
internal sealed class MigrationReport
{
    private readonly List<(string Step, string Result)> _steps = new();
    private readonly List<(string Title, List<string> Lines)> _details = new();
    private readonly List<(string Label, string Value)> _summary = new();

    public string Title { get; private set; } = "Migration";
    public DateTime Start { get; private set; }
    public DateTime? End { get; private set; }
    public bool IsEmpty => _steps.Count == 0;

    public void Begin(string title, IEnumerable<(string Label, string Value)> summary)
    {
        lock (_steps)
        {
            _steps.Clear();
            _details.Clear();
            _summary.Clear();
            _summary.AddRange(summary);
            Title = title;
            Start = DateTime.Now;
            End = null;
        }
    }

    public void Finish() => End = DateTime.Now;

    public void Step(string step, string result) { lock (_steps) _steps.Add((step, result)); }

    public void Detail(string title, IEnumerable<string> lines)
    {
        var list = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (list.Count > 0) lock (_steps) _details.Add((title, list));
    }

    public List<InfoSection> ToSections()
    {
        lock (_steps)
        {
            var section = new InfoSection(Title);
            var summary = new InfoGroup("Résumé");
            foreach (var (label, value) in _summary) summary.Add(label, value);
            summary.Add("Début", Start.ToString("dd/MM/yyyy HH:mm"));
            summary.Add("Durée", End is { } end ? Duration(end - Start) : "En cours");
            section.Groups.Add(summary);

            var steps = new InfoGroup("Ce qui a été transféré");
            foreach (var (step, result) in _steps) steps.Add(step, result);
            if (_steps.Count == 0) steps.Add("Étapes", "Aucune étape lancée");
            section.Groups.Add(steps);

            foreach (var (title, lines) in _details)
            {
                var group = new InfoGroup(title);
                for (var i = 0; i < lines.Count; i++) group.Add((i + 1).ToString(), lines[i]);
                section.Groups.Add(group);
            }
            return new List<InfoSection> { section };
        }
    }

    private static string Duration(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours} h {span.Minutes:00}"
        : $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} min";
}
