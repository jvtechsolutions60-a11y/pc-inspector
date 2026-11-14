using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace JvtechSonde;

/// <summary>
/// Envoi à un moniteur « Push » d'Uptime Kuma : un simple appel HTTPS à l'adresse du moniteur, avec l'état et un
/// court message. Aucun mot de passe sur le PC : l'adresse ne permet que de signaler ce PC. Si la sonde ne
/// donne plus de nouvelles (PC éteint, sonde supprimée), c'est Kuma qui le voit et prévient.
/// </summary>
internal static class Kuma
{
    public const int MaxMessage = 400;

    /// <summary>« down » si un point est rouge (ou orange quand alerte_orange = oui), sinon « up ».</summary>
    public static string Status(IReadOnlyCollection<Point> points, bool alertOnWarning) =>
        points.Any(p => p.Level == Level.Critical || (alertOnWarning && p.Level == Level.Warning)) ? "down" : "up";

    /// <summary>Message lisible dans Kuma : les points rouges, puis orange ; sinon « OK, N points vérifiés ».</summary>
    public static string Message(IReadOnlyCollection<Point> points)
    {
        var red = points.Where(p => p.Level == Level.Critical).Select(p => $"{p.Label} : {p.Value}").ToList();
        var orange = points.Where(p => p.Level == Level.Warning).Select(p => $"{p.Label} : {p.Value}").ToList();
        if (red.Count == 0 && orange.Count == 0)
            return $"OK, {points.Count(p => p.Level == Level.Ok)} points vérifiés";
        var parts = new List<string>();
        if (red.Count > 0) parts.Add("ROUGE " + string.Join(" | ", red));
        if (orange.Count > 0) parts.Add("ORANGE " + string.Join(" | ", orange));
        var text = string.Join(" ; ", parts);
        return text.Length <= MaxMessage ? text : text.Substring(0, MaxMessage - 1) + "…";
    }

    /// <summary>
    /// Adresse d'envoi : l'adresse du moniteur, sans ses paramètres d'exemple (?status=up&amp;msg=OK&amp;ping=),
    /// suivie de l'état et du message. Null si l'adresse n'est pas une adresse web valide.
    /// </summary>
    public static string? BuildUrl(string pushUrl, string status, string message)
    {
        if (string.IsNullOrWhiteSpace(pushUrl)) return null;
        var bare = pushUrl.Trim();
        var query = bare.IndexOf('?');
        if (query >= 0) bare = bare.Substring(0, query);
        if (!Uri.TryCreate(bare, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return null;
        return $"{bare}?status={status}&msg={Uri.EscapeDataString(message)}&ping=";
    }

    /// <summary>Envoie le résultat. Renvoie null si tout va bien, sinon la raison de l'échec (écrite dans le journal).</summary>
    public static string? Send(string pushUrl, IReadOnlyCollection<Point> points, bool alertOnWarning)
    {
        var url = BuildUrl(pushUrl, Status(points, alertOnWarning), Message(points));
        if (url is null) return "adresse kuma_push invalide dans sonde.ini";

        // .NET Framework 4.8 sur un vieux Windows 10 ne propose pas toujours TLS 1.2 de lui-même.
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 15000;
                request.UserAgent = "JVTECH-Sonde/" + PcInspector.AppInfo.Version;
                using var response = (HttpWebResponse)request.GetResponse();
                if ((int)response.StatusCode is >= 200 and < 300) return null;
            }
            catch (WebException ex) when (attempt < 3)
            {
                _ = ex;
                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(10 * attempt));
            }
            catch (WebException ex)
            {
                return ex.Response is HttpWebResponse r ? $"Kuma a répondu {(int)r.StatusCode} {r.StatusDescription}" : ex.Message;
            }
        }
        return "Kuma ne répond pas";
    }

    /// <summary>L'adresse sans le jeton du moniteur, pour le journal.</summary>
    public static string Masked(string pushUrl)
    {
        var bare = pushUrl.Split('?')[0];
        var slash = bare.LastIndexOf('/');
        return slash > 0 ? bare.Substring(0, slash + 1) + "…" : "…";
    }
}
