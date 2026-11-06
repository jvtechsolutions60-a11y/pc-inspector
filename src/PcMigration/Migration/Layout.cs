using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PcInspector;

namespace PcMigration.Migration;

/// <summary>Ce qui peut être transféré, dans l'ordre d'affichage.</summary>
internal static class Items
{
    public const string Passwords = "Passwords";
    public const string Apps = "Apps";
    public const string WiFi = "WiFi";
    public const string Browsers = "Browsers";
    public const string Outlook = "Outlook";
    public const string Wallpaper = "Wallpaper";
    public const string Printers = "Printers";
    public const string FolderPrefix = "Folder:";
}

/// <summary>Organisation du dossier de sauvegarde sur le disque externe, et son fichier de description.</summary>
internal sealed class Layout
{
    public const string RootName = "JVTECH Migration";
    public const string ManifestName = "migration.ini";

    public Layout(string folder) => Folder = folder;

    public string Folder { get; }
    public string Folders => Path.Combine(Folder, "Dossiers");
    public string Browsers => Path.Combine(Folder, "Navigateurs");
    public string Passwords => Path.Combine(Folder, "Mots de passe");
    public string Apps => Path.Combine(Folder, "Applications");
    public string WingetFile => Path.Combine(Apps, "winget.json");
    public string WiFi => Path.Combine(Folder, "Wi-Fi");
    public string Outlook => Path.Combine(Folder, "Outlook");
    public string Wallpaper => Path.Combine(Folder, "Fond d'écran");
    public string PrintersFile => Path.Combine(Folder, "Imprimantes.txt");
    public string Manifest => Path.Combine(Folder, ManifestName);

    /// <summary>X:\JVTECH Migration\Client - PC - 2026-11-03 14h05</summary>
    public static Layout Create(string root, string client, string computer)
    {
        var name = Tools.SafeName($"{(string.IsNullOrWhiteSpace(client) ? "Client" : client.Trim())} - {computer} - {DateTime.Now:yyyy-MM-dd HH}h{DateTime.Now:mm}");
        var folder = Path.Combine(root, RootName, name);
        Directory.CreateDirectory(folder);
        return new Layout(folder);
    }

    public void WriteManifest(IDictionary<string, string> values)
    {
        var text = new StringBuilder("; Sauvegarde PC Migration (JVTECH) : à restaurer avec PcMigration.exe sur le nouveau PC.\r\n");
        foreach (var pair in values) text.Append(pair.Key).Append('=').Append(pair.Value.Replace("\r", " ").Replace("\n", " ")).Append("\r\n");
        File.WriteAllText(Manifest, text.ToString(), Encoding.UTF8);
    }

    public Dictionary<string, string> ReadManifest()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var line in File.ReadAllLines(Manifest))
            {
                if (line.StartsWith(";")) continue;
                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2) values[parts[0].Trim()] = parts[1].Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return values;
    }

    /// <summary>Les sauvegardes trouvées sur les disques branchés (la plus récente d'abord).</summary>
    public static List<Layout> Find()
    {
        var found = new List<Layout>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                var root = Path.Combine(drive.RootDirectory.FullName, RootName);
                if (!Directory.Exists(root)) continue;
                found.AddRange(Directory.GetDirectories(root).Where(d => File.Exists(Path.Combine(d, ManifestName))).Select(d => new Layout(d)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        // Sur la clé JVTECH en mode clé USB, ou dans le dossier des rapports.
        try
        {
            var reports = Path.Combine(AppPaths.ReportsRoot, RootName);
            if (Directory.Exists(reports))
                found.AddRange(Directory.GetDirectories(reports).Where(d => File.Exists(Path.Combine(d, ManifestName))).Select(d => new Layout(d)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return found.GroupBy(l => l.Folder, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                    .OrderByDescending(l => Directory.GetLastWriteTime(l.Folder)).ToList();
    }

    public override string ToString() => Folder;
}
