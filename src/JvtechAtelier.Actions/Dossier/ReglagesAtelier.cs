using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Jvtech.Lecture.Regles;

namespace Jvtech.Atelier.Dossier;

/// <summary>
/// Réglages de l'atelier, dans atelier.ini à côté du .exe (sur la clé). Toutes les clés sont facultatives :
/// <code>
/// Dossiers=\\nas\atelier        ; sinon le dossier « Atelier » à côté du .exe
/// Technicien=Julien
/// BatterieSansMention=80        ; % de capacité d'origine à partir duquel la batterie n'est pas mentionnée
/// BatterieReserve=60            ; en dessous de « sans mention » : réserve ; en dessous de celui-ci : réserve + remplacement conseillé
/// </code>
/// </summary>
public sealed class ReglagesAtelier
{
    private readonly Dictionary<string, string> _valeurs;

    private ReglagesAtelier(Dictionary<string, string> valeurs) => _valeurs = valeurs;

    public static ReglagesAtelier Lire(string dossierExe)
    {
        var valeurs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fichier = Path.Combine(dossierExe, DossierPc.FichierReglages);
        if (File.Exists(fichier))
            foreach (var ligne in File.ReadAllLines(fichier, Encoding.UTF8))
            {
                var t = ligne.Trim();
                if (t.Length == 0 || t.StartsWith(";") || t.StartsWith("[")) continue;
                var egal = t.IndexOf('=');
                if (egal <= 0) continue;
                var valeur = t.Substring(egal + 1);
                var commentaire = valeur.IndexOf(" ;", StringComparison.Ordinal);
                if (commentaire >= 0) valeur = valeur.Substring(0, commentaire);
                valeurs[t.Substring(0, egal).Trim()] = valeur.Trim().Trim('"');
            }
        return new ReglagesAtelier(valeurs);
    }

    public string? Valeur(string cle) => _valeurs.TryGetValue(cle, out var v) && v.Length > 0 ? v : null;

    public string Racine(string dossierExe) => Valeur("Dossiers") ?? Path.Combine(dossierExe, "Atelier");

    public string? Technicien => Valeur("Technicien");

    public SeuilsBatterie Seuils
    {
        get
        {
            int Entier(string cle, int defaut) =>
                int.TryParse(Valeur(cle), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : defaut;
            return new SeuilsBatterie(Entier("BatterieSansMention", 80), Entier("BatterieReserve", 60)).Valides();
        }
    }
}
