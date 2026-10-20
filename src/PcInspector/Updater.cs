using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PcInspector;

/// <summary>Version publiée sur GitHub plus récente que celle lancée.</summary>
public sealed record UpdateInfo(string Tag, string Url);

/// <summary>Regarde sur GitHub si une nouvelle version des logiciels est sortie. Ne télécharge et n'installe rien.</summary>
public static class Updater
{
    public const string ReleasesPage = "https://github.com/jvtechsolutions60-a11y/pc-inspector/releases/latest";
    private const string LatestApi = "https://api.github.com/repos/jvtechsolutions60-a11y/pc-inspector/releases/latest";

    /// <summary>La nouvelle version, ou null si à jour, hors ligne, ou compilé à la main (v1.0.0).</summary>
    public static Task<UpdateInfo?> CheckAsync() => Task.Run<UpdateInfo?>(() =>
    {
        if (AppInfo.Build == 0) return null;
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using var client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "JVTECH-Outils";
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            var json = client.DownloadString(LatestApi);
            var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?1\\.0\\.(\\d+)\"");
            if (!tag.Success || !int.TryParse(tag.Groups[1].Value, out var latest) || latest <= AppInfo.Build) return null;
            var url = Regex.Match(json, "\"html_url\"\\s*:\\s*\"(https://github\\.com/[^\"]+/releases/tag/[^\"]+)\"");
            return new UpdateInfo("v1.0." + latest, url.Success ? url.Groups[1].Value : ReleasesPage);
        }
        catch (Exception ex) when (ex is WebException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    });
}
