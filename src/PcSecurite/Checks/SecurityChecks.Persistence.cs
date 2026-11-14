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
                else if (References.KnownDns.TryGetValue(server, out var owner) || IsNextDns(server, out owner)) known.Add($"{server} ({owner})");
                else unknown.Add($"{server} sur {adapter.Str("Description") ?? "carte réseau"}");
            }
        }

        if (unknown.Count > 0)
            // Avertissement et non danger : un DNS absent de la liste peut être celui d'une entreprise, d'un VPN ou d'un
            // opérateur moins courant. Le technicien vérifie avec le client avant de toucher à quoi que ce soit.
            return SecurityCheck.Warning(name, "DNS absent de la liste des DNS connus : " + string.Join(", ", unknown.Distinct()),
                "Demander au client s'il a choisi ce DNS (VPN, entreprise, contrôle parental). Sinon, un DNS inconnu peut envoyer vers de faux sites (banque, mises à jour) : remettre les DNS en automatique.");
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
        if (tasks is null) return SecurityCheck.Unknown(name, "Impossible de lire le Planificateur de tâches");

        var flagged = new List<(string Text, bool Severe)>();
        var signatures = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        int microsoft = 0, thirdParty = 0;
        foreach (var (taskName, command) in tasks)
        {
            var expanded = Environment.ExpandEnvironmentVariables(command);
            var isMicrosoft = taskName.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);
            if (isMicrosoft) microsoft++; else thirdParty++;

            // Les règles s'appliquent à toutes les tâches : un malware se cache volontiers sous \Microsoft\Windows\.
            var rule = TaskRules.FirstOrDefault(r => r.Pattern.IsMatch(expanded));
            var trusted = !isMicrosoft && TrustedAppData.Any(t => expanded.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            if (rule.Pattern is not null && !trusted)
            {
                flagged.Add(($"{taskName.TrimStart('\\')} : {rule.Why} ({Shorten(expanded)})", rule.Severe));
                continue;
            }

            // Une tâche rangée chez Microsoft doit lancer un programme signé par Microsoft (signature intégrée ou catalogue Windows).
            if (!isMicrosoft || TaskProgram(expanded) is not { } program) continue;
            if (!signatures.TryGetValue(program, out var signed)) signatures[program] = signed = PcInspector.Authenticode.IsMicrosoftSigned(program);
            if (signed) continue;
            // Signé par un autre éditeur reconnu (pilote, Office d'un OEM…) : à vérifier. Pas signé du tout : très suspect.
            var other = PcInspector.Authenticode.IsSigned(program) ? PcInspector.Authenticode.Publisher(program) : null;
            flagged.Add(other is not null
                ? ($"{taskName.TrimStart('\\')} : rangée parmi les tâches de Microsoft mais lance un programme signé par {other} ({Shorten(program)})", false)
                : ($"{taskName.TrimStart('\\')} : rangée parmi les tâches de Microsoft mais lance un programme non signé ({Shorten(program)})", true));
        }

        if (flagged.Count == 0) return SecurityCheck.Ok(name, $"{thirdParty} tâche(s) d'éditeurs tiers et {microsoft} de Microsoft (signatures vérifiées), rien de suspect");
        var detail = string.Join(" · ", flagged.Select(f => f.Text));
        const string advice = "Vérifier ces tâches dans le Planificateur : un logiciel malveillant s'en sert pour revenir à chaque démarrage. Désactiver celles que le client ne connaît pas, puis lancer une analyse antivirus et AdwCleaner (PC Maintenance).";
        return flagged.Any(f => f.Severe)
            ? SecurityCheck.Danger(name, detail, advice)
            : SecurityCheck.Warning(name, detail, advice);
    }

    /// <summary>
    /// Chemin du programme lancé par la tâche (entre guillemets, ou jusqu'au premier .exe). Null si ce n'est pas un .exe
    /// existant : gestionnaire COM, script, ou programme d'une fonction retirée de Windows (on ne signale pas l'absence).
    /// </summary>
    private static string? TaskProgram(string command)
    {
        var text = command.Trim();
        string candidate;
        if (text.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = text.IndexOf('"', 1);
            if (end < 0) return null;
            candidate = text.Substring(1, end - 1);
        }
        else
        {
            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe < 0) return null;
            candidate = text.Substring(0, exe + 4);
        }
        if (!candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        // « rundll32.exe » sans chemin : c'est celui de System32.
        if (candidate.IndexOf('\\') < 0) candidate = PcInspector.AppPaths.SystemTool(candidate);
        try { return System.IO.File.Exists(candidate) ? System.IO.Path.GetFullPath(candidate) : null; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException) { return null; }
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
            using var process = Process.Start(new ProcessStartInfo(PcInspector.AppPaths.SystemTool("schtasks.exe"), "/query /fo csv /v /nh")
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
