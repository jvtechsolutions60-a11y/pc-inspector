using System;
using System.IO;
using System.Linq;
using System.Text;

namespace JvtechEntretien;

/// <summary>
/// File d'attente vers le NAS, gardée sur le PC (C:\ProgramData\JVTECH\Entretien\a-envoyer) : rapports, lignes des
/// tableaux et journal. Tout est d'abord mis en file, puis envoyé ; si le NAS ne répond pas (portable en
/// déplacement, NAS éteint), rien n'est perdu : l'envoi est retenté à la prochaine ouverture de session.
///
/// Sur le NAS :
///   suivi-entretien.csv    une ligne par PC et par ouverture de session (état avant/après, mises à jour)
///   POSTE\                 un dossier par PC : rapports (POSTE_2026-11-17_0905.html) et journal.log
/// </summary>
internal static class Envois
{
    public const string SuiviEntretien = "suivi-entretien.csv";
    public const string Journal = "journal.log";

    private static string File_ => Path.Combine(Suivi.Dossier, "a-envoyer");

    public static void Rapport(string nom, string html) => Ecrire(Path.Combine(File_, "rapports", nom), html, ajouter: false);

    public static void Ligne(string tableau, string ligne) => Ecrire(Path.Combine(File_, tableau), ligne + "\r\n", ajouter: true);

    public static void JournalNas(string texte) => Ecrire(Path.Combine(File_, Journal), texte, ajouter: true);

    private static void Ecrire(string chemin, string contenu, bool ajouter)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
            if (ajouter) File.AppendAllText(chemin, contenu, new UTF8Encoding(false));
            else File.WriteAllText(chemin, contenu, new UTF8Encoding(true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Suivi.Journal($"Mise en attente impossible ({Path.GetFileName(chemin)}) : {ex.Message}");
        }
    }

    /// <summary>Envoie tout ce qui attend. Renvoie null si la file est vide ensuite, sinon la raison de l'échec.</summary>
    public static string? Envoyer(Reglages reglages)
    {
        if (!Directory.Exists(File_)) return null;
        if (reglages.DossierPartage.Length == 0) return "pas de NAS réglé (dossier_partage) : tout reste sur le PC";
        return Nas.Ecrire(reglages, dossier =>
        {
            var poste = Path.Combine(dossier, NomDossier(reglages.Poste));
            var rapports = Path.Combine(File_, "rapports");
            if (Directory.Exists(rapports))
            {
                foreach (var fichier in Directory.GetFiles(rapports))
                {
                    Directory.CreateDirectory(poste);
                    File.Copy(fichier, Path.Combine(poste, Path.GetFileName(fichier)), overwrite: true);
                    File.Delete(fichier);
                }
            }
            Vider(Path.Combine(File_, SuiviEntretien), Path.Combine(dossier, SuiviEntretien), JvtechEntretien.Rapport.EnteteCsv);
            var journal = Path.Combine(File_, Journal);
            if (File.Exists(journal))
            {
                Directory.CreateDirectory(poste);
                Vider(journal, Path.Combine(poste, Journal), null);
            }
        });
    }

    /// <summary>Ajoute les lignes en attente au fichier du NAS, puis les efface du PC.</summary>
    private static void Vider(string local, string nas, string? entete)
    {
        if (!File.Exists(local)) return;
        var lignes = File.ReadAllText(local, Encoding.UTF8);
        if (lignes.Length > 0) Nas.AjouterLignes(nas, entete, lignes);
        File.Delete(local);
    }

    internal static string NomDossier(string poste)
    {
        var nom = string.Concat(poste.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        return nom.Length == 0 ? "poste" : nom;
    }
}
