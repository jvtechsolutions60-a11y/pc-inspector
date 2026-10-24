using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PcInspector;

namespace PcMaintenance.Preparation;

/// <summary>Les étapes de la préparation, dans l'ordre.</summary>
internal enum PreparationStep
{
    Start = 0,
    Debloat = 1,
    Manufacturer = 2,
    WindowsUpdate = 3,
    Finish = 4,
    Done = 5,
}

/// <summary>
/// Où en est la préparation, gardé dans etat.ini à côté du .exe : elle reprend là où elle s'était arrêtée
/// après chaque redémarrage. Les résultats déjà obtenus y sont aussi notés, pour le rapport final.
/// </summary>
internal sealed class PreparationState
{
    public PreparationStep Step { get; set; } = PreparationStep.Start;
    /// <summary>Nombre de passages de Windows Update déjà faits.</summary>
    public int Round { get; set; }
    public DateTime Started { get; set; } = DateTime.Now;
    public List<(string Step, string Result)> Results { get; } = new();
    public List<(string Title, string Line)> Details { get; } = new();

    public static string FilePath => Path.Combine(AppPaths.ExeFolder, "etat.ini");
    public static string LogPath => Path.Combine(AppPaths.ExeFolder, "preparation.log");

    public static PreparationState Load()
    {
        var state = new PreparationState();
        if (!File.Exists(FilePath)) return state;
        foreach (var line in File.ReadAllLines(FilePath))
        {
            var parts = line.Split(new[] { '=' }, 2);
            if (parts.Length != 2) continue;
            var value = parts[1];
            switch (parts[0])
            {
                case "Step" when int.TryParse(value, out var step): state.Step = (PreparationStep)step; break;
                case "Round" when int.TryParse(value, out var round): state.Round = round; break;
                case "Started" when DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started): state.Started = started; break;
                case "Result" when value.Split('|') is { Length: 2 } r: state.Results.Add((r[0], r[1])); break;
                case "Detail" when value.Split('|') is { Length: 2 } d: state.Details.Add((d[0], d[1])); break;
            }
        }
        return state;
    }

    public void Save()
    {
        var lines = new List<string>
        {
            "Step=" + (int)Step,
            "Round=" + Round,
            "Started=" + Started.ToString("o", CultureInfo.InvariantCulture),
        };
        lines.AddRange(Results.Select(r => $"Result={Clean(r.Step)}|{Clean(r.Result)}"));
        lines.AddRange(Details.Select(d => $"Detail={Clean(d.Title)}|{Clean(d.Line)}"));
        File.WriteAllLines(FilePath, lines);
    }

    public void Result(string step, string result)
    {
        Results.RemoveAll(r => r.Step == step);
        Results.Add((step, result));
    }

    public void Detail(string title, IEnumerable<string> lines)
    {
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l))) Details.Add((title, line));
    }

    private static string Clean(string text) => text.Replace('|', '/').Replace("\r", " ").Replace("\n", " ").Trim();
}
