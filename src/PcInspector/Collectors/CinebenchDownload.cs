using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace PcInspector.Collectors;

/// <summary>
/// Téléchargement de Cinebench chez Maxon, seulement quand le technicien l'accepte (test gaming). JVTECH ne
/// redistribue rien : le fichier vient directement de maxon.net. Comme pour AdwCleaner, il n'est gardé que s'il est
/// complet et signé par Maxon. Maxon ne publie pas de lien fixe : il est cherché dans ses pages de téléchargement ;
/// si Maxon change ses pages, le technicien est renvoyé vers la page à la main.
/// </summary>
internal static class CinebenchDownload
{
    /// <summary>Page ouverte à la main si le téléchargement automatique échoue.</summary>
    public const string DownloadsPage = "https://www.maxon.net/en/downloads";

    private static readonly string[] Pages =
    {
        "https://www.maxon.net/en/downloads/cinebench-2026-downloads",
        DownloadsPage,
        "https://www.maxon.net/en/cinebench",
    };

    private const long MinFreeBytes = 4L * 1024 * 1024 * 1024;   // archive + dossier dézippé, avec de la marge
    private const long MaxZipBytes = 3L * 1024 * 1024 * 1024;

    private static readonly Regex ZipLink = new(
        @"https://[A-Za-z0-9.-]+\.maxon\.net/[^""'\s<>\\]*?cinebench[^""'\s<>\\]*?\.zip",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Lien de l'archive Windows de Cinebench dans une page de Maxon (HTML, ou JSON avec « \/ »). Seulement en https
    /// et sur un domaine maxon.net. Null si absent.
    /// </summary>
    internal static string? FindZipUrl(string page)
    {
        var text = page.Replace(@"\/", "/").Replace(@"/", "/");
        var links = ZipLink.Matches(text).Cast<Match>().Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(IsMaxonHttps)
            .Where(u => !Regex.IsMatch(u, "mac|osx|linux|arm64", RegexOptions.IgnoreCase))
            .ToList();
        return links.FirstOrDefault(u => Regex.IsMatch(u, "win", RegexOptions.IgnoreCase)) ?? (links.Count == 1 ? links[0] : null);
    }

    internal static bool IsMaxonHttps(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("maxon.net", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".maxon.net", StringComparison.OrdinalIgnoreCase));

    /// <summary>Télécharge, vérifie et range Cinebench dans Outils\Cinebench. Renvoie Cinebench.exe, ou la raison de l'échec.</summary>
    public static (string? Exe, string? Error) Run(string toolsFolder, Action<string> progress)
    {
        var zip = Path.Combine(toolsFolder, "Cinebench-maxon.zip");
        var part = zip + ".part";
        var staging = Path.Combine(toolsFolder, "Cinebench.telechargement");
        var target = Path.Combine(toolsFolder, "Cinebench");
        try
        {
            Directory.CreateDirectory(toolsFolder);
            var root = Path.GetPathRoot(Path.GetFullPath(toolsFolder));
            if (!string.IsNullOrEmpty(root) && new DriveInfo(root).AvailableFreeSpace < MinFreeBytes)
                return (null, $"pas assez de place sur {root} (4 Go libres nécessaires)");

            progress("Cinebench : recherche du lien de téléchargement chez Maxon…");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string? url = null;
            foreach (var page in Pages)
            {
                try
                {
                    using var client = Client();
                    url = FindZipUrl(client.DownloadString(page));
                }
                catch (WebException) { }
                if (url is not null) break;
            }
            if (url is null) return (null, "lien de téléchargement introuvable sur le site de Maxon");

            progress("Cinebench : téléchargement chez Maxon (environ 1 Go, une seule fois)…");
            Delete(part);
            using (var client = Client()) client.DownloadFile(url, part);
            var size = new FileInfo(part).Length;
            if (size < 10 * 1024 * 1024 || size > MaxZipBytes) return (null, "fichier reçu de taille inattendue");
            Delete(zip);
            File.Move(part, zip);

            progress("Cinebench : décompression et vérification de la signature Maxon…");
            DeleteFolder(staging);
            ExtractSafely(zip, staging);
            var exe = Directory.EnumerateFiles(staging, "Cinebench.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe is null) return (null, "Cinebench.exe absent de l'archive de Maxon");
            if (!Authenticode.IsSignedBy(exe, "Maxon")) return (null, "Cinebench.exe n'a pas une signature Maxon valide : supprimé, pas lancé");

            DeleteFolder(target);
            Directory.Move(staging, target);
            return (Path.Combine(target, exe.Substring(staging.Length).TrimStart('\\')), null);
        }
        catch (Exception ex) when (ex is WebException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            return (null, ex.Message);
        }
        finally
        {
            Delete(part);
            Delete(zip);
            DeleteFolder(staging);
        }
    }

    /// <summary>Dézippe sans jamais écrire en dehors du dossier cible (noms « ..\ » refusés).</summary>
    internal static void ExtractSafely(string zip, string folder)
    {
        var root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        using var archive = ZipFile.OpenRead(zip);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("archive refusée : un fichier sortirait du dossier Outils");
            if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }

    private static WebClient Client()
    {
        var client = new WebClient();
        // Le site de Maxon refuse parfois les clients sans navigateur déclaré.
        client.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) JVTECH-PC-Inspector";
        return client;
    }

    private static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void DeleteFolder(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
