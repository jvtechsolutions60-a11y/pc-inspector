using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using PcInspector.Report;

namespace PcSecurite.Checks;

/// <summary>
/// Fiche conseils à remettre au client : les points qui restent à améliorer, expliqués simplement,
/// avec le pas à pas quand il peut le faire lui-même. Rien n'est modifié sur le PC.
/// </summary>
public static class ClientGuide
{
    public static void Write(string path, ReportInfo info, IReadOnlyList<SecurityCheck> checks)
    {
        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            PdfPrinter.FromHtml(Html(info, checks), path);
            return;
        }
        var content = Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)
            ? Text(info, checks)
            : Html(info, checks);
        File.WriteAllText(path, content, new UTF8Encoding(true));
    }

    private static List<SecurityCheck> ToDo(IEnumerable<SecurityCheck> checks) =>
        checks.Where(c => c.NeedsAction).OrderBy(c => c.GroupOrder).ThenBy(c => c.Severity).ThenBy(c => c.Order).ToList();

    private static string Technician(ReportInfo info) =>
        string.IsNullOrWhiteSpace(info.Company) ? "votre technicien" : info.Company;

    private static string Intro(int count) => count == 0
        ? "Bonne nouvelle : votre ordinateur est bien protégé. Pensez simplement à laisser Windows faire ses mises à jour."
        : $"Voici {count} point(s) à améliorer sur votre ordinateur, du plus important au moins important, et comment faire.";

    public static string Html(ReportInfo info, IReadOnlyList<SecurityCheck> checks)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var todo = ToDo(checks);
        var score = SecurityScore.Compute(checks);
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>Conseils sécurité {E(info.Client)}</title><style>{Css}</style></head><body>");

        sb.AppendLine("<header>");
        if (ReportWriter.LogoDataUri(info.LogoPath) is { } logo) sb.AppendLine($"<img class=\"logo\" src=\"{logo}\" alt=\"\">");
        sb.AppendLine("<div class=\"head\"><h1>Conseils pour sécuriser votre ordinateur</h1>");
        var who = new[] { info.Client, info.Date.ToString("dd/MM/yyyy") }.Where(s => !string.IsNullOrWhiteSpace(s));
        sb.AppendLine($"<p class=\"sub\">{E(string.Join(" · ", who))}</p></div>");
        sb.AppendLine($"<div class=\"grade g{SecurityScore.Letter(score)}\">{SecurityScore.Letter(score)}</div>");
        sb.AppendLine("</header>");

        sb.AppendLine($"<p class=\"intro\">{E(Intro(todo.Count))}</p>");
        foreach (var check in todo)
        {
            var color = check.Status == CheckStatus.Danger ? "danger" : "warning";
            sb.AppendLine($"<section class=\"{color}\"><h2>{E(check.Name)}</h2>");
            sb.AppendLine($"<p><strong>Ce que nous avons vu :</strong> {E(check.Detail)}</p>");
            if (check.Advice is not null) sb.AppendLine($"<p><strong>Ce qu'il faut faire :</strong> {E(check.Advice)}</p>");
            if (check.Tutorial is { ClientCanDo: true } tutorial)
                sb.AppendLine("<ol>" + string.Concat(tutorial.Steps.Select(s => $"<li>{E(s)}</li>")) + "</ol>");
            else
                sb.AppendLine($"<p class=\"pro\">Cette opération est délicate : confiez-la à {E(Technician(info))}.</p>");
            sb.AppendLine("</section>");
        }

        var contact = new[] { info.Company, info.Technician }.Where(s => !string.IsNullOrWhiteSpace(s));
        sb.AppendLine("<footer>" + (contact.Any() ? $"Une question ? Contactez {E(string.Join(", ", contact))}." : "") + "</footer>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    public static string Text(ReportInfo info, IReadOnlyList<SecurityCheck> checks)
    {
        var todo = ToDo(checks);
        var sb = new StringBuilder();
        const string title = "Conseils pour sécuriser votre ordinateur";
        sb.AppendLine(title).AppendLine(new string('=', title.Length));
        if (!string.IsNullOrWhiteSpace(info.Client)) sb.AppendLine("Client : " + info.Client);
        sb.AppendLine("Date : " + info.Date.ToString("dd/MM/yyyy"));
        sb.AppendLine("Classe de sécurité : " + SecurityScore.Letter(SecurityScore.Compute(checks)));
        sb.AppendLine().AppendLine(Intro(todo.Count));

        var n = 0;
        foreach (var check in todo)
        {
            sb.AppendLine().AppendLine($"{++n}. {check.Name}");
            sb.AppendLine("   Ce que nous avons vu : " + check.Detail);
            if (check.Advice is not null) sb.AppendLine("   Ce qu'il faut faire : " + check.Advice);
            if (check.Tutorial is { ClientCanDo: true } tutorial)
                foreach (var step in tutorial.Numbered()) sb.AppendLine("     " + step);
            else
                sb.AppendLine($"   Cette opération est délicate : confiez-la à {Technician(info)}.");
        }

        var contact = new[] { info.Company, info.Technician }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (contact.Count > 0) sb.AppendLine().AppendLine("Une question ? Contactez " + string.Join(", ", contact) + ".");
        return sb.ToString();
    }

    private const string Css = @"
body{font-family:'Segoe UI',Arial,sans-serif;color:#1f2937;margin:32px auto;max-width:800px;padding:0 16px;font-size:15px;line-height:1.5}
header{display:flex;align-items:center;gap:20px;border-bottom:3px solid #3d8bfd;padding-bottom:16px;margin-bottom:20px}
.logo{max-height:70px;max-width:200px}
.head{flex:1}
h1{margin:0;font-size:24px}
.sub{margin:4px 0 0;color:#6b7280}
.grade{width:64px;height:64px;border-radius:12px;color:#fff;font-size:36px;font-weight:bold;display:flex;align-items:center;justify-content:center}
.gA{background:#16a34a}.gB{background:#65a30d}.gC{background:#eab308}.gD{background:#f97316}.gE{background:#dc2626}
.intro{font-size:16px}
section{border-left:5px solid;padding:4px 16px;margin:18px 0;background:#f9fafb;border-radius:4px;break-inside:avoid-page}
section.danger{border-color:#dc2626}
section.warning{border-color:#f59e0b}
h2{font-size:18px;margin:8px 0}
ol{margin:6px 0 10px;padding-left:22px}
.pro{font-style:italic;color:#6b7280}
footer{margin-top:36px;color:#6b7280;text-align:center}
@media print{body{margin:0}}
";
}
