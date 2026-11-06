using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using PcInspector;

namespace PcMaintenance.Preparation;

/// <summary>
/// Avancement envoyé au serveur PXE de l'atelier (adresse lue dans serveur.ini, écrit par JVTECH Serveur PXE),
/// pour suivre tous les PC depuis son écran. Sans serveur.ini, rien n'est envoyé.
/// </summary>
internal sealed class ServerLink
{
    private readonly string? _url;
    private readonly object _lock = new();
    /// <summary>Messages envoyés l'un après l'autre, sans bloquer la fenêtre, dans l'ordre.</summary>
    private Task _queue = Task.CompletedTask;

    private ServerLink(string? url) => _url = url;

    public bool Enabled => _url is not null;

    public static ServerLink Load()
    {
        try
        {
            var path = Path.Combine(AppPaths.ExeFolder, "serveur.ini");
            var url = File.Exists(path)
                ? File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.StartsWith("Url=", StringComparison.OrdinalIgnoreCase)).Select(l => l.Substring(4).Trim()).FirstOrDefault()
                : null;
            return new ServerLink(string.IsNullOrEmpty(url) ? null : url!.TrimEnd('/') + "/");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ServerLink(null);
        }
    }

    public void Status(string model, string serial, DateTime started, string step, string detail, bool finished = false, bool toCheck = false)
    {
        if (_url is null) return;
        var lines = new List<string>
        {
            "Macs=" + string.Join(",", Macs()),
            "Serie=" + serial,
            "Modele=" + model,
            "Debut=" + started.ToString("o"),
            "Etape=" + OneLine(step),
            "Detail=" + OneLine(detail),
        };
        if (finished) lines.AddRange(new[] { "Fini=1", "AVerifier=" + (toCheck ? "1" : "0") });
        var body = string.Join("\n", lines);
        lock (_lock) _queue = _queue.ContinueWith(_ => Post("etat", body), TaskScheduler.Default);
    }

    /// <summary>Envoie le rapport HTML au serveur. Renvoie vrai s'il l'a bien reçu.</summary>
    public bool Report(string serial, string path)
    {
        if (_url is null) return false;
        try
        {
            return Post($"rapport?serie={Uri.EscapeDataString(serial)}&nom={Uri.EscapeDataString(Path.GetFileName(path))}", File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool Post(string relative, string body)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            var request = (HttpWebRequest)WebRequest.Create(_url + relative);
            request.Method = "POST";
            request.ContentType = "text/plain; charset=utf-8";
            request.Timeout = 15000;
            request.Proxy = null;
            using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            using var response = (HttpWebResponse)request.GetResponse();
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex) when (ex is WebException or IOException or UriFormatException)
        {
            // Serveur éteint ou injoignable : la préparation continue sans lui.
            return false;
        }
    }

    private static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>Adresses MAC des cartes réseau physiques, comme le serveur les voit au démarrage réseau.</summary>
    private static IEnumerable<string> Macs()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet)
                .Select(n => n.GetPhysicalAddress().GetAddressBytes())
                .Where(b => b.Length == 6)
                .Select(b => string.Join(":", b.Select(x => x.ToString("X2"))))
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return Enumerable.Empty<string>();
        }
    }
}
