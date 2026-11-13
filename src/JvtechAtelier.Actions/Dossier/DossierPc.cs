using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Jvtech.Lecture;
using Jvtech.Lecture.Regles;

namespace Jvtech.Atelier.Dossier;

/// <summary>
/// Dossier d'un PC à l'atelier : Atelier\&lt;identifiant&gt;\ sur la clé USB (à côté du .exe) ou sur le partage réglé
/// dans atelier.ini. Contient l'inventaire (valeur détectée ET correction), le journal des actions, puis les tests,
/// fiches, annonce et rapport. Rien n'est écrit sur le disque du PC préparé.
/// </summary>
public sealed class DossierPc
{
    public const string FichierInventaire = "inventaire.ini";
    public const string FichierJournal = "journal.txt";
    public const string FichierReglages = "atelier.ini";
    public const string FichierTests = "tests.ini";
    public const string FichierPreparation = "preparation.ini";
    public const string FichierFiche = "fiche.ini";

    private DossierPc(string chemin, string identifiant)
    {
        Chemin = chemin;
        Identifiant = identifiant;
    }

    public string Chemin { get; private set; }
    public string Identifiant { get; private set; }

    /// <summary>Racine des dossiers : « Dossiers= » de atelier.ini, sinon le dossier « Atelier » à côté du .exe.</summary>
    public static string Racine(string dossierExe) => ReglagesAtelier.Lire(dossierExe).Racine(dossierExe);

    /// <summary>Ouvre le dossier s'il existe, sinon le crée.</summary>
    public static DossierPc OuvrirOuCreer(string racine, string identifiant)
    {
        if (identifiant.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || identifiant.Contains(".."))
            throw new ArgumentException("Identifiant de dossier invalide : " + identifiant, nameof(identifiant));
        var chemin = Path.Combine(racine, identifiant);
        Directory.CreateDirectory(chemin);
        return new DossierPc(chemin, identifiant);
    }

    /// <summary>
    /// Dossier déjà créé pour ce PC (même numéro de série détecté), ou null. Le numéro de série retrouve le dossier
    /// même après son renommage en V… ; sans numéro de série lisible, rien n'est retrouvé (pas de devinette).
    /// </summary>
    public static DossierPc? Trouver(string racine, string? serieDetectee)
    {
        var serie = Champ.Nettoyer(serieDetectee);
        if (serie is null || !Directory.Exists(racine)) return null;
        foreach (var dir in Directory.GetDirectories(racine).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var dossier = new DossierPc(dir, Path.GetFileName(dir));
            if (string.Equals(dossier.LireSerieDetectee(), serie, StringComparison.OrdinalIgnoreCase)) return dossier;
        }
        return null;
    }

    private string? LireSerieDetectee()
    {
        var fichier = Path.Combine(Chemin, FichierInventaire);
        if (!File.Exists(fichier)) return null;
        var dansSerie = false;
        foreach (var brut in File.ReadAllLines(fichier, Encoding.UTF8))
        {
            var ligne = brut.Trim();
            if (ligne.StartsWith("[")) { dansSerie = ligne.Equals("[champ:serie]", StringComparison.OrdinalIgnoreCase); continue; }
            if (dansSerie && ligne.StartsWith("detecte=", StringComparison.OrdinalIgnoreCase))
                return Champ.Nettoyer(Deschapper(ligne.Substring("detecte=".Length)));
        }
        return null;
    }

