using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Débit de téléchargement et temps de réponse, pour montrer au client si la ligne est en cause (comme DownTester).</summary>
public sealed class SpeedTestCollector : ICollector
{
    private const int PingCount = 4;
    private static readonly TimeSpan DownloadDuration = TimeSpan.FromSeconds(8);

    // Fichiers de test publics prévus pour ça. Le premier qui répond sert de mesure.
    private static readonly (string Name, string Url)[] TestFiles =
    {
        ("Cloudflare", "https://speed.cloudflare.com/__down?bytes=200000000"),
        ("OVH (France)", "https://proof.ovh.net/files/100Mb.dat"),
    };

    public string Title => "Débit internet";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

        var connection = ActiveInterface();
        var link = new InfoGroup("Connexion utilisée");
        if (connection is null)
        {
            link.Add("Résultat", "Aucune connexion réseau active");
            section.Groups.Add(link);
            return section;
        }
        link.Add("Carte", connection.Description)
            .Add("Type", connection.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Câble ou autre")
            .Add("Vitesse de liaison", $"{connection.Speed / 1_000_000} Mbit/s");
        section.Groups.Add(link);

        var download = new InfoGroup("Téléchargement");
        double? mbps = null;
        foreach (var (name, url) in TestFiles)
        {
            mbps = Download(url);
            if (mbps is null) continue;
            download.Add("Débit mesuré", $"{mbps:0.0} Mbit/s (serveur {name})")
                    .Add("Verdict", Verdict(mbps.Value));
            break;
        }
        if (mbps is null) download.Add("Résultat", "Test impossible : pas d'accès à internet ou serveurs de test bloqués");
        section.Groups.Add(download);

        var gateway = connection.GetIPProperties().GatewayAddresses
            .Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
        var latency = new InfoGroup("Temps de réponse (ping)");
        if (gateway is not null) latency.Add($"Box ou routeur ({gateway})", PingSummary(gateway.ToString()));
        latency.Add("Internet (1.1.1.1)", PingSummary("1.1.1.1"))
               .Add("Internet (8.8.8.8)", PingSummary("8.8.8.8"))
               .Add("Résolution des noms (DNS)", DnsTime());
        section.Groups.Add(latency);

        section.Groups.Add(Advice(connection, mbps));
        return section;
    }

    private static NetworkInterface? ActiveInterface() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel
                        && n.GetIPProperties().GatewayAddresses.Any())
            .OrderByDescending(n => n.Speed)
            .FirstOrDefault();

    /// <summary>Télécharge pendant quelques secondes sans rien enregistrer et renvoie le débit en Mbit/s.</summary>
    private static double? Download(string url)
    {
        try
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "PC Inspector";
            request.Timeout = 10_000;
            request.ReadWriteTimeout = 10_000;
            using var response = request.GetResponse();
            using var stream = response.GetResponseStream();
            if (stream is null) return null;

            var buffer = new byte[81920];
            long total = 0;
            var watch = Stopwatch.StartNew();
            int read;
            while (watch.Elapsed < DownloadDuration && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
                total += read;
            watch.Stop();
            request.Abort();

            // En dessous de 1 Mo, la mesure n'est pas fiable.
            if (total < 1_000_000 || watch.Elapsed.TotalSeconds <= 0) return null;
            return total * 8 / watch.Elapsed.TotalSeconds / 1_000_000;
        }
        catch (Exception ex) when (ex is WebException or System.IO.IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static string PingSummary(string host)
    {
        using var ping = new Ping();
        var times = new System.Collections.Generic.List<long>();
        for (var i = 0; i < PingCount; i++)
        {
            try
            {
                var reply = ping.Send(host, 2000);
                if (reply?.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
            }
            catch (PingException) { }
        }

        if (times.Count == 0) return "Pas de réponse";
        var lost = PingCount - times.Count;
        var text = $"{times.Average():0} ms en moyenne (max {times.Max()} ms)";
        return lost > 0 ? $"{text} · {lost} perte(s) sur {PingCount}" : text;
    }

    private static string DnsTime()
    {
        try
        {
            var watch = Stopwatch.StartNew();
            Dns.GetHostAddresses("www.microsoft.com");
            return $"{watch.ElapsedMilliseconds} ms";
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
        {
            return "Échec : les sites ne peuvent pas être trouvés";
        }
    }

    private static string Verdict(double mbps) => mbps switch
    {
        < 5 => "Très lent : vidéo et visio difficiles",
        < 20 => "Lent : suffisant pour le web et les mails",
        < 100 => "Correct : vidéo HD et télétravail sans souci",
        < 500 => "Rapide",
        _ => "Très rapide (fibre)",
    };

    private static InfoGroup Advice(NetworkInterface connection, double? mbps)
    {
        var group = new InfoGroup("Conseils");
        var wifi = connection.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
        var linkMbps = connection.Speed / 1_000_000.0;

        if (wifi && mbps is { } m && m < linkMbps * 0.3)
            group.Add("Wi-Fi", "Le débit est bien en dessous de la liaison Wi-Fi : refaire le test en câble près de la box pour savoir si c'est la ligne ou le Wi-Fi.");
        if (wifi && linkMbps < 100)
            group.Add("Signal Wi-Fi", "Liaison Wi-Fi faible : rapprocher le PC de la box, ou prévoir un répéteur ou du CPL.");
        if (!wifi && linkMbps > 0 && linkMbps <= 100)
            group.Add("Câble", "Liaison à 100 Mbit/s maximum : câble ou prise abîmés, ou port de la box limité. Un câble Cat 5e ou 6 suffit pour 1 Gbit/s.");
        if (mbps is { } slow && slow < 20)
            group.Add("Ligne", "Débit faible : comparer avec l'offre du fournisseur et faire un test sur un autre appareil avant d'accuser le PC.");
        if (group.Items.Count == 0) group.Add("Résultat", "Rien d'anormal");
        group.Add("À savoir", "Le test télécharge jusqu'à 200 Mo pendant 8 secondes. Éviter sur une connexion 4G limitée.");
        return group;
    }
}
