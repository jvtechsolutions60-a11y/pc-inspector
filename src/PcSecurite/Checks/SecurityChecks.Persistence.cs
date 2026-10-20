using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using PcInspector.Collectors;

namespace PcSecurite.Checks;

/// <summary>Serveurs DNS détournés et tâches planifiées qui relancent un logiciel malveillant. Lecture seule.</summary>
public static partial class SecurityChecks
{
    // Box des opérateurs français et DNS publics connus.
    private static readonly Dictionary<string, string> KnownDns = new()
    {
        ["80.10.246.2"] = "Orange", ["80.10.246.129"] = "Orange", ["81.253.149.1"] = "Orange", ["81.253.149.2"] = "Orange",
        ["212.27.40.240"] = "Free", ["212.27.40.241"] = "Free",
        ["109.0.66.10"] = "SFR", ["109.0.66.20"] = "SFR", ["86.64.145.140"] = "SFR", ["86.64.145.146"] = "SFR",
        ["194.158.122.10"] = "Bouygues Telecom", ["194.158.122.15"] = "Bouygues Telecom",
        ["8.8.8.8"] = "Google", ["8.8.4.4"] = "Google",
        ["1.1.1.1"] = "Cloudflare", ["1.0.0.1"] = "Cloudflare", ["1.1.1.2"] = "Cloudflare", ["1.0.0.2"] = "Cloudflare", ["1.1.1.3"] = "Cloudflare", ["1.0.0.3"] = "Cloudflare",
        ["9.9.9.9"] = "Quad9", ["149.112.112.112"] = "Quad9", ["9.9.9.10"] = "Quad9", ["9.9.9.11"] = "Quad9",
        ["208.67.222.222"] = "OpenDNS", ["208.67.220.220"] = "OpenDNS", ["208.67.222.123"] = "OpenDNS", ["208.67.220.123"] = "OpenDNS",
        ["94.140.14.14"] = "AdGuard", ["94.140.15.15"] = "AdGuard", ["94.140.14.15"] = "AdGuard", ["94.140.15.16"] = "AdGuard",
        ["80.67.169.12"] = "FDN", ["80.67.169.40"] = "FDN",
        ["193.110.81.0"] = "DNS0.eu", ["185.253.5.0"] = "DNS0.eu",
        ["76.76.2.0"] = "Control D", ["76.76.10.0"] = "Control D",
    };

    private static SecurityCheck DnsServers()
    {
        const string name = "Serveurs DNS";
        var known = new List<string>();
        var unknown = new List<string>();
        foreach (var adapter in Wmi.QueryOptional("SELECT Description, DNSServerSearchOrder FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE"))
        {
            if (adapter["DNSServerSearchOrder"] is not string[] servers) continue;
            foreach (var server in servers.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()))
            {
                if (IsLocal(server)) known.Add(server + " (box ou réseau local)");
                else if (KnownDns.TryGetValue(server, out var owner) || IsNextDns(server, out owner)) known.Add($"{server} ({owner})");
                else unknown.Add($"{server} sur {adapter.Str("Description") ?? "carte réseau"}");
            }
        }

