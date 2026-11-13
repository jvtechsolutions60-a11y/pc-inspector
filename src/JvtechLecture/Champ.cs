using System;
using System.Collections.Generic;

namespace Jvtech.Lecture;

/// <summary>
/// Une information du PC telle que détectée, et sa correction éventuelle par le technicien (ajustement 6 du 13/11/2026).
/// La valeur détectée n'est jamais perdue : elle reste visible dans la fiche interne et le journal.
/// Une information absente reste « Non détecté » : rien n'est déduit ni inventé.
/// </summary>
public sealed class Champ
{
    public const string NonDetecte = "Non détecté";

    /// <summary>Textes de remplissage laissés par les fabricants à la place d'une vraie valeur.</summary>
    private static readonly HashSet<string> Remplissage = new(StringComparer.OrdinalIgnoreCase)
    {
        "To Be Filled By O.E.M.", "To be filled by O.E.M", "Default string", "System Product Name", "System manufacturer",
        "System Serial Number", "System Version", "Not Applicable", "Not Specified", "Not Available", "None", "N/A",
        "O.E.M.", "OEM", "Base Board Product Name", "Chassis Serial Number", "Invalid", "Unknown", "Inconnu",
    };

    public Champ(string nom, string? brut, string? source = null)
    {
        Id = nom;
        Nom = nom;
        Detecte = Nettoyer(brut);
        Source = source;
    }

    /// <summary>Identifiant stable (« modele », « serie »…) pour l'enregistrement ; par défaut le nom affiché.</summary>
    public string Id { get; init; }

    public string Nom { get; }

    /// <summary>Valeur lue sur le PC, ou null si absente, vide ou texte de remplissage du fabricant.</summary>
    public string? Detecte { get; }

    /// <summary>D'où vient la valeur détectée (classe WMI, registre…).</summary>
    public string? Source { get; }

    /// <summary>Valeur saisie par le technicien, ou null.</summary>
    public string? Corrige { get; private set; }

    public string? CorrigePar { get; private set; }
    public DateTime? CorrigeLe { get; private set; }

    /// <summary>Valeur retenue : la correction si elle existe, sinon la valeur détectée, sinon null.</summary>
    public string? Valeur => Corrige ?? Detecte;

    public bool EstCorrige => Corrige is not null;
    public bool EstConnu => Valeur is not null;

    /// <summary>Texte à afficher : jamais vide, « Non détecté » si rien n'est connu.</summary>
    public string Affichage => Valeur ?? NonDetecte;

    /// <summary>Remplace la valeur affichée. Une correction identique à la valeur détectée est ignorée.</summary>
    public void Corriger(string valeur, string operateur, DateTime quand)
    {
        var propre = Nettoyer(valeur)
            ?? throw new ArgumentException("Correction vide : utiliser AnnulerCorrection pour revenir à la valeur détectée.", nameof(valeur));
        if (string.Equals(propre, Detecte, StringComparison.Ordinal))
        {
            AnnulerCorrection();
            return;
        }
        Corrige = propre;
        CorrigePar = string.IsNullOrWhiteSpace(operateur) ? "?" : operateur.Trim();
        CorrigeLe = quand;
    }

    /// <summary>Recharge une correction déjà enregistrée (dossier du PC), sans perdre qui l'a faite ni quand.</summary>
    public void Restaurer(string? corrige, string? par, DateTime? le)
    {
        var propre = Nettoyer(corrige);
        if (propre is null || string.Equals(propre, Detecte, StringComparison.Ordinal))
        {
            AnnulerCorrection();
            return;
        }
        Corrige = propre;
        CorrigePar = string.IsNullOrWhiteSpace(par) ? "?" : par!.Trim();
        CorrigeLe = le;
    }

    public void AnnulerCorrection()
    {
        Corrige = null;
        CorrigePar = null;
        CorrigeLe = null;
    }

    /// <summary>Ligne pour la fiche interne : la valeur retenue et, si elle a été corrigée, la valeur détectée.</summary>
    public string LigneFicheInterne() => EstCorrige
        ? $"{Nom} : {Corrige} (corrigé par {CorrigePar}, détecté : {Detecte ?? NonDetecte})"
        : $"{Nom} : {Affichage}";

    /// <summary>Espaces retirés ; vide ou texte de remplissage du fabricant = null.</summary>
    public static string? Nettoyer(string? brut)
    {
        if (brut is null) return null;
        var texte = brut.Replace('\0', ' ').Trim();
        while (texte.Contains("  ")) texte = texte.Replace("  ", " ");
        if (texte.Length == 0 || Remplissage.Contains(texte)) return null;
        // Numéros de série factices : que des zéros, des espaces, des points ou des tirets.
        if (texte.Trim('0', ' ', '.', '-', 'x', 'X').Length == 0) return null;
        return texte;
    }
}
