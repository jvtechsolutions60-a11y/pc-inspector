using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jvtech.Lecture.Regles;

/// <summary>Ce qui sert à écrire les fiches et l'annonce d'un PC.</summary>
public sealed record DonneesFiche(
    string? Inventaire,
    IReadOnlyList<Champ> Champs,
    IReadOnlyList<Controle> Controles,
    BilanVente Bilan,
    MesureBatterie? Batterie,
    string? Etat,
    string? Remarques);

/// <summary>Annonce et fiche commerciale prêtes à copier, avec les avertissements pour le technicien.</summary>
public sealed record TexteAnnonce(string Titre, string Description, IReadOnlyList<string> Avertissements);

/// <summary>
/// Fiches et annonce (cahier § 3.6, ajustement 6). Règles :
/// - numéro d'inventaire V… en tête du titre ;
/// - seules les valeurs connues (détectées ou corrigées) sont écrites : rien d'inventé, jamais « Non détecté » dans l'annonce ;
/// - les réserves commerciales sont toujours écrites ;
/// - aucun lien, aucun prix.
/// </summary>
public static class Annonce
{
    public const int LongueurTitreMax = 100; // limite de Leboncoin
    private static readonly Regex Lien = new(@"(https?://|www\.)\S+", RegexOptions.IgnoreCase);

    private static string? Valeur(DonneesFiche d, string id) => d.Champs.FirstOrDefault(c => c.Id == id)?.Valeur;

    public static TexteAnnonce Composer(DonneesFiche d)
    {
        var avertissements = new List<string>();
        if (d.Bilan.Statut is StatutVente.Incomplet or StatutVente.Bloque)
            avertissements.Add($"Statut avant vente : {d.Bilan.Libelle}. Ne pas publier avant d'avoir levé les points restants.");
        if (IdentifiantAtelier.NormaliserInventaire(d.Inventaire) is null)
            avertissements.Add("Numéro d'inventaire V… manquant : il doit être en tête du titre.");

        var titre = Titre(d);
        if (titre.Length > LongueurTitreMax) avertissements.Add($"Titre de {titre.Length} caractères : Leboncoin le coupe à {LongueurTitreMax}.");

        var sb = new StringBuilder();
        sb.AppendLine(titre);
        sb.AppendLine();
        if (Propre(d.Etat, avertissements) is { } etat) { sb.AppendLine("État : " + etat); sb.AppendLine(); }

        sb.AppendLine("Configuration :");
        foreach (var (libelle, valeur) in Lignes(d)) sb.AppendLine($"- {libelle} : {valeur}");

        var reserves = d.Bilan.Reserves.Select(r => $"- {Court(r.Titre)} : {ResumeReserve(r)}").ToList();
        if (reserves.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("À savoir :");
            reserves.ForEach(r => sb.AppendLine(r));
        }

        var testes = d.Controles.Where(c => c.Id.StartsWith("test-") && c.Statut == Statut.Conforme).Select(c => Court(c.Titre)).ToList();
        if (testes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Testé : " + string.Join(", ", testes) + ".");
        }

        if (Propre(d.Remarques, avertissements) is { } remarques) { sb.AppendLine(); sb.AppendLine(remarques); }
        sb.AppendLine();
        sb.AppendLine("PC contrôlé et préparé par JVTECH Solutions.");
        return new TexteAnnonce(titre, sb.ToString(), avertissements);
    }

    /// <summary>« V1042 · Lenovo ThinkPad T480 · i5-8350U · 16 Go · SSD 256 Go ». Parties inconnues omises.</summary>
    public static string Titre(DonneesFiche d)
    {
        var parties = new List<string?>
        {
            IdentifiantAtelier.NormaliserInventaire(d.Inventaire),
            NomPc(Valeur(d, "fabricant"), Valeur(d, "modele")),
            ProcesseurCourt(Valeur(d, "processeur")),
            MemoireCourte(Valeur(d, "memoire")),
            StockageCourt(Valeur(d, "stockage")),
        };
        var morceaux = parties.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
        // Trop long : on retire d'abord le stockage puis la mémoire, jamais le numéro ni le modèle.
        while (string.Join(" · ", morceaux).Length > LongueurTitreMax && morceaux.Count > 2) morceaux.RemoveAt(morceaux.Count - 1);
        return string.Join(" · ", morceaux);
    }

    public static IEnumerable<(string Libelle, string Valeur)> Lignes(DonneesFiche d)
    {
        var lignes = new (string Libelle, string? Valeur)[]
        {
            ("Modèle", NomPc(Valeur(d, "fabricant"), Valeur(d, "modele"))),
            ("Processeur", Valeur(d, "processeur")),
            ("Mémoire", Valeur(d, "memoire")),
            ("Stockage", Valeur(d, "stockage")),
            ("Carte graphique", Valeur(d, "graphique")),
            ("Écran", Valeur(d, "ecran")),
            ("Batterie", d.Batterie is { NombreBatteries: > 0, SantePourcent: { } s } b
                ? $"{s} % de la capacité d'origine" + (b.Cycles is > 0 ? $", {b.Cycles} cycles" : "")
                : null),
            ("Système", Valeur(d, "windows")),
        };
        return lignes.Where(l => !string.IsNullOrWhiteSpace(l.Valeur)).Select(l => (l.Libelle, l.Valeur!.Trim()));
    }

