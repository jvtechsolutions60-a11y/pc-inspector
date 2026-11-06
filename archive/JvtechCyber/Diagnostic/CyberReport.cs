using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using PcInspector;
using PcInspector.Report;

namespace JvtechCyber.Diagnostic;

/// <summary>Rapport client du diagnostic : note, menaces, état par thème, constat sur les postes et plan d'action chiffré.</summary>
public static class CyberReport
{
    public static void Write(string path, CyberDiagnostic diagnostic, ReportInfo info)
    {
        var html = Html(diagnostic, info);
        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) PdfPrinter.FromHtml(html, path);
        else File.WriteAllText(path, html, new UTF8Encoding(true));
    }

    public static string Html(CyberDiagnostic d, ReportInfo info)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var tarifs = Tarifs.Load();
        var previous = d.Previous();
        var score = d.Score;
        var color = score >= 80 ? "#16a34a" : score >= 55 ? "#2563eb" : score >= 30 ? "#d97706" : "#dc2626";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>Diagnostic cyber</title><style>");
        sb.AppendLine(@"@page{size:A4;margin:14mm 13mm}
body{font-family:'Segoe UI',Arial,sans-serif;color:#0f172a;font-size:10pt;margin:0;line-height:1.4}
header{display:flex;justify-content:space-between;align-items:flex-start;border-bottom:2px solid #1e3a5f;padding-bottom:8px}
header img{max-height:16mm;max-width:45mm}
h1{font-size:17pt;margin:0;color:#1e3a5f}
h2{font-size:12pt;color:#1e3a5f;margin:18px 0 6px;border-bottom:1px solid #cbd5e1;padding-bottom:2px}
.muted{color:#64748b;font-size:9pt}
.score{display:flex;gap:16px;align-items:center;margin:14px 0}
.big{font-size:30pt;font-weight:700;line-height:1}
.bar{height:10px;background:#e2e8f0;border-radius:5px;overflow:hidden;width:100%}
.bar i{display:block;height:100%}
table{width:100%;border-collapse:collapse}
th,td{text-align:left;padding:5px 6px;border-bottom:1px solid #e2e8f0;vertical-align:top}
th{font-size:8.5pt;color:#475569;text-transform:uppercase;letter-spacing:.3px}
.pill{display:inline-block;border-radius:9px;padding:1px 8px;font-size:8.5pt;font-weight:600;color:#fff;white-space:nowrap}
.Exposed{background:#dc2626}.Partial{background:#d97706}.Covered{background:#16a34a}
.dots b{display:inline-block;width:9px;height:9px;border-radius:50%;background:#e2e8f0;margin-right:2px}
.dots b.on{background:#1e3a5f}
small{color:#64748b;display:block}
.price{font-weight:600;color:#1e3a5f;white-space:nowrap}
.page{page-break-before:always}
footer{margin-top:18px;font-size:8pt;color:#94a3b8}");
        sb.AppendLine("</style></head><body>");

        sb.Append("<header><div><h1>Diagnostic de cybersécurité</h1>");
        sb.Append($"<div><b>{E(d.Company)}</b>{(d.Activity.Length > 0 ? " · " + E(d.Activity) : "")}</div>");
        sb.Append($"<div class=\"muted\">Réalisé le {d.Date:dd/MM/yyyy}{(d.ContactName.Length > 0 ? " avec " + E(d.ContactName) : "")}"
                  + $"{(d.Workstations > 0 ? $" · {d.Workstations} poste(s)" : "")}"
                  + $"{(info.Technician.Length > 0 ? " · par " + E(info.Technician) : "")}</div></div>");
        sb.Append("<div style=\"text-align:right\">");
        if (ReportWriter.LogoDataUri(info.LogoPath) is { } logo) sb.Append($"<img src=\"{logo}\" alt=\"\"><br>");
        sb.Append($"<b>{E(info.Company)}</b><br><span class=\"muted\">{E(info.Contact)}</span></div></header>");

        sb.Append($"<div class=\"score\"><div class=\"big\" style=\"color:{color}\">{score}<span style=\"font-size:13pt\">/100</span></div><div style=\"flex:1\">");
        sb.Append($"<b style=\"color:{color}\">{CyberDiagnostic.Grade(score)}</b>");
        if (previous is not null)
        {
            var delta = score - previous.Score;
            sb.Append($" <span class=\"muted\">· {(delta >= 0 ? "+" : "")}{delta} points depuis le diagnostic du {previous.Date:dd/MM/yyyy} ({previous.Score}/100)</span>");
        }
        sb.Append($"<div class=\"bar\"><i style=\"width:{score}%;background:{color}\"></i></div></div></div>");

        sb.AppendLine("<h2>Menaces principales</h2><table><tr><th>Menace</th><th>Exemple</th><th>Protection</th></tr>");
        foreach (var threat in Referentiel.Threats)
        {
            var exposure = d.ExposureOf(threat);
            var label = exposure switch { Exposure.Exposed => "Exposé", Exposure.Partial => "Partielle", _ => "Correcte" };
            sb.AppendLine($"<tr><td><b>{E(threat.Name)}</b></td><td class=\"muted\">{E(threat.Example)}</td><td><span class=\"pill {exposure}\">{label}</span></td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine("<h2>État par thème</h2><table><tr><th style=\"width:24%\">Thème</th><th style=\"width:11%\">Niveau</th><th>Situation constatée</th></tr>");
        foreach (var theme in Referentiel.Themes)
        {
            var level = d.Level(theme.Id);
            var dots = string.Concat(Enumerable.Range(1, 3).Select(i => i <= level ? "<b class=\"on\"></b>" : "<b></b>"));
            sb.Append($"<tr><td><b>{E(theme.Name)}</b></td><td class=\"dots\">{dots} {level}/3</td><td>{E(theme.Levels[level].Situation)}");
            if (d.Remarks.TryGetValue(theme.Id, out var remark) && remark.Trim().Length > 0) sb.Append($"<small>{E(remark)}</small>");
            if (PosteResult.Finding(theme, d.Postes) is { } finding) sb.Append($"<small>{E(finding)}</small>");
            sb.AppendLine("</td></tr>");
        }
        sb.AppendLine("</table>");

        var plan = d.Plan().ToList();
        sb.AppendLine("<h2 class=\"page\">Plan d'action conseillé</h2>");
        if (plan.Count == 0) sb.AppendLine("<p>Tous les thèmes sont au meilleur niveau : à revoir chaque année.</p>");
        else
        {
            sb.AppendLine("<p class=\"muted\">Par ordre de priorité. Coûts et durées indicatifs pour l'entreprise ; les prestations JVTECH sont chiffrées à part.</p>");
            sb.AppendLine("<table><tr><th>#</th><th>Action</th><th>Coût indicatif</th><th>Durée</th><th>Prestation JVTECH</th></tr>");
            var i = 1;
            foreach (var (theme, next) in plan)
            {
                var price = next.Jvtech ? (tarifs.TryGetValue($"{theme.Id}.{next.Value}", out var p) ? p : "Sur devis") : "—";
                sb.AppendLine($"<tr><td>{i++}</td><td><b>{E(theme.Name)}</b> → niveau {next.Value}<br>{E(next.Action)}</td>"
                              + $"<td>{E(next.Cost)}</td><td>{E(next.Duration)}</td><td class=\"price\">{E(price)}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        if (d.Postes.Count > 0)
        {
            sb.AppendLine("<h2>Postes contrôlés</h2><table><tr><th>Poste</th><th>Windows</th><th>Points à corriger</th></tr>");
            foreach (var poste in d.Postes.OrderByDescending(p => p.Problems))
            {
                var problems = poste.Checks.Where(c => c.IsProblem).Select(c => c.Name).ToList();
                sb.AppendLine($"<tr><td><b>{E(poste.Name)}</b>{(poste.Date is { } date ? $"<small>{date:dd/MM/yyyy}</small>" : "")}</td><td>{E(poste.Windows)}</td>"
                              + $"<td>{(problems.Count == 0 ? "Aucun" : E(string.Join(", ", problems)))}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        if (d.Notes.Trim().Length > 0) sb.AppendLine($"<h2>Remarques</h2><p>{E(d.Notes).Replace("\n", "<br>")}</p>");

        sb.AppendLine("<h2>En cas d'incident</h2><p>Débrancher le poste du réseau sans l'éteindre, prévenir " + E(info.Company.Length > 0 ? info.Company : "votre prestataire")
                      + (info.Contact.Length > 0 ? " (" + E(info.Contact) + ")" : "")
                      + ", et signaler l'attaque sur <b>cybermalveillance.gouv.fr</b> (ou au 17Cyber). En cas de fraude bancaire, appeler la banque immédiatement.</p>");
        sb.AppendLine($"<footer>Diagnostic réalisé avec JVTECH Diagnostic Cyber {E(AppInfo.Version)}, d'après les recommandations de l'ANSSI et de cybermalveillance.gouv.fr. "
                      + "Ce diagnostic donne une photographie à une date et ne remplace pas un audit de sécurité complet.</footer>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}
