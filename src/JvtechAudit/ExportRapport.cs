using System;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace JvtechAudit;

/// <summary>
/// SEULE écriture de JVTECH Audit (cahier § 4.5) : le rapport, à l'endroit choisi par le technicien dans la fenêtre
/// « Enregistrer sous » (clé USB proposée en premier, c'est-à-dire le dossier du .exe). Rien d'autre n'est écrit.
/// tests/JvtechTests/SeparationTests vérifie que ce fichier est le seul à écrire, et qu'il n'écrit qu'un fichier choisi.
/// </summary>
internal static class ExportRapport
{
    public static string? Enregistrer(Window proprietaire, string html, string nomPropose)
    {
        var dialogue = new SaveFileDialog
        {
            Title = "Enregistrer le rapport d'audit",
            Filter = "Page web (*.html)|*.html",
            FileName = Nettoyer(nomPropose),
            InitialDirectory = AppDomain.CurrentDomain.BaseDirectory,
            OverwritePrompt = true,
            AddExtension = true,
            DefaultExt = ".html",
        };
        if (dialogue.ShowDialog(proprietaire) != true) return null;
        try
        {
            File.WriteAllText(dialogue.FileName, html, new UTF8Encoding(true));
            return dialogue.FileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            MessageBox.Show(proprietaire, "Rapport non enregistré : " + ex.Message, "JVTECH Audit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private static string Nettoyer(string nom)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) nom = nom.Replace(c, '-');
        return nom;
    }
}
