using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace JvtechServeur.Pxe;

/// <summary>
/// Règles du suivi des postes (port 8081), séparées du serveur HTTP pour être testées sans réseau :
/// jeton partagé, réseau local seulement, taille et contenu des remontées limités, débit limité par adresse.
/// </summary>
internal static class StatusPolicy
{
    public const string TokenHeader = "X-JVTECH-Jeton";
    /// <summary>Avancement : quelques lignes « Nom=valeur ».</summary>
    public const int MaxStatusBytes = 16 * 1024;
    /// <summary>Rapport HTML d'un poste : quelques centaines de Ko en pratique.</summary>
    public const int MaxReportBytes = 5 * 1024 * 1024;
    public const int MaxRequestsPerMinute = 120;
    private const int MaxValueLength = 300;

    /// <summary>Champs acceptés dans une remontée d'avancement ; les autres sont ignorés.</summary>
    public static readonly HashSet<string> StatusFields = new(StringComparer.OrdinalIgnoreCase)
        { "Macs", "Serie", "Modele", "Debut", "Etape", "Detail", "Fini", "AVerifier" };

    /// <summary>Jeton aléatoire de 32 octets (256 bits), lisible dans un fichier ini.</summary>
    public static string NewToken()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Comparaison en temps constant : la durée ne révèle pas combien de caractères sont justes.</summary>
    public static bool TokenMatches(string? expected, string? received)
    {
        if (string.IsNullOrEmpty(expected) || received is null) return false;
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(received);
        var diff = a.Length ^ b.Length;
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
            diff |= (i < a.Length ? a[i] : 0) ^ (i < b.Length ? b[i] : 0);
        return diff == 0;
    }

    /// <summary>
    /// Seuls le serveur lui-même et les machines du même sous-réseau IPv4 que la carte du serveur sont acceptés
    /// (les PC en préparation sont sur le réseau de l'atelier).
    /// </summary>
    public static bool IsAllowedSource(IPAddress? remote, IPAddress? server, IPAddress? mask)
    {
        if (remote is null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (IPAddress.IsLoopback(remote)) return true;
        if (remote.AddressFamily != AddressFamily.InterNetwork || server is null || mask is null) return false;
        if (server.AddressFamily != AddressFamily.InterNetwork || mask.AddressFamily != AddressFamily.InterNetwork) return false;
        var r = remote.GetAddressBytes();
        var s = server.GetAddressBytes();
        var m = mask.GetAddressBytes();
        for (var i = 0; i < 4; i++)
            if ((r[i] & m[i]) != (s[i] & m[i])) return false;
        return true;
    }

    /// <summary>
    /// Lignes « Nom=valeur » d'une remontée : seulement les champs connus, valeurs raccourcies et sans caractères de
    /// contrôle. Renvoie null si rien d'exploitable (remontée malformée).
    /// </summary>
    public static Dictionary<string, string>? ParseStatus(string body)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(new[] { '=' }, 2);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            if (!StatusFields.Contains(key)) continue;
            values[key] = Clean(parts[1]);
        }
        if (values.Count == 0) return null;
        if (values.TryGetValue("Macs", out var macs))
        {
            var valid = macs.Split(',').Select(m => m.Trim()).Where(IsMac).Take(8).ToList();
            values["Macs"] = string.Join(",", valid);
        }
        // Un poste se reconnaît à son numéro de série ou à une adresse MAC : sans les deux, la remontée est inutilisable.
        var hasSerial = values.TryGetValue("Serie", out var serial) && serial.Length > 0;
        var hasMac = values.TryGetValue("Macs", out var mac) && mac.Length > 0;
        return hasSerial || hasMac ? values : null;
    }

    /// <summary>Nom du fichier de rapport : caractères sûrs seulement, .html imposé, longueur limitée.</summary>
    public static string SafeReportName(string? name, string? serial, DateTime now)
    {
        var raw = string.IsNullOrWhiteSpace(name) ? $"{now:yyyy-MM-dd HH-mm} {serial}" : name!;
        var chars = raw.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' ? c : '_').ToArray();
        var safe = new string(chars).Trim().TrimStart('.');
        if (safe.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) safe = safe.Substring(0, safe.Length - 5);
        if (safe.Length == 0) safe = "rapport";
        if (safe.Length > 120) safe = safe.Substring(0, 120);
        return safe + ".html";
    }

    public static string Clean(string? value)
    {
        var text = new string((value ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return text.Length > MaxValueLength ? text.Substring(0, MaxValueLength) : text;
    }

    private static bool IsMac(string text) =>
        text.Length == 17 && text.Where((c, i) => i % 3 == 2 ? c is ':' or '-' : Uri.IsHexDigit(c)).Count() == 17;
}

/// <summary>Limite de débit par adresse : au-delà de N remontées par minute, les suivantes sont refusées.</summary>
internal sealed class RateLimiter
{
    private readonly int _perMinute;
    private readonly Dictionary<string, (DateTime Start, int Count)> _counts = new();
    private readonly object _lock = new();

    public RateLimiter(int perMinute) => _perMinute = perMinute;

    public bool Allow(string key, DateTime now)
    {
        lock (_lock)
        {
            if (_counts.Count > 1000) _counts.Clear(); // jamais de croissance sans fin
            if (!_counts.TryGetValue(key, out var entry) || now - entry.Start >= TimeSpan.FromMinutes(1)) entry = (now, 0);
            entry.Count++;
            _counts[key] = entry;
            return entry.Count <= _perMinute;
        }
    }
}
