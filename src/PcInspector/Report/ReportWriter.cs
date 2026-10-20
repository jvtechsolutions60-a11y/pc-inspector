using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using PcInspector.Models;

namespace PcInspector.Report;

/// <summary>Écrit le rapport complet en HTML (à imprimer ou joindre à un devis) ou en texte brut.</summary>
public static class ReportWriter
{
    public static void Write(string path, ReportInfo info, IReadOnlyList<InfoSection> sections)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            PdfPrinter.FromHtml(Html(info, sections), path);
            return;
        }
        var content = extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            ? Text(info, sections)
            : Html(info, sections);
        File.WriteAllText(path, content, new UTF8Encoding(true));
    }

    public static string Text(ReportInfo info, IReadOnlyList<InfoSection> sections)
    {
        var sb = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(info.Company) ? $"Rapport {AppInfo.Product}" : $"Rapport {info.Company}";
        sb.AppendLine(title).AppendLine(new string('=', title.Length));
        AppendTextLine(sb, "Date", info.Date.ToString("dd/MM/yyyy HH:mm"));
        AppendTextLine(sb, "Client", info.Client);
        AppendTextLine(sb, "Intervention n°", info.JobNumber);
        AppendTextLine(sb, "Technicien", info.Technician);
        AppendTextLine(sb, "Contact", info.Contact);
        if (!string.IsNullOrWhiteSpace(info.Notes)) sb.AppendLine().AppendLine("Notes :").AppendLine(info.Notes.Trim());

        foreach (var section in sections)
        {
            sb.AppendLine().AppendLine().AppendLine(section.Title.ToUpperInvariant()).AppendLine(new string('-', section.Title.Length));
            if (section.Error is not null) sb.AppendLine(section.Error);
            foreach (var group in section.Groups)
            {
                sb.AppendLine().AppendLine("  " + group.Title);
                var width = group.Items.Count == 0 ? 0 : group.Items.Max(i => i.Label.Length);
                foreach (var item in group.Items)
                    sb.AppendLine($"    {item.Label.PadRight(width)}  {item.Value.Replace("\n", "\n      " + new string(' ', width))}" + (IsWebLink(item.Url) ? $" ({item.Url})" : ""));
            }
        }
        return sb.ToString();
    }

    private static void AppendTextLine(StringBuilder sb, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"{label} : {value.Trim()}");
    }

    public static string Html(ReportInfo info, IReadOnlyList<InfoSection> sections)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>Rapport {E(info.Client)} {info.Date:dd-MM-yyyy}</title>");
        sb.AppendLine("<style>" + Css + "</style></head><body>");

        sb.AppendLine("<header>");
        if (LogoDataUri(info.LogoPath) is { } logo) sb.AppendLine($"<img class=\"logo\" src=\"{logo}\" alt=\"\">");
        sb.AppendLine("<div class=\"head\">");
        sb.AppendLine($"<h1>{(string.IsNullOrWhiteSpace(info.Company) ? "Rapport PC" : E(info.Company))}</h1>");
        sb.AppendLine($"<p class=\"sub\">{E(Subtitle)}</p></div>");
        sb.AppendLine("<table class=\"meta\">");
        void Meta(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"<tr><th>{label}</th><td>{E(value)}</td></tr>");
        }
        Meta("Date", info.Date.ToString("dd/MM/yyyy HH:mm"));
        Meta("Client", info.Client);
        Meta("Intervention n°", info.JobNumber);
        Meta("Technicien", info.Technician);
        Meta("Contact", info.Contact);
        sb.AppendLine("</table></header>");

        if (!string.IsNullOrWhiteSpace(info.Notes))
            sb.AppendLine($"<section class=\"notes\"><h2>Notes</h2><p>{E(info.Notes.Trim()).Replace("\n", "<br>")}</p></section>");

        foreach (var section in sections)
        {
            sb.AppendLine($"<section><h2>{E(section.Title)}</h2>");
            if (section.Error is not null) sb.AppendLine($"<p class=\"error\">{E(section.Error)}</p>");
            foreach (var group in section.Groups)
            {
                sb.AppendLine($"<div class=\"group\"><h3>{E(group.Title)}</h3><table>");
                foreach (var item in group.Items)
                {
                    var value = !IsWebLink(item.Url) ? E(item.Value).Replace("\n", "<br>") : $"<a href=\"{E(item.Url)}\">{E(item.Value)}</a>";
                    sb.AppendLine($"<tr><th>{E(item.Label)}</th><td{StatusClass(item.Value)}>{value}</td></tr>");
                }
                sb.AppendLine("</table></div>");
            }
            sb.AppendLine("</section>");
        }

        sb.AppendLine($"<footer>Généré par {E(AppInfo.Product)} {AppInfo.Version} le {info.Date:dd/MM/yyyy à HH:mm}</footer>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string Subtitle => AppInfo.Product switch
    {
        "PC Maintenance" => "Rapport de maintenance",
        "PC Sécurité" => "Bilan de sécurité du PC",
        _ => "Rapport d'analyse du PC",
    };

    /// <summary>Les liens internes du logiciel (jvtech:…) ne servent à rien dans un rapport client.</summary>
    private static bool IsWebLink(string? url) => url is not null && url.StartsWith("http", StringComparison.OrdinalIgnoreCase);

    /// <summary>Colore les résultats du bilan et des vérifications (✘ rouge, ! orange, ✔ vert).</summary>
    private static string StatusClass(string value) =>
        value.StartsWith("✘ ", StringComparison.Ordinal) ? " class=\"bad\""
        : value.StartsWith("! ", StringComparison.Ordinal) ? " class=\"warn\""
        : value.StartsWith("✔ ", StringComparison.Ordinal) ? " class=\"ok\""
        : "";

    internal static string? LogoDataUri(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var mime = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".svg" => "image/svg+xml",
                _ => null,
            };
            return mime is null ? null : $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private const string Css = @"
body{font-family:'Segoe UI',Arial,sans-serif;color:#1f2937;margin:32px auto;max-width:900px;padding:0 16px;font-size:14px}
header{display:flex;align-items:center;gap:20px;border-bottom:3px solid #3d8bfd;padding-bottom:16px;margin-bottom:24px;flex-wrap:wrap}
.logo{max-height:70px;max-width:200px}
.head{flex:1}
h1{margin:0;font-size:24px}
.sub{margin:4px 0 0;color:#6b7280}
.meta th{text-align:left;color:#6b7280;font-weight:normal;padding:2px 12px 2px 0}
h2{font-size:18px;color:#3d8bfd;border-bottom:1px solid #e5e7eb;padding-bottom:4px;margin-top:28px}
h3{font-size:14px;margin:14px 0 6px}
.group table{border-collapse:collapse;width:100%}
.group th{text-align:left;font-weight:normal;color:#6b7280;width:220px;padding:3px 8px 3px 0;vertical-align:top}
.group td{padding:3px 0}
.group tr+tr th,.group tr+tr td{border-top:1px solid #f3f4f6}
.notes p{background:#f5f6f8;padding:12px;border-radius:6px}
.error{color:#b91c1c}
td.bad{color:#b91c1c;font-weight:600}
td.warn{color:#b45309}
td.ok{color:#15803d}
footer{margin-top:40px;color:#9ca3af;font-size:12px;text-align:center}
section,.group{break-inside:avoid-page}
@media print{body{margin:0}a{color:inherit;text-decoration:none}}
";
}
