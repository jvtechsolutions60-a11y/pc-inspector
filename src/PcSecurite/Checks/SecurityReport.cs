using System.Collections.Generic;
using PcInspector.Models;

namespace PcSecurite.Checks;

/// <summary>Met les contrôles au format du rapport de PC Inspector (même mise en page, même en-tête client).</summary>
public static class SecurityReport
{
    public static List<InfoSection> ToSections(IReadOnlyList<SecurityCheck> checks)
    {
        var score = SecurityScore.Compute(checks);
        var section = new InfoSection("Bilan de sécurité");
        section.Groups.Add(new InfoGroup("Note globale")
            .Add("Note", $"{score} / 100")
            .Add("Verdict", SecurityScore.Verdict(score)));

        var toFix = new InfoGroup("À corriger");
        var results = new InfoGroup("Détail des contrôles");
        foreach (var check in checks)
        {
            results.Add(check.Name, $"{check.Symbol} {check.Detail}");
            if (check.Advice is not null && check.Status is CheckStatus.Danger or CheckStatus.Warning)
                toFix.Add(check.Name, $"{check.Symbol} {check.Advice}");
        }
        if (toFix.Items.Count > 0) section.Groups.Add(toFix);
        section.Groups.Add(results);
        return new List<InfoSection> { section };
    }
}
