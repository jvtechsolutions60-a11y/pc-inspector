using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace JvtechServeur.Pxe;

/// <summary>
/// « WSUS maison » : cherche sur le catalogue Microsoft Update la dernière mise à jour cumulative de Windows 11 et
/// l'intègre aux fichiers d'installation avec DISM. Les PC installés ensuite sont déjà à jour du mois : il ne reste à
/// Windows Update que les pilotes et les petites mises à jour.
/// </summary>
internal static class ImageUpdater
{
    private const string Catalog = "https://www.catalog.update.microsoft.com/";

    internal sealed record Update(string Id, string Title, string Kb, string Month);

    private static string InstallWim => Path.Combine(ServerConfig.InstallFolder, @"sources\install.wim");

    /// <summary>Numéro de build de l'image (22631, 26100, 26200…), lu par DISM.</summary>
    public static int? ImageBuild(Action<string> log)
    {
        var (_, lines) = Tools.PowerShell($"(Get-WindowsImage -ImagePath {Tools.Quote(InstallWim)} -Index 1).Version", log, TimeSpan.FromMinutes(2));
        var version = lines.Select(l => Regex.Match(l, @"10\.0\.(\d{5})")).FirstOrDefault(m => m.Success);
        return version is null ? null : int.Parse(version.Groups[1].Value);
    }

    private static string? Release(int build) => build switch
    {
        22631 => "23H2",
        26100 => "24H2",
        26200 => "25H2",
        _ => null,
    };

    /// <summary>Dernière mise à jour cumulative publiée pour la version de l'image, ou null (rien trouvé ou pas d'Internet).</summary>
    public static Update? Latest(Action<string> log)
    {
        var build = ImageBuild(log);
        if (build is null) { log("Version de l'image introuvable : préparez d'abord le serveur avec une ISO."); return null; }
        var release = Release(build.Value);
        if (release is null) { log($"Build {build} non pris en charge pour la mise à jour automatique."); return null; }

        string html;
        try
        {
            using var web = Client();
            html = web.DownloadString(Catalog + "Search.aspx?q=" + Uri.EscapeDataString($"Cumulative Update for Windows 11 Version {release} for x64-based Systems"));
        }
        catch (WebException ex)
        {
            log("Catalogue Microsoft Update injoignable : " + ex.Message);
            return null;
        }

        var candidates = Regex.Matches(html, @"id=""([0-9a-fA-F\-]{36})_link""[^>]*>\s*([^<]+?)\s*</a>")
            .Cast<Match>()
            .Select(m => (Id: m.Groups[1].Value, Title: WebUtility.HtmlDecode(m.Groups[2].Value)))
            .Where(u => u.Title.StartsWith("20") && u.Title.Contains("Cumulative Update for Windows 11 Version " + release)
                        && u.Title.Contains("x64-based") && !Regex.IsMatch(u.Title, "Preview|\\.NET|Dynamic|Hotpatch|Server", RegexOptions.IgnoreCase))
            .Select(u => new Update(u.Id, u.Title, Regex.Match(u.Title, @"KB\d+").Value, u.Title.Substring(0, 7)))
            .Where(u => u.Kb.Length > 0)
            .OrderByDescending(u => u.Month).ThenByDescending(u => int.Parse(u.Kb.Substring(2)))
            .ToList();
        if (candidates.Count == 0) log("Aucune mise à jour cumulative trouvée sur le catalogue (la page a peut-être changé).");
        return candidates.FirstOrDefault();
    }

    /// <summary>Télécharge la mise à jour et l'intègre à chaque édition de install.wim. Renvoie null si tout va bien.</summary>
    public static string? Apply(Update update, ServerConfig config, Action<string> log, Action<string> status)
    {
        var folder = Path.Combine(ServerConfig.WorkFolder, "MiseAJour");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);

        status($"Téléchargement de {update.Kb}…");
        var urls = DownloadUrls(update.Id, log);
        if (urls.Count == 0) return "Liens de téléchargement introuvables sur le catalogue.";
        try
        {
            using var web = Client();
            foreach (var url in urls)
            {
                var name = Path.GetFileName(new Uri(url).LocalPath);
                log("Téléchargement de " + name);
                web.DownloadFile(url, Path.Combine(folder, name));
            }
        }
        catch (WebException ex)
        {
            return "Téléchargement interrompu : " + ex.Message;
        }

