using System.Collections.Generic;
using System.Linq;
using PcInspector.Models;

namespace PcSecurite.Checks;

/// <summary>Met les contrôles au format du rapport de PC Inspector (même mise en page, même en-tête client).</summary>
public static class SecurityReport
{
    /// <param name="initialScore">Note de la première analyse, pour montrer l'avant/après de l'intervention.</param>
    /// <param name="corrected">Points rouges ou orange à la première analyse et en ordre maintenant.</param>
    public static List<InfoSection> ToSections(IReadOnlyList<SecurityCheck> checks, int? initialScore = null, IReadOnlyCollection<string>? corrected = null)
    {
        var score = SecurityScore.Compute(checks);
        var section = new InfoSection("Bilan de sécurité");
        var summary = new InfoGroup("Note globale")
            .Add("Classe", $"{SecurityScore.Letter(score)} (de A, très bien protégé, à E, exposé)")
            .Add("Note", $"{score} / 100")
            .Add("Verdict", SecurityScore.Verdict(score));
        if (initialScore is { } before && before != score)
            summary.Add("Avant intervention", $"{SecurityScore.Letter(before)}, {before} / 100");
        section.Groups.Add(summary);

        if (corrected is { Count: > 0 })
        {
            var done = new InfoGroup("Corrigé pendant l'intervention");
            foreach (var name in corrected) done.Add(name, "✅ En ordre");
            section.Groups.Add(done);
        }

        // Même ordre qu'à l'écran : corrections, puis ce qu'il faut changer, les plus graves en premier.
        var sorted = checks.OrderBy(c => c.GroupOrder).ThenBy(c => c.Severity).ThenBy(c => c.Order).ToList();
        var toFix = new InfoGroup(SecurityCheck.CorrectionsTitle);
        var toChange = new InfoGroup(SecurityCheck.ChangesTitle);
        var results = new InfoGroup("Détail des contrôles");
        foreach (var check in sorted)
        {
            results.Add(check.Name, $"{check.Symbol} {check.Detail}");
            if (check.NeedsAction && check.Advice is not null)
                (check.Kind == CheckKind.Fix ? toFix : toChange).Add(check.Name, $"{check.Symbol} {check.Advice}"
                    + (check.Tutorial is null ? "" : "\nComment faire :\n" + string.Join("\n", check.Tutorial.Numbered())));
        }
        if (toFix.Items.Count > 0) section.Groups.Add(toFix);
        if (toChange.Items.Count > 0) section.Groups.Add(toChange);
        section.Groups.Add(results);
        return new List<InfoSection> { section };
    }
}
