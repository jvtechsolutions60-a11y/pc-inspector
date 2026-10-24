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
/// </summary>
internal sealed class StatusServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Postes _postes;
    private readonly Action<string> _log;

    public StatusServer(Postes postes, Action<string> log)
    {
        _postes = postes;
        _log = log;
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
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var body = reader.ReadToEnd();
                var path = context.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
                if (context.Request.HttpMethod == "POST" && path.EndsWith("/etat"))
                {
                    _postes.Status(Parse(body));
                }
                else if (context.Request.HttpMethod == "POST" && path.EndsWith("/rapport"))
                {
                    SaveReport(context.Request.QueryString["serie"] ?? "", context.Request.QueryString["nom"] ?? "", body);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException or UnauthorizedAccessException)
            {
                _log("Suivi des postes : " + ex.Message);
                context.Response.StatusCode = 500;
            }
            finally
            {
                try { context.Response.Close(); } catch (HttpListenerException) { }
            }
        }
    }

    private void SaveReport(string serial, string name, string html)
    {
        var folder = Path.Combine(ServerConfig.ReportsFolder, DateTime.Now.ToString("yyyy-MM"));
        Directory.CreateDirectory(folder);
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((name.Length > 0 ? name : $"{DateTime.Now:yyyy-MM-dd HH-mm} {serial}").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (!safe.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) safe += ".html";
        var path = Path.Combine(folder, safe);
        File.WriteAllText(path, html, new UTF8Encoding(true));
        _postes.Report(serial, path);
        _log("Rapport reçu : " + path);
    }

    private static Dictionary<string, string> Parse(string body) =>
        body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(new[] { '=' }, 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last()[1].Trim(), StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); }
        catch (ObjectDisposedException) { }
    }
}