        var dism = Tools.System32("dism.exe");
        var mount = Path.Combine(ServerConfig.WorkFolder, "montage-image");
        Tools.Run(dism, $"/Unmount-Image /MountDir:\"{mount}\" /Discard", null, TimeSpan.FromMinutes(20));
        Tools.Run(dism, "/Cleanup-Wim", null, TimeSpan.FromMinutes(10));
        Directory.CreateDirectory(mount);

        var (_, info) = Tools.PowerShell($"(Get-WindowsImage -ImagePath {Tools.Quote(InstallWim)}).ImageIndex", null, TimeSpan.FromMinutes(2));
        var indexes = info.Select(l => int.TryParse(l, out var i) ? i : 0).Where(i => i > 0).ToList();
        if (indexes.Count == 0) return "Impossible de lire les éditions de install.wim.";

        foreach (var index in indexes)
        {
            status($"Intégration de {update.Kb} : édition {index} sur {indexes.Count} (20 à 40 minutes chacune)…");
            if (Tools.Run(dism, $"/Mount-Image /ImageFile:\"{InstallWim}\" /Index:{index} /MountDir:\"{mount}\"", log, TimeSpan.FromHours(1)).Code != 0)
                return $"Impossible d'ouvrir l'édition {index} de install.wim.";
            // Depuis 24H2, la mise à jour arrive en plusieurs fichiers (« checkpoint ») : DISM prend le dossier entier.
            var (added, _) = Tools.Run(dism, $"/Image:\"{mount}\" /Add-Package /PackagePath:\"{folder}\"", log, TimeSpan.FromHours(3));
            var ok = added is 0 or 3010;
            if (ok) Tools.Run(dism, $"/Image:\"{mount}\" /Cleanup-Image /StartComponentCleanup", log, TimeSpan.FromHours(2));
            if (Tools.Run(dism, $"/Unmount-Image /MountDir:\"{mount}\" {(ok ? "/Commit" : "/Discard")}", log, TimeSpan.FromHours(1)).Code != 0)
                return $"Impossible d'enregistrer l'édition {index}.";
            if (!ok) return $"DISM n'a pas pu intégrer {update.Kb} (code {added}). L'image reste comme avant.";
        }

        // Réécrit le fichier pour rendre la place des anciennes versions.
        status("Compactage de l'image…");
        var compact = Path.Combine(ServerConfig.WorkFolder, "install-compact.wim");
        if (File.Exists(compact)) File.Delete(compact);
        var exported = indexes.All(i => Tools.Run(dism, $"/Export-Image /SourceImageFile:\"{InstallWim}\" /SourceIndex:{i} /DestinationImageFile:\"{compact}\" /Compress:max", log, TimeSpan.FromHours(1)).Code == 0);
        if (exported) File.Copy(compact, InstallWim, true);
        if (File.Exists(compact)) File.Delete(compact);
        Directory.Delete(folder, true);

        config.LastUpdate = update.Kb;
        config.LastUpdateDate = DateTime.Today;
        config.Save();
        log($"{update.Title} intégrée à l'image.");
        return null;
    }

    private static List<string> DownloadUrls(string id, Action<string> log)
    {
        try
        {
            using var web = Client();
            web.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
            var body = "updateIDs=" + Uri.EscapeDataString($"[{{\"size\":0,\"languages\":\"\",\"uidInfo\":\"{id}\",\"updateID\":\"{id}\"}}]");
            var html = web.UploadString(Catalog + "DownloadDialog.aspx", body);
            return Regex.Matches(html, @"\.url\s*=\s*'([^']+)'").Cast<Match>().Select(m => m.Groups[1].Value)
                .Where(u => u.EndsWith(".msu", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        }
        catch (WebException ex)
        {
            log("Catalogue Microsoft Update : " + ex.Message);
            return new List<string>();
        }
    }

    private static WebClient Client()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var web = new WebClient { Encoding = Encoding.UTF8 };
        web.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) JVTECH-Serveur";
        return web;
    }
}
