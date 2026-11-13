using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jvtech.Lecture;

/// <summary>
/// Identifiant du dossier d'un PC à l'atelier : le numéro d'inventaire VENTE NANTERRE (V…) s'il est connu,
/// sinon AT-date-4 derniers caractères du numéro de série (ou l'heure si le PC n'a pas de numéro lisible).
/// </summary>
public static class IdentifiantAtelier
{
    private static readonly Regex Inventaire = new(@"^\s*[Vv]\s*-?\s*(?<n>\d{1,7})\s*$");

    /// <summary>« v 1042 », « V-1042 » → « V1042 » ; null si vide ou pas au format V suivi de chiffres.</summary>
    public static string? NormaliserInventaire(string? saisie)
    {
        if (string.IsNullOrWhiteSpace(saisie)) return null;
        var m = Inventaire.Match(saisie);
        return m.Success ? "V" + m.Groups["n"].Value : null;
    }

    public static bool InventaireValide(string? saisie) => string.IsNullOrWhiteSpace(saisie) || NormaliserInventaire(saisie) is not null;

    public static string Calculer(string? inventaire, string? serie, DateTime quand)
    {
        if (NormaliserInventaire(inventaire) is { } v) return v;
        var propre = new string((Champ.Nettoyer(serie) ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var suffixe = propre.Length >= 4 ? propre.Substring(propre.Length - 4) : quand.ToString("HHmm");
        return $"AT-{quand:yyyyMMdd}-{suffixe}";
    }
}