    /// <summary>
    /// Le numéro d'inventaire V… est saisi après coup : le dossier prend ce nom. Refus si un dossier de ce nom
    /// existe déjà (deux PC avec le même numéro) : rien n'est fusionné ni écrasé.
    /// </summary>
    public void Renommer(string nouvelIdentifiant)
    {
        if (string.Equals(nouvelIdentifiant, Identifiant, StringComparison.OrdinalIgnoreCase)) return;
        if (nouvelIdentifiant.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Identifiant de dossier invalide : " + nouvelIdentifiant, nameof(nouvelIdentifiant));
        var cible = Path.Combine(Path.GetDirectoryName(Chemin)!, nouvelIdentifiant);
        if (Directory.Exists(cible))
            throw new IOException($"Un dossier {nouvelIdentifiant} existe déjà : vérifier le numéro d'inventaire.");
        Directory.Move(Chemin, cible);
        var ancien = Identifiant;
        Chemin = cible;
        Identifiant = nouvelIdentifiant;
        Journaliser($"Dossier renommé : {ancien} → {nouvelIdentifiant}");
    }

    /// <summary>Ajoute une ligne horodatée au journal du PC.</summary>
    public void Journaliser(string ligne, DateTime? quand = null)
    {
        var texte = $"{(quand ?? DateTime.Now):yyyy-MM-dd HH:mm:ss} | {ligne.Replace("\r", " ").Replace("\n", " ")}{Environment.NewLine}";
        File.AppendAllText(Path.Combine(Chemin, FichierJournal), texte, Encoding.UTF8);
    }

    /// <summary>
    /// Enregistre l'inventaire : pour chaque information, la valeur détectée, sa source et la correction éventuelle.
    /// Écriture dans un fichier temporaire puis remplacement : un arrêt brutal ne laisse pas un fichier à moitié écrit.
    /// </summary>
    public void EnregistrerInventaire(string? inventaire, IEnumerable<Champ> champs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("; Inventaire JVTECH Atelier : valeur détectée sur le PC et correction du technicien");
        sb.AppendLine("[pc]");
        sb.AppendLine("identifiant=" + Echapper(Identifiant));
        sb.AppendLine("inventaire=" + Echapper(inventaire));
        foreach (var c in champs)
        {
            sb.AppendLine();
            sb.AppendLine($"[champ:{c.Id}]");
            sb.AppendLine("nom=" + Echapper(c.Nom));
            sb.AppendLine("detecte=" + Echapper(c.Detecte));
            sb.AppendLine("source=" + Echapper(c.Source));
            sb.AppendLine("corrige=" + Echapper(c.Corrige));
            sb.AppendLine("corrige_par=" + Echapper(c.CorrigePar));
            sb.AppendLine("corrige_le=" + (c.CorrigeLe?.ToString("s", CultureInfo.InvariantCulture) ?? ""));
        }
        EcrireAtomique(Path.Combine(Chemin, FichierInventaire), sb.ToString());
    }

    /// <summary>Corrections déjà enregistrées, par identifiant de champ (pour les remettre sur un nouvel inventaire).</summary>
    public (string? Inventaire, Dictionary<string, (string? Corrige, string? Par, DateTime? Le)> Corrections) LireInventaire()
    {
        var corrections = new Dictionary<string, (string?, string?, DateTime?)>(StringComparer.OrdinalIgnoreCase);
        string? inventaire = null;
        var fichier = Path.Combine(Chemin, FichierInventaire);
        if (!File.Exists(fichier)) return (null, corrections);

        string? section = null;
        var valeurs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Fermer()
        {
            if (section is null) return;
            if (section == "pc") inventaire = Lire(valeurs, "inventaire");
            else if (section.StartsWith("champ:"))
            {
                DateTime? le = DateTime.TryParse(Lire(valeurs, "corrige_le"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
                corrections[section.Substring(6)] = (Lire(valeurs, "corrige"), Lire(valeurs, "corrige_par"), le);
            }
            valeurs.Clear();
        }
        foreach (var brut in File.ReadAllLines(fichier, Encoding.UTF8))
        {
            var ligne = brut.Trim();
            if (ligne.Length == 0 || ligne.StartsWith(";")) continue;
            if (ligne.StartsWith("[") && ligne.EndsWith("]")) { Fermer(); section = ligne.Substring(1, ligne.Length - 2); continue; }
            var egal = ligne.IndexOf('=');
            if (egal > 0) valeurs[ligne.Substring(0, egal)] = ligne.Substring(egal + 1);
        }
        Fermer();
        return (inventaire, corrections);
    }

    /// <summary>Remet sur l'inventaire fraîchement lu les corrections déjà faites (la valeur détectée reste celle du jour).</summary>
    public static void AppliquerCorrections(IEnumerable<Champ> champs, IReadOnlyDictionary<string, (string? Corrige, string? Par, DateTime? Le)> corrections)
    {
        foreach (var c in champs)
            if (corrections.TryGetValue(c.Id, out var corr))
                c.Restaurer(corr.Corrige, corr.Par, corr.Le);
    }

    /// <summary>Enregistre un document du PC (fiche, annonce) dans son dossier. Nom de fichier simple uniquement.</summary>
    public string EnregistrerTexte(string nomFichier, string contenu)
    {
        if (nomFichier.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || nomFichier.Contains(".."))
            throw new ArgumentException("Nom de fichier invalide : " + nomFichier, nameof(nomFichier));
        var chemin = Path.Combine(Chemin, nomFichier);
        EcrireAtomique(chemin, contenu);
        return chemin;
    }

    /// <summary>Enregistre les résultats des tests manuels (preuve : technicien, date, remarque).</summary>
    /// <summary>Avancement de la préparation (passes Windows Update, pack), relu après un redémarrage.</summary>
    public void EnregistrerPreparation(EtatPreparation etat) =>
        EcrireAtomique(Path.Combine(Chemin, FichierPreparation), etat.Ecrire());

    /// <summary>Null si la préparation n'a jamais été lancée sur ce PC.</summary>
    public EtatPreparation? LirePreparation()
    {
        var fichier = Path.Combine(Chemin, FichierPreparation);
        return File.Exists(fichier) ? EtatPreparation.Lire(File.ReadAllText(fichier, Encoding.UTF8)) : null;
    }

    /// <summary>Statut et réserves au moment où la fiche et l'annonce ont été enregistrées (contrôlé avant Sysprep).</summary>
    public void EnregistrerEmpreinteFiche(string empreinte, DateTime quand) =>
        EcrireAtomique(Path.Combine(Chemin, FichierFiche),
            "; Fiche interne et annonce enregistrées avec ce statut" + Environment.NewLine
            + "empreinte=" + Echapper(empreinte) + Environment.NewLine
            + "le=" + quand.ToString("s", CultureInfo.InvariantCulture) + Environment.NewLine);

    public string? LireEmpreinteFiche()
    {
        var fichier = Path.Combine(Chemin, FichierFiche);
        if (!File.Exists(fichier)) return null;
        foreach (var ligne in File.ReadAllLines(fichier, Encoding.UTF8))
            if (ligne.StartsWith("empreinte=", StringComparison.Ordinal))
                return Deschapper(ligne.Substring("empreinte=".Length));
        return null;
    }

    public void EnregistrerTests(IEnumerable<ResultatTestManuel> resultats)
    {
        var sb = new StringBuilder();
        sb.AppendLine("; Tests manuels JVTECH Atelier");
        foreach (var r in resultats)
        {
            sb.AppendLine($"[test:{r.Id}]");
            sb.AppendLine("resultat=" + r.Resultat);
            sb.AppendLine("portee=" + r.PorteeEchec);
            sb.AppendLine("remarque=" + Echapper(r.Remarque));
            sb.AppendLine("technicien=" + Echapper(r.Technicien));
            sb.AppendLine("le=" + (r.Le?.ToString("s", CultureInfo.InvariantCulture) ?? ""));
            sb.AppendLine();
        }
        EcrireAtomique(Path.Combine(Chemin, FichierTests), sb.ToString());
    }

    public Dictionary<string, ResultatTestManuel> LireTests()
    {
        var tests = new Dictionary<string, ResultatTestManuel>(StringComparer.OrdinalIgnoreCase);
        foreach (var (section, valeurs) in LireSections(Path.Combine(Chemin, FichierTests)))
        {
            if (!section.StartsWith("test:", StringComparison.OrdinalIgnoreCase)) continue;
            var id = section.Substring(5);
            Enum.TryParse<ResultatTest>(Lire(valeurs, "resultat"), out var resultat);
            if (!Enum.TryParse<Portee>(Lire(valeurs, "portee"), out var portee)) portee = Portee.Bloquante;
            DateTime? le = DateTime.TryParse(Lire(valeurs, "le"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            tests[id] = new ResultatTestManuel(id, resultat, portee, Lire(valeurs, "remarque"), Lire(valeurs, "technicien"), le);
        }
        return tests;
    }

    private static IEnumerable<(string Section, Dictionary<string, string> Valeurs)> LireSections(string fichier)
    {
        if (!File.Exists(fichier)) yield break;
        string? section = null;
        var valeurs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var brut in File.ReadAllLines(fichier, Encoding.UTF8))
        {
            var ligne = brut.Trim();
            if (ligne.Length == 0 || ligne.StartsWith(";")) continue;
            if (ligne.StartsWith("[") && ligne.EndsWith("]"))
            {
                if (section is not null) yield return (section, valeurs);
                section = ligne.Substring(1, ligne.Length - 2);
                valeurs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var egal = ligne.IndexOf('=');
            if (egal > 0) valeurs[ligne.Substring(0, egal)] = ligne.Substring(egal + 1);
        }
        if (section is not null) yield return (section, valeurs);
    }

    private static string? Lire(Dictionary<string, string> valeurs, string cle) =>
        valeurs.TryGetValue(cle, out var v) && v.Length > 0 ? Deschapper(v) : null;

    internal static string Echapper(string? texte) =>
        (texte ?? "").Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n");

    internal static string Deschapper(string texte)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < texte.Length; i++)
        {
            if (texte[i] == '\\' && i + 1 < texte.Length)
            {
                i++;
                sb.Append(texte[i] == 'n' ? '\n' : texte[i]);
            }
            else sb.Append(texte[i]);
        }
        return sb.ToString();
    }

    private static void EcrireAtomique(string fichier, string contenu)
    {
        var temp = fichier + ".tmp";
        File.WriteAllText(temp, contenu, new UTF8Encoding(true));
        if (!File.Exists(fichier))
        {
            File.Move(temp, fichier);
            return;
        }
        try { File.Replace(temp, fichier, null); }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // Certains partages réseau et systèmes de fichiers de clé USB refusent le remplacement direct.
            File.Copy(temp, fichier, overwrite: true);
            File.Delete(temp);
        }
    }
}
