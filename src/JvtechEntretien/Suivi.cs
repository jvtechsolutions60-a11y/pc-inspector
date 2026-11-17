using System;
using System.IO;
using System.Linq;
using System.Text;

namespace JvtechEntretien;

/// <summary>
/// Fichiers gardés sur le PC, dans C:\ProgramData\JVTECH\Entretien : journal et rapports des 60 derniers jours.
/// </summary>
internal static class Suivi
{
    private const long JournalMax = 1024 * 1024;

    public static string Dossier =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "JVTECH", "Entretien");

    /// <summary>Enregistre un fichier du dossier (rapport). Renvoie son chemin, ou null en cas d'échec.</summary>
    public static string? Enregistrer(string nom, string contenu)
    {
        try
        {
            Directory.CreateDirectory(Dossier);
            var chemin = Path.Combine(Dossier, nom);
            File.WriteAllText(chemin, contenu, new UTF8Encoding(true));
            // Un rapport par ouverture de session : on ne garde sur le PC que les 60 derniers jours (le NAS a tout).
            foreach (var ancien in new DirectoryInfo(Dossier).GetFiles("*.html").Where(f => f.LastWriteTime < DateTime.Now.AddDays(-60)))
                ancien.Delete();
            return chemin;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Journal($"Impossible d'enregistrer {nom} : {ex.Message}");
            return null;
        }
    }

    /// <summary>Journal clair, une ligne datée par étape. Un seul ancien journal est gardé (entretien.log.old).</summary>
    public static void Journal(string message)
    {
        try
        {
            Directory.CreateDirectory(Dossier);
            var log = Path.Combine(Dossier, "entretien.log");
            if (File.Exists(log) && new FileInfo(log).Length > JournalMax)
            {
                File.Copy(log, log + ".old", overwrite: true);
                File.Delete(log);
            }
            File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}", new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
