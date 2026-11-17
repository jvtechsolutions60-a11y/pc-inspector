using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace JvtechEntretien;

/// <summary>
/// Rapport de l'entretien : points à voir, page HTML avant/après et ligne du tableau de suivi.
/// Aucune donnée personnelle : ni nom d'utilisateur, ni e-mail, ni fichier ; seulement le code client et le poste.
/// </summary>
internal static class Rapport
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Ce que JVTECH doit regarder après un entretien. Vide : tout va bien.</summary>
    public static List<string> Problemes(Resultat r, Reglages reglages)
    {
        var p = new List<string>();
        if (r.Avant.ErreurWindows is { } ew) p.Add("Windows Update ne répond pas : " + ew);
        if (r.WindowsEchecs.Count > 0) p.Add($"{r.WindowsEchecs.Count} mise(s) à jour Windows en échec");
        if (r.Avant.ErreurLogiciels is { } el) p.Add("Logiciels non mis à jour : " + el);
        if (r.LogicielsEchecs.Count > 0) p.Add($"{r.LogicielsEchecs.Count} mise(s) à jour de logiciel en échec");
        p.AddRange(ProblemesEtat(r.Apres, reglages, redemarrage: false));
        if (r.Avant.RedemarrageNecessaire) p.Add("Le PC attendait déjà un redémarrage avant l'entretien : il n'a pas été redémarré depuis une installation");
        return p;
    }

    /// <summary>Points à voir dans l'état du PC (disque, antivirus, redémarrage).</summary>
    public static List<string> ProblemesEtat(Etat e, Reglages reglages, bool redemarrage = true)
    {
        var p = new List<string>();
        if (e.DisqueLibrePourcent is { } libre && libre < reglages.DisqueLibreAlerte)
            p.Add($"Disque système presque plein : {libre.ToString("0", Fr)} % libres ({Go(e.DisqueLibre)})");
        if (e.Antivirus is { } av)
        {
            if (av.Count == 0) p.Add("Aucun antivirus détecté");
            else if (!av.Any(a => a.Actif)) p.Add("Antivirus désactivé : " + string.Join(", ", av.Select(a => a.Nom)));
            else if (!av.Where(a => a.Actif).Any(a => a.AJour)) p.Add("Antivirus pas à jour : " + string.Join(", ", av.Where(a => a.Actif).Select(a => a.Nom)));
        }
        if (redemarrage && e.RedemarrageNecessaire) p.Add("Redémarrage en attente");
        return p;
    }

    private static string Qui(Reglages reglages) =>
        (reglages.Client.Length > 0 ? reglages.Client + " · " : "") + reglages.Poste;

    /// <summary>Résumé en une phrase, comme « 4 mises à jour Windows à faire, 3 passées… ».</summary>
    public static string Resume(Resultat r)
    {
        var parts = new List<string>();
        parts.Add(r.Avant.MajWindows is { } w
            ? $"{w.Count} mise(s) à jour Windows à faire, {r.WindowsInstallees.Count} passée(s)"
            : "Windows Update n'a pas répondu");
        parts.Add(r.Avant.MajLogiciels is { } l
            ? $"{l.Count} mise(s) à jour logicielle(s) à faire, {r.LogicielsMisAJour.Count} passée(s)"
            : "logiciels non vérifiés (winget absent ou en échec)");
        parts.Add(r.Apres.RedemarrageNecessaire ? "redémarrage nécessaire" : "pas de redémarrage nécessaire");
        return string.Join(", ", parts) + ".";
    }

    public static string Html(Resultat r, Reglages reglages, IReadOnlyCollection<string> problemes, string version)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append(@"<!doctype html><html lang=""fr""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>Entretien du PC</title><style>
:root{--accent:#2563EB;--text:#0F172A;--muted:#64748B;--line:#E2E8F0;--soft:#F8FAFC;--ok:#16A34A;--ok-bg:#DCFCE7;--warn:#D97706;--warn-bg:#FEF3C7;--bad:#DC2626;--bad-bg:#FEE2E2}
body{margin:0;background:#E5E7EB;font:10.5pt/1.45 'Segoe UI',Arial,sans-serif;color:var(--text)}
.page{max-width:210mm;margin:10mm auto;padding:12mm;background:#fff}
header{display:flex;justify-content:space-between;gap:16px;padding-bottom:10px;border-bottom:3px solid var(--accent)}
h1{margin:0;font-size:15pt} .meta{text-align:right;font-size:9pt;color:var(--muted)}
h2{font-size:10pt;text-transform:uppercase;letter-spacing:.06em;color:var(--accent);margin:16px 0 6px}
table{width:100%;border-collapse:collapse;font-size:9.5pt}
th{text-align:left;font-size:8.5pt;color:var(--muted);font-weight:600;border-bottom:2px solid var(--line);padding:5px 6px}
td{border-bottom:1px solid var(--line);padding:5px 6px;vertical-align:top} td.num{text-align:right;white-space:nowrap}
.pill{display:inline-block;padding:1px 8px;border-radius:999px;font-size:8.5pt;font-weight:600}
.ok{background:var(--ok-bg);color:var(--ok)}.warn{background:var(--warn-bg);color:var(--warn)}.bad{background:var(--bad-bg);color:var(--bad)}
ul{margin:4px 0;padding-left:20px} .resume{background:var(--soft);padding:8px 12px;border-left:4px solid var(--accent)}
footer{margin-top:18px;color:var(--muted);font-size:8pt;text-align:center;border-top:1px solid var(--line);padding-top:6px}
</style></head><body><div class=""page"">");
        sb.Append($@"<header><div><h1>Entretien du PC</h1><div>{E(Qui(reglages))}</div></div>
<div class=""meta""><strong>{r.Avant.Date.ToString("dd/MM/yyyy HH:mm", Fr)}</strong><br>Durée : {Duree(r.Duree)}</div></header>");
        sb.Append($"<p class=\"resume\">{E(Resume(r))}</p>");

        sb.Append("<h2>Points à voir</h2>");
        if (problemes.Count == 0) sb.Append("<p><span class=\"pill ok\">Rien à signaler</span></p>");
        else sb.Append("<ul>" + string.Concat(problemes.Select(p => $"<li>{E(p)}</li>")) + "</ul>");

        sb.Append("<h2>Avant / Après</h2><table><thead><tr><th>Point contrôlé</th><th class=\"num\">Avant</th><th class=\"num\">Après</th><th>Résultat</th></tr></thead><tbody>");
        Ligne(sb, "Mises à jour Windows en attente", r.Avant.MajWindows?.Count, r.Apres.MajWindows?.Count);
        Ligne(sb, "Logiciels à mettre à jour (liste sûre)", r.Avant.MajLogiciels?.Count, r.Apres.MajLogiciels?.Count);
        sb.Append($"<tr><td>Espace libre sur le disque système</td><td class=\"num\">{E(Go(r.Avant.DisqueLibre))}</td><td class=\"num\">{E(Go(r.Apres.DisqueLibre))}</td><td>{Pill(r.Apres.DisqueLibrePourcent is { } d ? (d >= reglages.DisqueLibreAlerte ? "ok" : "bad") : "warn", r.Apres.DisqueLibrePourcent is { } pc ? pc.ToString("0", Fr) + " % libres" : "non lu")}</td></tr>");
        sb.Append($"<tr><td>Antivirus</td><td class=\"num\">{E(Av(r.Avant.Antivirus))}</td><td class=\"num\">{E(Av(r.Apres.Antivirus))}</td><td></td></tr>");
        sb.Append($"<tr><td>Redémarrage nécessaire</td><td class=\"num\">{(r.Avant.RedemarrageNecessaire ? "oui" : "non")}</td><td class=\"num\">{(r.Apres.RedemarrageNecessaire ? "oui" : "non")}</td><td>{(r.Apres.RedemarrageNecessaire ? Pill("warn", "à faire par l'utilisateur") : "")}</td></tr>");
        sb.Append("</tbody></table>");

        sb.Append("<h2>Ce qui a été fait</h2>");
        Liste(sb, "Mises à jour Windows installées", r.WindowsInstallees);
        Liste(sb, "Mises à jour Windows en échec", r.WindowsEchecs);
        Liste(sb, "Logiciels mis à jour", r.LogicielsMisAJour);
        Liste(sb, "Logiciels en échec", r.LogicielsEchecs);
        if (r.Apres.AutresLogiciels is > 0)
            sb.Append($"<p>{r.Apres.AutresLogiciels} autre(s) logiciel(s) ont une mise à jour, volontairement non installée (hors de la liste sûre).</p>");
        if (r.WindowsInstallees.Count + r.WindowsEchecs.Count + r.LogicielsMisAJour.Count + r.LogicielsEchecs.Count == 0)
            sb.Append("<p>Rien à installer.</p>");

        sb.Append($"<footer>JVTECH Entretien {E(version)} · aucun redémarrage ni message à l'utilisateur · journal détaillé sur le PC : C:\\ProgramData\\JVTECH\\Entretien\\entretien.log</footer>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    /// <summary>En-tête du tableau de suivi (CSV, séparateur ; pour Excel en français).</summary>
    public const string EnteteCsv =
        "Date;Client;Poste;MAJ Windows avant;MAJ Windows installées;MAJ Windows en échec;MAJ Windows après;" +
        "Logiciels avant;Logiciels mis à jour;Logiciels en échec;Logiciels après;Autres logiciels non touchés;" +
        "Disque libre avant (Go);Disque libre après (Go);Antivirus;Redémarrage nécessaire;Points à voir";

    public static string LigneCsv(Resultat r, Reglages reglages, IReadOnlyCollection<string> problemes)
    {
        string N(int? n) => n?.ToString(CultureInfo.InvariantCulture) ?? "";
        string G(long? octets) => octets is { } o ? (o / 1e9).ToString("0.0", Fr) : "";
        var champs = new[]
        {
            r.Avant.Date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), reglages.Client, reglages.Poste,
            N(r.Avant.MajWindows?.Count), N(r.WindowsInstallees.Count), N(r.WindowsEchecs.Count), N(r.Apres.MajWindows?.Count),
            N(r.Avant.MajLogiciels?.Count), N(r.LogicielsMisAJour.Count), N(r.LogicielsEchecs.Count), N(r.Apres.MajLogiciels?.Count), N(r.Apres.AutresLogiciels),
            G(r.Avant.DisqueLibre), G(r.Apres.DisqueLibre), Av(r.Apres.Antivirus), r.Apres.RedemarrageNecessaire ? "oui" : "non",
            problemes.Count == 0 ? "OK" : string.Join(" | ", problemes),
        };
        return string.Join(";", champs.Select(Csv));
    }

    internal static string Csv(string valeur)
    {
        valeur = valeur.Replace("\r", " ").Replace("\n", " ");
        // Une valeur qui commence par = + - @ serait lue comme une formule par Excel.
        if (valeur.Length > 0 && valeur[0] is '=' or '+' or '-' or '@') valeur = "'" + valeur;
        return valeur.IndexOfAny(new[] { ';', '"' }) >= 0 ? "\"" + valeur.Replace("\"", "\"\"") + "\"" : valeur;
    }

    private static void Ligne(StringBuilder sb, string nom, int? avant, int? apres)
    {
        var resultat = apres switch
        {
            null => Pill("warn", "non vérifié"),
            0 => Pill("ok", "À jour"),
            var n => Pill("warn", $"{n} restante(s)"),
        };
        sb.Append($"<tr><td>{WebUtility.HtmlEncode(nom)}</td><td class=\"num\">{avant?.ToString() ?? "?"}</td><td class=\"num\">{apres?.ToString() ?? "?"}</td><td>{resultat}</td></tr>");
    }

    private static void Liste(StringBuilder sb, string titre, IReadOnlyCollection<string> elements)
    {
        if (elements.Count == 0) return;
        sb.Append($"<p><strong>{WebUtility.HtmlEncode(titre)}</strong></p><ul>");
        foreach (var e in elements) sb.Append($"<li>{WebUtility.HtmlEncode(e)}</li>");
        sb.Append("</ul>");
    }

    private static string Pill(string classe, string texte) => $"<span class=\"pill {classe}\">{WebUtility.HtmlEncode(texte)}</span>";

    internal static string Go(long? octets) => octets is { } o ? (o / 1e9).ToString("0.0", Fr) + " Go" : "non lu";

    internal static string Av(List<Antivirus>? av) => av switch
    {
        null => "non lu",
        { Count: 0 } => "aucun",
        _ => string.Join(", ", av.Select(a => $"{a.Nom} ({(a.Actif ? "actif" : "désactivé")}{(a.Actif && !a.AJour ? ", pas à jour" : "")})")),
    };

    private static string Duree(TimeSpan d) => d.TotalMinutes < 1 ? "moins d'une minute" : $"{(int)d.TotalMinutes} min";
}