        if (unknown.Count > 0)
            return SecurityCheck.Danger(name, "DNS inconnu : " + string.Join(", ", unknown.Distinct()),
                "Un DNS inconnu peut envoyer vers de faux sites (banque, mises à jour). Remettre les DNS en automatique si le client ne l'a pas choisi.");
        if (known.Count == 0) return SecurityCheck.Info(name, "Aucun serveur DNS configuré (pas de connexion réseau ?)");
        return SecurityCheck.Ok(name, string.Join(", ", known.Distinct()));
    }

    private static bool IsLocal(string server)
    {
        if (!IPAddress.TryParse(server, out var ip)) return false;
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || server.StartsWith("fd", StringComparison.OrdinalIgnoreCase) || IPAddress.IsLoopback(ip);
        var b = ip.GetAddressBytes();
        return b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    private static bool IsNextDns(string server, out string owner)
    {
        owner = "NextDNS";
        return server.StartsWith("45.90.28.", StringComparison.Ordinal) || server.StartsWith("45.90.30.", StringComparison.Ordinal);
    }

    // ---------- Tâches planifiées ----------

    // Logiciels connus qui se mettent à jour depuis AppData : pas suspects en eux-mêmes.
    private static readonly string[] TrustedAppData = { @"\Microsoft\OneDrive\", @"\Microsoft\Teams\", @"\Zoom\", @"\Discord\", @"\Dropbox\", @"\Spotify\", @"\Google\", @"\Mozilla\", @"\Programs\Opera" };

    private static readonly (Regex Pattern, string Why, bool Severe)[] TaskRules =
    {
        (new Regex(@"powershell(\.exe)?.*(-e(nc|ncodedcommand)?\s|downloadstring|iex\b|invoke-expression|frombase64string)", RegexOptions.IgnoreCase), "PowerShell avec commande cachée ou téléchargement", true),
        (new Regex(@"powershell(\.exe)?.*-w(indowstyle)?\s+h(idden)?", RegexOptions.IgnoreCase), "PowerShell en fenêtre cachée", true),
        (new Regex(@"mshta(\.exe)?\s", RegexOptions.IgnoreCase), "mshta (script lancé depuis une page web)", true),
        (new Regex(@"(regsvr32|certutil|bitsadmin)(\.exe)?.*https?://", RegexOptions.IgnoreCase), "outil Windows détourné pour télécharger", true),
        (new Regex(@"(wscript|cscript)(\.exe)?.*\\(appdata|temp|users\\public)\\", RegexOptions.IgnoreCase), "script lancé depuis un dossier utilisateur", true),
        (new Regex(@"\\(windows\\)?temp\\", RegexOptions.IgnoreCase), "programme lancé depuis un dossier temporaire", true),
        (new Regex(@"\\users\\public\\", RegexOptions.IgnoreCase), "programme dans le dossier Public", true),
        (new Regex(@"\\programdata\\[^\\]+\.(exe|dll|bat|cmd|vbs|js|ps1)", RegexOptions.IgnoreCase), "programme posé à la racine de ProgramData", true),
        (new Regex(@"rundll32(\.exe)?.*\\appdata\\", RegexOptions.IgnoreCase), "DLL lancée depuis AppData", true),
        (new Regex(@"\\appdata\\", RegexOptions.IgnoreCase), "programme lancé depuis AppData", false),
    };

    private static SecurityCheck ScheduledTasks()
    {
        const string name = "Tâches planifiées";
        var tasks = ReadTasks();
        if (tasks is null) return SecurityCheck.Info(name, "Impossible de lire le Planificateur de tâches");

        var thirdParty = tasks.Where(t => !t.Name.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)).ToList();
        var flagged = new List<(string Text, bool Severe)>();
        foreach (var (taskName, command) in thirdParty)
        {
            var expanded = Environment.ExpandEnvironmentVariables(command);
            if (TrustedAppData.Any(t => expanded.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
            var rule = TaskRules.FirstOrDefault(r => r.Pattern.IsMatch(expanded));
            if (rule.Pattern is null) continue;
            flagged.Add(($"{taskName.TrimStart('\\')} : {rule.Why} ({Shorten(expanded)})", rule.Severe));
        }

        if (flagged.Count == 0) return SecurityCheck.Ok(name, $"{thirdParty.Count} tâche(s) hors Microsoft, rien de suspect");
        var detail = string.Join(" · ", flagged.Select(f => f.Text));
        const string advice = "Vérifier ces tâches dans le Planificateur : un logiciel malveillant s'en sert pour revenir à chaque démarrage. Désactiver celles que le client ne connaît pas, puis lancer une analyse antivirus et AdwCleaner (PC Maintenance).";
        return flagged.Any(f => f.Severe)
            ? SecurityCheck.Danger(name, detail, advice)
            : SecurityCheck.Warning(name, detail, advice);
    }

    private static string Shorten(string command) => command.Length <= 90 ? command : command.Substring(0, 87) + "…";

    /// <summary>
    /// Nom et commande de chaque tâche, lus avec « schtasks /query /fo csv /v /nh ». Les colonnes sont toujours dans le même ordre,
    /// quelle que soit la langue : 1 = nom de la tâche, 8 = programme à lancer.
    /// </summary>
    private static List<(string Name, string Command)>? ReadTasks()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", "/query /fo csv /v /nh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            });
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);

            var result = new List<(string, string)>();
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = Csv(line);
                if (fields.Count < 9) continue;
                var command = fields[8];
                // « COM handler », « Plusieurs actions »… : pas de programme à vérifier.
                if (command.IndexOf('\\') < 0 && command.IndexOf('.') < 0) continue;
                result.Add((fields[1], command));
            }
            return result.Distinct().ToList();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static List<string> Csv(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }
}
