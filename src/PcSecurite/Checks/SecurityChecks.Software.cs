using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace PcSecurite.Checks;

/// <summary>Logiciels installés, navigateurs et réglages réseau détournés par les arnaques et les logiciels malveillants.</summary>
public static partial class SecurityChecks
{
    private static readonly string[] RemoteTools =
    {
        "AnyDesk", "TeamViewer", "UltraViewer", "RustDesk", "ScreenConnect", "ConnectWise Control", "Splashtop", "LogMeIn",
        "Supremo", "Ammyy", "AeroAdmin", "AweSun", "RemotePC", "Zoho Assist", "SimpleHelp", "NetSupport", "DWService", "GoTo Resolve",
    };

    private static SecurityCheck RemoteAccessTools()
    {
        const string name = "Logiciels de prise en main à distance";
        static string Key(string s) => s.Replace(" ", "").ToLowerInvariant();

        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var program in InstalledPrograms())
        {
            var tool = RemoteTools.FirstOrDefault(t => program.Name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            if (tool is not null) found.Add(tool);
        }
        // Les versions portables (AnyDesk lancé depuis Téléchargements) ne sont pas installées : on regarde aussi les programmes en cours.
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var tool = RemoteTools.FirstOrDefault(t => Key(process.ProcessName).Contains(Key(t)));
                if (tool is not null) found.Add(tool + " (en cours d'exécution)");
            }
        }

        if (found.Count == 0) return SecurityCheck.Ok(name, "Aucun trouvé");
        return SecurityCheck.Warning(name, "Trouvé : " + string.Join(", ", found),
            "Demander au client s'il les a installés lui-même : les arnaques au faux support technique passent par ces logiciels. Désinstaller ceux qui ne servent pas.");
    }

    private static SecurityCheck NetworkRedirects()
    {
        const string name = "Redirections réseau (hosts, proxy)";
        var problems = new List<string>();

        var hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
        if (File.Exists(hosts))
        {
            var entries = File.ReadAllLines(hosts)
                .Select(l => l.Split('#')[0].Trim())
                .Where(l => l.Length > 0)
                .Select(l => l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length >= 2 && !parts.Skip(1).All(h => h.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
                .Select(parts => parts[1])
                .ToList();
            if (entries.Count > 0)
                problems.Add($"fichier hosts modifié ({entries.Count} ligne(s) : {string.Join(", ", entries.Take(3))}{(entries.Count > 3 ? ", …" : "")})");
        }

        using (var internet = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
        {
            if (internet?.GetValue("ProxyEnable") is int proxy && proxy == 1)
                problems.Add("proxy " + (internet.GetValue("ProxyServer") as string ?? "activé"));
            if (internet?.GetValue("AutoConfigURL") is string pac && pac.Length > 0)
                problems.Add("script de proxy " + pac);
        }

        if (problems.Count == 0) return SecurityCheck.Ok(name, "Aucune redirection");
        return SecurityCheck.Warning(name, string.Join(" · ", problems),
            "Vérifier ces redirections : elles servent à envoyer vers de faux sites (banque, mises à jour). Supprimer celles que le client ne connaît pas.");
    }

    private static SecurityCheck WifiProfiles()
    {
        const string name = "Réseaux Wi-Fi enregistrés";
        // L'export XML ne dépend pas de la langue de Windows, contrairement au texte de « netsh wlan show ».
        // Sans « key=clear », les mots de passe restent chiffrés dans les fichiers.
        var folder = Path.Combine(Path.GetTempPath(), "PcSecurite-wifi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var start = new ProcessStartInfo("netsh", $"wlan export profile folder=\"{folder}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using (var netsh = Process.Start(start)!)
            {
                netsh.StandardOutput.ReadToEnd();
                netsh.WaitForExit(15000);
            }

            var risky = new List<string>();
            var count = 0;
            foreach (var file in Directory.GetFiles(folder, "*.xml"))
            {
                var xml = XDocument.Load(file);
                string? Value(string element) => xml.Descendants().FirstOrDefault(e => e.Name.LocalName == element)?.Value;
                count++;
                var ssid = Value("name") ?? "?";
                var auth = Value("authentication") ?? "";
                var encryption = Value("encryption") ?? "";
                if (encryption.Equals("WEP", StringComparison.OrdinalIgnoreCase)) risky.Add(ssid + " (WEP)");
                else if (auth.Equals("open", StringComparison.OrdinalIgnoreCase) && Value("connectionMode") == "auto") risky.Add(ssid + " (ouvert)");
            }

            if (count == 0) return SecurityCheck.Info(name, "Aucun réseau Wi-Fi enregistré");
            if (risky.Count == 0) return SecurityCheck.Ok(name, $"{count} réseau(x), tous protégés par un mot de passe");
            return SecurityCheck.Warning(name, "Connexion automatique à des réseaux non protégés : " + string.Join(", ", risky),
                "Les oublier (Paramètres > Wi-Fi > Gérer les réseaux connus) : n'importe qui peut créer un faux réseau du même nom.");
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static SecurityCheck Browsers()
    {
        const string name = "Navigateurs internet";
        var found = new List<string>();
        var stale = new List<string>();
        foreach (var (exe, label) in new[]
        {
            ("chrome.exe", "Chrome"), ("msedge.exe", "Edge"), ("firefox.exe", "Firefox"), ("brave.exe", "Brave"),
        })
        {
            var path = AppPath(exe);
            if (path is null || !File.Exists(path)) continue;
            var version = FileVersionInfo.GetVersionInfo(path).ProductVersion;
            // Le fichier du navigateur est remplacé à chaque mise à jour : sa date dit quand il a été mis à jour.
            var days = (int)(DateTime.Now - File.GetLastWriteTime(path)).TotalDays;
            var text = $"{label} {version}";
            found.Add(text);
            if (days > 45) stale.Add($"{text} (pas mis à jour depuis {days} jours)");
        }

        if (found.Count == 0) return SecurityCheck.Info(name, "Aucun navigateur courant trouvé");
        if (stale.Count == 0) return SecurityCheck.Ok(name, string.Join(", ", found) + " : à jour");
        return SecurityCheck.Warning(name, string.Join(", ", stale),
            "Ouvrir le navigateur > Aide > À propos pour lancer la mise à jour, puis le redémarrer. Les navigateurs sont la première porte d'entrée des attaques.");
    }

    private static string? AppPath(string exe)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (key?.GetValue(null) is string path && path.Length > 0) return path.Trim('"');
        }
        return null;
    }

    // Logiciels avec des failles exploitées par les pirates, et la première version corrigée.
    private static readonly (Regex Name, Version Fixed, string Why, bool Danger)[] Vulnerable =
    {
        (new Regex(@"^WinRAR", RegexOptions.IgnoreCase), new Version(7, 13), "faille exploitée par des pirates (CVE-2025-8088)", true),
        (new Regex(@"^7-Zip", RegexOptions.IgnoreCase), new Version(25, 1), "failles corrigées en 2025 (CVE-2025-0411, CVE-2025-55188)", false),
        (new Regex(@"^Notepad\+\+", RegexOptions.IgnoreCase), new Version(8, 8, 9), "mise à jour détournée par des pirates, corrigée en 8.8.9", false),
        (new Regex(@"^PuTTY", RegexOptions.IgnoreCase), new Version(0, 81), "clé privée récupérable (CVE-2024-31497)", false),
        (new Regex(@"^VLC media player", RegexOptions.IgnoreCase), new Version(3, 0, 21), "faille corrigée en 3.0.21", false),
    };

    private static readonly Regex VersionPattern = new(@"\d+(\.\d+)+");

    private static SecurityCheck VulnerableSoftware()
    {
        const string name = "Logiciels avec failles connues";
        var found = new List<string>();
        var danger = false;
        foreach (var program in InstalledPrograms())
        {
            var rule = Vulnerable.FirstOrDefault(r => r.Name.IsMatch(program.Name));
            if (rule.Name is null) continue;
            var match = VersionPattern.Match(program.Version ?? "");
            if (!match.Success) match = VersionPattern.Match(program.Name);
            if (!match.Success || !Version.TryParse(match.Value, out var version) || version >= rule.Fixed) continue;
            found.Add($"{program.Name} {program.Version} : {rule.Why}");
            danger |= rule.Danger;
        }

        if (found.Count == 0) return SecurityCheck.Ok(name, "Aucun logiciel connu pour être vulnérable");
        const string advice = "Mettre à jour ces logiciels depuis leur site officiel (ou winget upgrade --all).";
        var detail = string.Join(" · ", found.Distinct());
        return danger ? SecurityCheck.Danger(name, detail, advice) : SecurityCheck.Warning(name, detail, advice);
    }

    // Logiciels qui ne reçoivent plus aucun correctif : seule solution, les désinstaller ou les remplacer.
    private static readonly (Regex Name, string Why, bool Danger)[] Obsolete =
    {
        (new Regex(@"Adobe Flash Player", RegexOptions.IgnoreCase), "abandonné depuis 2020", true),
        (new Regex(@"Adobe Shockwave", RegexOptions.IgnoreCase), "abandonné depuis 2019", true),
        (new Regex(@"Microsoft Silverlight", RegexOptions.IgnoreCase), "abandonné depuis 2021", false),
        (new Regex(@"^QuickTime", RegexOptions.IgnoreCase), "abandonné sur Windows depuis 2016", false),
        (new Regex(@"^Java(\(TM\))? ?[67]\b|^J2SE Runtime", RegexOptions.IgnoreCase), "plus de correctifs", false),
        (new Regex(@"^Adobe Reader (8|9|X|XI)\b|^Adobe Acrobat (Reader )?(X|XI|2015|2017|2020)\b", RegexOptions.IgnoreCase), "plus de correctifs", false),
        (new Regex(@"^Microsoft Office (?!.*(Click-to-Run|Proof|MUI|Language|Langue|Shared|Component|Viewer|Validation|Live Meeting)).*\b(2003|2007|2010|2013)\b", RegexOptions.IgnoreCase), "plus de correctifs depuis des années", false),
        (new Regex(@"^Microsoft Office (?!.*(Click-to-Run|Proof|MUI|Language|Langue|Shared|Component|Viewer|Validation)).*\b(2016|2019)\b", RegexOptions.IgnoreCase), "plus de correctifs depuis le 14/10/2025", false),
        (new Regex(@"^Microsoft Office (?!.*(Click-to-Run|Proof|MUI|Language|Langue|Shared|Component|Viewer|Validation)).*\b2021\b", RegexOptions.IgnoreCase), "plus de correctifs depuis le 13/10/2026", false),
        (new Regex(@"^Windows Live (Essentials|Mail|Messenger)", RegexOptions.IgnoreCase), "abandonné depuis 2017", false),
        (new Regex(@"^Skype\b(?!.*(Entreprise|Business))", RegexOptions.IgnoreCase), "service fermé en mai 2025", false),
    };

    private static SecurityCheck ObsoleteSoftware()
    {
        const string name = "Logiciels abandonnés";
        var found = new List<string>();
        var danger = false;
        foreach (var program in InstalledPrograms())
        {
            var rule = Obsolete.FirstOrDefault(r => r.Name.IsMatch(program.Name));
            if (rule.Name is null) continue;
            found.Add($"{program.Name} ({rule.Why})");
            danger |= rule.Danger;
        }

        if (found.Count == 0) return SecurityCheck.Ok(name, "Aucun logiciel abandonné");
        const string advice = "Désinstaller ces logiciels, ou les remplacer par une version suivie (Microsoft 365 ou Office 2024, Acrobat Reader à jour, LibreOffice…).";
        var detail = string.Join(" · ", found.Distinct());
        return danger
            ? SecurityCheck.Danger(name, detail, advice, CheckKind.Change)
            : SecurityCheck.Warning(name, detail, advice, CheckKind.Change);
    }

    private static List<(string Name, string? Version)>? _programs;

    /// <summary>Logiciels installés (mêmes clés que « Applications et fonctionnalités »), lus une seule fois par analyse.</summary>
    private static List<(string Name, string? Version)> InstalledPrograms()
    {
        if (_programs is not null) return _programs;
        var list = new List<(string, string?)>();
        foreach (var (hive, view) in new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default),
        })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var subKey in uninstall.GetSubKeyNames())
            {
                using var app = uninstall.OpenSubKey(subKey);
                var displayName = (app?.GetValue("DisplayName") as string)?.Trim();
                if (string.IsNullOrEmpty(displayName)) continue;
                list.Add((displayName!, app!.GetValue("DisplayVersion") as string));
            }
        }
        return _programs = list.Distinct().ToList();
    }

    /// <summary>À appeler avant chaque analyse pour relire la liste des logiciels (le technicien a pu en désinstaller).</summary>
    public static void ResetCache() => _programs = null;
}
