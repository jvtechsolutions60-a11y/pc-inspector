using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace JvtechServeur.Pxe;

/// <summary>
/// Petit serveur web local qui reçoit l'avancement envoyé par PC Maintenance sur chaque PC en préparation
/// (http://serveur:8081/jvtech/etat), et son rapport final (…/jvtech/rapport).
/// Protections (règles dans <see cref="StatusPolicy"/>) : jeton secret partagé avec le kit d'installation,
/// réseau local seulement, POST seulement, taille lue plafonnée (même sans Content-Length), champs filtrés,
/// débit limité par adresse. Le jeton n'est jamais écrit dans le journal.
/// </summary>
internal sealed class StatusServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Postes _postes;
    private readonly Action<string> _log;
    private readonly string _token;
    private readonly Func<(IPAddress? Address, IPAddress? Mask)> _network;
    private readonly RateLimiter _limiter = new(StatusPolicy.MaxRequestsPerMinute);
    private readonly RateLimiter _rejectLog = new(5);

    public StatusServer(Postes postes, Action<string> log, string token, Func<(IPAddress? Address, IPAddress? Mask)> network)
    {
        _postes = postes;
        _log = log;
        _token = token;
        _network = network;
        _listener.Prefixes.Add($"http://+:{ServerConfig.StatusPort}/jvtech/");
    }

    public void Start()
    {
        _listener.Start();
        new Thread(Listen) { IsBackground = true, Name = "Suivi des postes" }.Start();
    }

    private void Listen()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = _listener.GetContext(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }

            try
            {
                context.Response.StatusCode = Handle(context);
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException or UnauthorizedAccessException or DecoderFallbackException)
            {
                _log("Suivi des postes : " + ex.Message);
                try { context.Response.StatusCode = 500; } catch (Exception inner) when (inner is InvalidOperationException or ObjectDisposedException) { }
            }
            finally
            {
                try { context.Response.Close(); } catch (HttpListenerException) { }
            }
        }
    }

    private int Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var remote = request.RemoteEndPoint?.Address;
        var (server, mask) = _network();
        if (!StatusPolicy.IsAllowedSource(remote, server, mask)) return Reject(remote, 403, "hors du réseau de l'atelier");
        if (!_limiter.Allow(remote?.ToString() ?? "?", DateTime.Now)) return Reject(remote, 429, "trop de remontées");
        if (request.HttpMethod != "POST") return 405;
        if (!StatusPolicy.TokenMatches(_token, request.Headers[StatusPolicy.TokenHeader])) return Reject(remote, 401, "jeton absent ou faux (kit d'installation ancien ?)");

        var path = request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
        var isStatus = path.EndsWith("/jvtech/etat");
        var isReport = path.EndsWith("/jvtech/rapport");
        if (!isStatus && !isReport) return 404;

        var limit = isStatus ? StatusPolicy.MaxStatusBytes : StatusPolicy.MaxReportBytes;
        if (request.ContentLength64 > limit) return Reject(remote, 413, "remontée trop grosse");
        var body = ReadLimited(request.InputStream, limit);
        if (body is null) return Reject(remote, 413, "remontée trop grosse");

        if (isStatus)
        {
            var values = StatusPolicy.ParseStatus(body);
            if (values is null) return Reject(remote, 400, "remontée illisible");
            _postes.Status(values);
            return 200;
        }

        var serial = StatusPolicy.Clean(request.QueryString["serie"]);
        if (body.IndexOf("<html", StringComparison.OrdinalIgnoreCase) < 0) return Reject(remote, 400, "rapport qui n'est pas une page HTML");
        SaveReport(serial, StatusPolicy.SafeReportName(request.QueryString["nom"], serial, DateTime.Now), body);
        return 200;
    }

    /// <summary>Lit au plus <paramref name="limit"/> octets : null si le corps est plus gros (Content-Length absent ou faux).</summary>
    private static string? ReadLimited(Stream input, int limit)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > limit) return null;
            buffer.Write(chunk, 0, read);
        }
        return new UTF8Encoding(false, true).GetString(buffer.ToArray());
    }

    private int Reject(IPAddress? remote, int code, string why)
    {
        // Journal limité : une machine qui insiste ne doit pas le remplir.
        if (_rejectLog.Allow(remote?.ToString() ?? "?", DateTime.Now)) _log($"Suivi des postes : remontée refusée de {remote} ({why}).");
        return code;
    }

    private void SaveReport(string serial, string fileName, string html)
    {
        var folder = Path.Combine(ServerConfig.ReportsFolder, DateTime.Now.ToString("yyyy-MM"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        File.WriteAllText(path, html, new UTF8Encoding(true));
        _postes.Report(serial, path);
        _log("Rapport reçu : " + path);
    }

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); }
        catch (ObjectDisposedException) { }
    }
}
