using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PcInspector.Models;

namespace PcMaintenance.Maintenance;

/// <summary>Ce qui a été fait pendant la maintenance, pour la fiche client.</summary>
internal sealed class MaintenanceReport
{
    private readonly List<(string Step, string Result)> _steps = new();
    private readonly List<(string Title, List<string> Lines)> _details = new();
    private readonly List<(string Label, string? Before, string? After)> _compare = new();

    public DateTime Start { get; private set; }
    public DateTime? End { get; private set; }
    public ulong? FreeBefore { get; private set; }
    public ulong? FreeAfter { get; private set; }

    public bool IsEmpty => _steps.Count == 0;

    /// <summary>Repart de zéro ; la préparation revente donne l'heure de son vrai début, avant les redémarrages.</summary>
    public void Begin(DateTime? start = null)
    {
        _steps.Clear();
        _details.Clear();
        _compare.Clear();
        Start = start ?? DateTime.Now;
        End = null;
        FreeBefore = FreeSpace();
        FreeAfter = null;
    }

    public void Finish()
    {
        End = DateTime.Now;
        FreeAfter = FreeSpace();
    }

    /// <summary>Une ligne du tableau « Avant / Après » (avant = relevé au début, sinon à la fin).</summary>
    public void Compare(string label, string value, bool before)
    {
        var i = _compare.FindIndex(c => c.Label == label);
        (string Label, string? Before, string? After) row = i >= 0 ? _compare[i] : (label, null, null);
        row = before ? (label, value, row.After) : (label, row.Before, value);
        if (i >= 0) _compare[i] = row; else _compare.Add(row);
    }

    public void Step(string step, string result) => _steps.Add((step, result));

    public void Detail(string title, IEnumerable<string> lines)
    {
        var list = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (list.Count > 0) _details.Add((title, list));
    }

    public List<InfoSection> ToSections(string sectionTitle = "Maintenance du PC")
    {
        var section = new InfoSection(sectionTitle);
        var summary = new InfoGroup("Résumé")
            .Add("Ordinateur", Environment.MachineName)
            .Add("Début", Start.ToString("dd/MM/yyyy HH:mm"))
            .Add("Durée", End is { } end ? Duration(end - Start) : "En cours");
        if (FreeBefore is { } before && FreeAfter is { } after)
        {
            var gained = (long)after - (long)before;
            summary.Add("Espace libre sur " + SystemDrive, $"{Size(before)} avant, {Size(after)} après"
                + (gained > 50L * 1024 * 1024 ? $" (+{Size((ulong)gained)} récupérés)" : ""));
        }
        section.Groups.Add(summary);

        var compare = new InfoGroup("Avant / Après");
        if (FreeBefore is { } b && FreeAfter is { } a) compare.Add("Espace libre sur " + SystemDrive, $"{Size(b)}  →  {Size(a)}");
        foreach (var (label, was, now) in _compare)
            compare.Add(label, $"{was ?? "?"}  →  {now ?? "?"}");
        if (compare.Items.Count > 0) section.Groups.Add(compare);

        var steps = new InfoGroup("Étapes réalisées");
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

    private static string SystemDrive => (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');

    private static ulong? FreeSpace()
    {
        try { return (ulong)new DriveInfo(SystemDrive).AvailableFreeSpace; }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return null; }
    }

    private static string Size(ulong bytes) => bytes >= 1024UL * 1024 * 1024
        ? $"{bytes / (1024.0 * 1024 * 1024):0.#} Go"
        : $"{bytes / (1024.0 * 1024):0} Mo";

    private static string Duration(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours} h {span.Minutes:00}"
        : $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} min";
}
