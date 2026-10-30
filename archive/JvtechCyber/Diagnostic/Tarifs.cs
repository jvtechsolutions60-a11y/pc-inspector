using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PcInspector;

namespace JvtechCyber.Diagnostic;

/// <summary>
/// Prix des prestations JVTECH affichés dans le plan d'action (tarifs-cyber.ini dans les paramètres) :
/// une ligne « theme.niveau = texte », par exemple « sauvegardes.1 = 390 € TTC, NAS 2 To installé ».
/// </summary>
public static class Tarifs
{
    public static string FilePath => Path.Combine(AppPaths.SettingsFolder, "tarifs-cyber.ini");

    public static Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Dictionary<string, string>();
            return File.ReadAllLines(FilePath, Encoding.UTF8)
                .Where(l => !l.TrimStart().StartsWith("#"))
                .Select(l => l.Split(new[] { '=' }, 2))
                .Where(p => p.Length == 2 && p[1].Trim().Length > 0)
                .GroupBy(p => p[0].Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First()[1].Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>Crée le fichier avec une ligne vide par étape faisable par JVTECH, à compléter dans le Bloc-notes.</summary>
    public static string EnsureFile()
    {
        if (File.Exists(FilePath)) return FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var lines = new List<string>
        {
            "# Tarifs JVTECH affichés dans le plan d'action du diagnostic cyber.",
            "# Une ligne par étape : theme.niveau = prix et précision. Laisser vide pour « sur devis ».",
            "",
        };
        foreach (var theme in Referentiel.Themes)
        {
            lines.Add("# " + theme.Name);
            lines.AddRange(theme.Levels.Where(l => l.Jvtech).Select(l => $"{theme.Id}.{l.Value} = "));
            lines.Add("");
        }
        File.WriteAllLines(FilePath, lines, new UTF8Encoding(true));
        return FilePath;
    }
}
