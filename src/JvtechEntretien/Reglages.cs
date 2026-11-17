using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace JvtechEntretien;

/// <summary>
/// Réglages lus dans entretien.ini à côté du .exe. Une valeur absente ou illisible garde la valeur par défaut :
/// une faute de frappe n'empêche jamais les mises à jour.
/// </summary>
public sealed class Reglages
{
    /// <summary>
    /// Liste sûre par défaut : logiciels courants dont la mise à jour silencieuse ne casse rien. Les logiciels métier,
    /// pilotes, Office, antivirus et outils de prise en main ne sont jamais mis à jour par l'entretien.
    /// </summary>
    public static readonly IReadOnlyList<string> ListeSure = new[]
    {
        "Google.Chrome", "Mozilla.Firefox", "Mozilla.Firefox.ESR", "Mozilla.Thunderbird",
        "Adobe.Acrobat.Reader.64-bit", "Adobe.Acrobat.Reader.32-bit", "Foxit.FoxitReader",
        "7zip.7zip", "RARLab.WinRAR", "VideoLAN.VLC", "Notepad++.Notepad++",
        "TheDocumentFoundation.LibreOffice", "Zoom.Zoom", "Oracle.JavaRuntimeEnvironment",
        "IrfanSkiljan.IrfanView", "SumatraPDF.SumatraPDF", "Audacity.Audacity", "GIMP.GIMP",
    };

    public string Client { get; private set; } = "";
    public string Poste { get; private set; } = Environment.MachineName;
    public List<string> Logiciels { get; } = new(ListeSure);
    public double DisqueLibreAlerte { get; private set; } = 15;

    /// <summary>Dossier partagé du NAS du client (\\NAS\partage\…) où déposer le rapport et la ligne du tableau de suivi.</summary>
    public string DossierPartage { get; private set; } = "";
    /// <summary>Compte du NAS autorisé à écrire (la tâche tourne en compte SYSTEM, que le NAS ne connaît pas).</summary>
    public string PartageUtilisateur { get; private set; } = "";
    public string PartageMotDePasse { get; private set; } = "";
    public List<string> Ignorees { get; } = new();


    public static Reglages Charger(string chemin) => File.Exists(chemin) ? Lire(File.ReadAllText(chemin)) : new Reglages();

    public static Reglages Lire(string texte)
    {
        var r = new Reglages();
        foreach (var brute in texte.Split('\n'))
        {
            var ligne = brute.Trim();
            if (ligne.Length == 0 || ligne[0] is ';' or '#' or '[') continue;
            var egal = ligne.IndexOf('=');
            if (egal <= 0) { r.Ignorees.Add(ligne); continue; }
            var cle = ligne.Substring(0, egal).Trim().ToLowerInvariant();
            var valeur = ligne.Substring(egal + 1).Trim();
            // Le mot de passe n'apparaît jamais dans le journal, même mal écrit.
            if (!r.Appliquer(cle, valeur)) r.Ignorees.Add(cle.Contains("mot_de_passe") ? cle + " = ***" : ligne);
        }
        return r;
    }

    private bool Appliquer(string cle, string valeur)
    {
        switch (cle)
        {
            case "client": Client = valeur; return true;
            case "poste": if (valeur.Length > 0) Poste = valeur; return true;
            case "logiciels":
                var ids = Liste(valeur).ToList();
                if (ids.Count == 0) return true; // vide : liste sûre par défaut
                Logiciels.Clear();
                Logiciels.AddRange(ids);
                return true;
            case "dossier_partage": DossierPartage = valeur.TrimEnd('\\'); return true;
            case "partage_utilisateur": PartageUtilisateur = valeur; return true;
            case "partage_mot_de_passe": PartageMotDePasse = valeur; return true;
            case "disque_libre_alerte":
                if (!double.TryParse(valeur.Replace(',', '.').TrimEnd('%', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out var seuil) || seuil is < 0 or > 100) return false;
                DisqueLibreAlerte = seuil; return true;
            default: return false;
        }
    }

    private static IEnumerable<string> Liste(string valeur) =>
        valeur.Split(';', ',').Select(v => v.Trim()).Where(v => v.Length > 0);
}
