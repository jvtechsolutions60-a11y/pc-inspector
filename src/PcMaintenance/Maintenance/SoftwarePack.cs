using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PcInspector;

namespace PcMaintenance.Maintenance;

/// <summary>Un logiciel du pack « nouveau PC », installé avec winget.</summary>
internal sealed record PackItem(string Name, string Id, bool Checked);

/// <summary>La sélection de logiciels de base, modifiable dans pack.ini (une ligne « Id winget | Nom | 1 ou 0 »).</summary>
internal static class SoftwarePack
{
    private static readonly PackItem[] Defaults =
    {
        new("Google Chrome", "Google.Chrome", true),
        new("Mozilla Firefox", "Mozilla.Firefox", false),
        new("7-Zip", "7zip.7zip", true),
        new("VLC", "VideoLAN.VLC", true),
        new("Adobe Acrobat Reader", "Adobe.Acrobat.Reader.64-bit", true),
        new("LibreOffice", "TheDocumentFoundation.LibreOffice", false),
        new("Notepad++", "Notepad++.Notepad++", false),
        new("Malwarebytes", "Malwarebytes.Malwarebytes", false),
    };

    public static string FilePath => Path.Combine(AppPaths.SettingsFolder, "pack.ini");

    public static IReadOnlyList<PackItem> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return Defaults;
            var items = File.ReadAllLines(FilePath)
                .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#"))
                .Select(l => l.Split('|').Select(p => p.Trim()).ToArray())
                .Where(p => p.Length >= 2 && p[0].Length > 0)
                .Select(p => new PackItem(p[1], p[0], p.Length < 3 || p[2] != "0"))
                .ToList();
            return items.Count > 0 ? items : Defaults;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Defaults;
        }
    }

    /// <summary>Crée pack.ini avec la liste actuelle s'il n'existe pas, pour l'ouvrir dans le Bloc-notes.</summary>
    public static string EnsureFile()
    {
        if (File.Exists(FilePath)) return FilePath;
        AppPaths.EnsureFolder(AppPaths.SettingsFolder);
        var lines = new List<string>
        {
            "# Une ligne par logiciel : identifiant winget | nom affiché | 1 = coché par défaut, 0 = décoché.",
            "# Trouver un identifiant : « winget search nom » dans un terminal, ou sur winget.run.",
        };
        lines.AddRange(Defaults.Select(i => $"{i.Id} | {i.Name} | {(i.Checked ? 1 : 0)}"));
        File.WriteAllLines(FilePath, lines);
        return FilePath;
    }
}
