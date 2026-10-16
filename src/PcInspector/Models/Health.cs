using System;
using System.Collections.Generic;
using System.Linq;

namespace PcInspector.Models;

public enum Severity { Critical, Warning, Ok }

/// <summary>Un point du bilan : ce qui a été vérifié, le résultat, et l'onglet où voir le détail.</summary>
public sealed record Finding(Severity Severity, string Area, string Message, string Tab)
{
    public string Mark => Severity switch
    {
        Severity.Critical => "✘",
        Severity.Warning => "!",
        _ => "✔",
    };
}

/// <summary>Le bilan en 1 clic : une note sur 100 et les points triés du plus grave au moins grave.</summary>
public sealed class HealthReport
{
    private const int CriticalPenalty = 25;
    private const int WarningPenalty = 8;

    public HealthReport(IEnumerable<Finding> findings)
    {
        Findings = findings.OrderBy(f => f.Severity).ToList();
        Critical = Findings.Count(f => f.Severity == Severity.Critical);
        Warnings = Findings.Count(f => f.Severity == Severity.Warning);
        Score = Math.Max(0, 100 - Critical * CriticalPenalty - Warnings * WarningPenalty);
        Level = Critical > 0 || Score < 50 ? Severity.Critical : Warnings > 0 ? Severity.Warning : Severity.Ok;
    }

    public List<Finding> Findings { get; }
    public int Critical { get; }
    public int Warnings { get; }
    public int Passed => Findings.Count - Critical - Warnings;
    public int Score { get; }

    /// <summary>Un seul point rouge suffit à demander une intervention, quelle que soit la note.</summary>
    public Severity Level { get; }

    public string Verdict => Level switch
    {
        Severity.Critical => "Intervention nécessaire",
        Severity.Warning => "À surveiller",
        _ => "Bon état",
    };

    public string Counts => $"{Critical} critique(s) · {Warnings} à surveiller · {Passed} OK";
}
