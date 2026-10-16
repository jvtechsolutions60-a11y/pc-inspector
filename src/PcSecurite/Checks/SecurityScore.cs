using System;
using System.Collections.Generic;
using System.Linq;

namespace PcSecurite.Checks;

public static class SecurityScore
{
    /// <summary>Note sur 100 : un point vert vaut 1, orange 0,5, rouge 0. Les infos ne comptent pas.</summary>
    public static int Compute(IEnumerable<SecurityCheck> checks)
    {
        var scored = checks.Where(c => c.Status != CheckStatus.Info).ToList();
        if (scored.Count == 0) return 0;
        var points = scored.Sum(c => c.Status switch { CheckStatus.Ok => 1.0, CheckStatus.Warning => 0.5, _ => 0.0 });
        var score = (int)Math.Round(points * 100 / scored.Count);
        // Un seul point rouge suffit à ne pas afficher une note rassurante.
        return scored.Any(c => c.Status == CheckStatus.Danger) ? Math.Min(score, 69) : score;
    }

    /// <summary>Classe de A à E, façon Nutri-Score, pour un affichage lisible par le client (annonces, rapport).</summary>
    public static char Letter(int score) => score switch
    {
        >= 90 => 'A',
        >= 75 => 'B',
        >= 60 => 'C',
        >= 40 => 'D',
        _ => 'E',
    };

    public static string Verdict(int score) => score switch
    {
        >= 90 => "Bien protégé",
        >= 70 => "Protection correcte, quelques points à améliorer",
        >= 50 => "Protection insuffisante",
        _ => "PC exposé, intervention recommandée",
    };
}