    /// <summary>Fiche interne : chaque valeur avec sa valeur détectée si elle a été corrigée, chaque contrôle avec sa preuve.</summary>
    public static string FicheInterne(DonneesFiche d, string mode, DateTime quand)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"FICHE INTERNE JVTECH ATELIER · {IdentifiantAtelier.NormaliserInventaire(d.Inventaire) ?? "sans numéro d'inventaire"} · {quand:dd/MM/yyyy HH:mm}");
        sb.AppendLine(mode);
        sb.AppendLine();
        sb.AppendLine("Statut avant vente : " + d.Bilan.Libelle);
        foreach (var b in d.Bilan.Bloquants) sb.AppendLine($"  ✕ bloquant : {b.Titre} : {b.Constat}");
        foreach (var m in d.Bilan.Manquants) sb.AppendLine($"  ○ non fait ou à vérifier : {m}");
        foreach (var r in d.Bilan.Reserves) sb.AppendLine($"  ? réserve commerciale : {r.Titre} : {r.Constat}");
        sb.AppendLine();
        sb.AppendLine("Inventaire (valeur retenue ; valeur détectée si corrigée) :");
        foreach (var c in d.Champs) sb.AppendLine("  " + c.LigneFicheInterne() + (c.Source is null ? "" : $"  [{c.Source}]"));
        if (d.Batterie is { NombreBatteries: > 0 } bat)
        {
            sb.AppendLine();
            sb.AppendLine("Batterie :");
            foreach (var l in bat.Details()) sb.AppendLine("  " + l);
        }
        sb.AppendLine();
        sb.AppendLine("Contrôles et tests :");
        foreach (var c in d.Controles)
            sb.AppendLine($"  [{Libelles.De(c.Statut)}{(c.Statut == Statut.Anomalie ? ", " + Libelles.De(c.Portee) : "")}] {c.Titre} : {c.Constat}");
        return sb.ToString();
    }

    public static string? NomPc(string? fabricant, string? modele)
    {
        var f = Champ.Nettoyer(fabricant);
        var m = Champ.Nettoyer(modele);
        if (m is null) return null;
        if (f is null) return m;
        var fCourt = Regex.Replace(f, @"\s*(Inc\.?|Corporation|Corp\.?|Co\.,? ?Ltd\.?|Technology|Computer|International)\b.*$", "", RegexOptions.IgnoreCase).Trim();
        fCourt = fCourt.ToUpperInvariant() switch
        {
            "LENOVO" => "Lenovo",
            "HEWLETT-PACKARD" => "HP",
            "ASUSTEK" => "ASUS",
            "MICRO-STAR" => "MSI",
            "TOSHIBA" => "Toshiba",
            "FUJITSU" => "Fujitsu",
            _ => fCourt,
        };
        return m.StartsWith(fCourt, StringComparison.OrdinalIgnoreCase) ? m : $"{fCourt} {m}";
    }

    public static string? ProcesseurCourt(string? nom)
    {
        if (Champ.Nettoyer(nom) is not { } n) return null;
        n = Regex.Replace(n, @"\((R|TM|tm)\)", "");
        n = Regex.Replace(n, @"\s+CPU\b|\s+@.*$|\s+with Radeon.*$|\s+\d+-Core Processor|\s+Processor\b|\s+Mobile\b", "", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"^Intel\s+Core\s+", "", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"^(\d+)(th|rd|nd|st) Gen Intel\s+Core\s+", "", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"^AMD\s+", "", RegexOptions.IgnoreCase);
        return Regex.Replace(n, @"\s{2,}", " ").Trim();
    }

    public static string? MemoireCourte(string? memoire)
    {
        var m = Regex.Match(memoire ?? "", @"\d+\s*Go", RegexOptions.IgnoreCase);
        return m.Success ? Regex.Replace(m.Value, @"\s+", " ") : null;
    }

    public static string? StockageCourt(string? stockage)
    {
        if (Champ.Nettoyer(stockage) is not { } s) return null;
        var parties = s.Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Regex.Replace(p, @"\s*\(.*?\)", ""))
            .Select(p => Regex.Replace(p, @"\b(NVMe|SATA)\s*", "", RegexOptions.IgnoreCase).Trim())
            .Where(p => p.Length > 0);
        return string.Join(" + ", parties);
    }

    private static string Court(string titre) => Regex.Replace(titre, @"\s*\(.*\)$", "");

    /// <summary>Réserve telle qu'écrite pour l'acheteur : sans la preuve interne (technicien, heure) ni les détails techniques.</summary>
    public static string ResumeReserve(Controle r)
    {
        var c = Regex.Replace(r.Constat, @"\s*\([^()]*\d{2}/\d{2}/\d{4}[^()]*\)$", "");
        if (c.StartsWith("Échec : ", StringComparison.Ordinal)) c = c.Substring("Échec : ".Length);
        else if (c == "Échec") c = "défaut constaté";
        var point = c.IndexOf(". ", StringComparison.Ordinal);
        return (point > 0 ? c.Substring(0, point) : c).TrimEnd('.');
    }

    /// <summary>Texte libre du technicien : liens retirés (pas de lien dans l'annonce), avec un avertissement.</summary>
    private static string? Propre(string? texte, List<string> avertissements)
    {
        if (string.IsNullOrWhiteSpace(texte)) return null;
        var t = texte!.Trim();
        if (Lien.IsMatch(t))
        {
            t = Lien.Replace(t, "").Trim();
            avertissements.Add("Lien retiré du texte : pas de lien dans l'annonce.");
        }
        return t.Length == 0 ? null : t;
    }
}
