using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PcSecurite.Checks;

/// <summary>
/// Données de référence de PC Sécurité, réunies ici pour être tenues à jour en un seul endroit :
/// fins de support de Windows, DNS connus, logiciels vulnérables et logiciels abandonnés.
/// Mise à jour (voir docs/references-pc-securite.md) : modifier les listes, puis changer <see cref="UpdatedOn"/>.
/// Au-delà d'un an sans mise à jour, les contrôles qui s'appuient dessus ne disent plus « OK » mais « non vérifié ».
/// </summary>
internal static class References
{
    /// <summary>Date de la dernière vérification de ces listes.</summary>
    public static readonly DateTime UpdatedOn = new(2026, 11, 10);

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(365);

    public static bool IsStale(DateTime today) => today - UpdatedOn > MaxAge;

    /// <summary>Phrase ajoutée à un résultat favorable quand les listes sont anciennes.</summary>
    public static string StaleNote => $"listes de référence du {UpdatedOn:dd/MM/yyyy}, trop anciennes pour l'affirmer : mettre PC Sécurité à jour";

    // Fin du support des versions de Windows (éditions Famille et Pro).
    public static readonly (int Build, string Name, DateTime End)[] WindowsSupport =
    {
        (26200, "Windows 11 25H2", new DateTime(2027, 10, 12)),
        (26100, "Windows 11 24H2", new DateTime(2026, 10, 13)),
        (22631, "Windows 11 23H2", new DateTime(2025, 11, 11)),
        (22621, "Windows 11 22H2", new DateTime(2024, 10, 8)),
        (22000, "Windows 11 21H2", new DateTime(2023, 10, 10)),
        (0, "Windows 10", new DateTime(2025, 10, 14)),
    };

    // Box des opérateurs français et DNS publics connus.
    public static readonly Dictionary<string, string> KnownDns = new()
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

    // Logiciels avec des failles exploitées par les pirates, et la première version corrigée.
    public static readonly (Regex Name, Version Fixed, string Why, bool Danger)[] VulnerableSoftware =
    {
        (new Regex(@"^WinRAR", RegexOptions.IgnoreCase), new Version(7, 13), "faille exploitée par des pirates (CVE-2025-8088)", true),
        (new Regex(@"^7-Zip", RegexOptions.IgnoreCase), new Version(25, 1), "failles corrigées en 2025 (CVE-2025-0411, CVE-2025-55188)", false),
        (new Regex(@"^Notepad\+\+", RegexOptions.IgnoreCase), new Version(8, 8, 9), "mise à jour détournée par des pirates, corrigée en 8.8.9", false),
        (new Regex(@"^PuTTY", RegexOptions.IgnoreCase), new Version(0, 81), "clé privée récupérable (CVE-2024-31497)", false),
        (new Regex(@"^VLC media player", RegexOptions.IgnoreCase), new Version(3, 0, 21), "faille corrigée en 3.0.21", false),
    };

    // Logiciels qui ne reçoivent plus aucun correctif : seule solution, les désinstaller ou les remplacer.
    public static readonly (Regex Name, string Why, bool Danger)[] ObsoleteSoftware =
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
}
