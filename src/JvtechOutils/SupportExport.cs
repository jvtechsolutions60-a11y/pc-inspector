using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using PcInspector;

namespace JvtechOutils;

/// <summary>Export des journaux pour le support : un seul .zip à envoyer, sans rien modifier sur le PC.</summary>
internal static class SupportExport
{
    /// <summary>
    /// Réunit les journaux et rapports d'erreur des <paramref name="days"/> derniers jours dans un .zip à envoyer
    /// pour le support. Les fichiers sont déjà masqués à l'écriture. Renvoie le chemin du .zip, ou null si rien.
    /// </summary>
    public static string? ExportForSupport(string destinationFolder, int days = 14)
    {
        var files = new[] { "Journaux", "Plantages" }
            .Select(n => Path.Combine(AppPaths.ReportsRoot, n))
            .Where(Directory.Exists)
            .SelectMany(f => Directory.GetFiles(f, "*.txt"))
            .Where(f => DateTime.Now - File.GetLastWriteTime(f) <= TimeSpan.FromDays(days))
            .ToList();
        if (files.Count == 0) return null;
        Directory.CreateDirectory(destinationFolder);
        var zip = Path.Combine(destinationFolder, $"JVTECH journaux {Environment.MachineName} {DateTime.Now:yyyy-MM-dd HH-mm}.zip");
        if (File.Exists(zip)) File.Delete(zip);
        using var archive = new ZipArchive(new FileStream(zip, FileMode.CreateNew), ZipArchiveMode.Create);
        foreach (var file in files)
        {
            var entry = archive.CreateEntry(Path.GetFileName(Path.GetDirectoryName(file)) + "/" + Path.GetFileName(file));
            using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = entry.Open();
            source.CopyTo(target);
        }
        return zip;
    }
}
