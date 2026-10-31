using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.Win32;
using PcInspector.Collectors;

namespace PcInspector.Report;

/// <summary>
/// Étiquette au format carte de visite (85 × 55 mm) à imprimer et coller sous le PC : configuration,
/// date d'intervention et coordonnées du technicien.
/// </summary>
public static class LabelWriter
{
    private const ulong Gb = 1024UL * 1024 * 1024;

    public static void Write(string path, ReportInfo info)
    {
        var html = Html(info);
        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) PdfPrinter.FromHtml(html, path);
        else File.WriteAllText(path, html, new UTF8Encoding(true));
    }

    public static string Html(ReportInfo info)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

        var system = Wmi.Query("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault();
        var serial = Wmi.QueryOptional("SELECT SerialNumber FROM Win32_BIOS").FirstOrDefault()?.Str("SerialNumber");
        var cpu = Wmi.Query("SELECT Name FROM Win32_Processor").FirstOrDefault()?.Str("Name");
        var os = Wmi.Query("SELECT Caption FROM Win32_OperatingSystem").FirstOrDefault()?.Str("Caption");
        var ram = system?.ULong("TotalPhysicalMemory") is { } bytes ? $"{Math.Round(bytes / (double)Gb)} Go" : null;
        var disks = Wmi.QueryOptional("SELECT FriendlyName, Size, MediaType FROM MSFT_PhysicalDisk", Wmi.StorageScope)
            .Select(d => $"{(d.ULong("MediaType") == 3 ? "HDD" : "SSD")} {Math.Round((d.ULong("Size") ?? 0) / 1e9)} Go")
            .ToList();

        var rows = new List<(string, string?)>
        {
            ("Modèle", MachineIdentity.Model()),
            ("N° de série", serial),
            ("Processeur", Shorten(cpu)),
            ("Mémoire", ram),
            ("Disque", string.Join(" + ", disks)),
            ("Windows", $"{os?.Replace("Microsoft ", "")} {DisplayVersion()}".Trim()),
            ("Intervention", info.Date.ToString("dd/MM/yyyy") + (string.IsNullOrWhiteSpace(info.JobNumber) ? "" : " · n° " + info.JobNumber)),
            ("Prochain entretien", info.Date.AddYears(1).ToString("MM/yyyy")),
        };

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>Étiquette PC</title><style>");
        sb.AppendLine(@"@page{size:A4;margin:12mm}
body{font-family:'Segoe UI',Arial,sans-serif;color:#0f172a;margin:0}
.hint{color:#64748b;font-size:12px;margin:0 0 6mm}
.label{width:85mm;height:55mm;box-sizing:border-box;border:1px dashed #94a3b8;border-radius:3mm;padding:3mm 3.5mm;display:flex;flex-direction:column}
.top{display:flex;align-items:center;gap:2.5mm;border-bottom:.4mm solid #2563eb;padding-bottom:1.5mm;margin-bottom:1.5mm}
.top img{max-height:9mm;max-width:22mm}
.top b{font-size:10pt;display:block}
.top span{font-size:7pt;color:#475569}
table{border-collapse:collapse;font-size:6.6pt;line-height:1.25}
th{text-align:left;color:#64748b;font-weight:600;padding-right:2mm;white-space:nowrap;vertical-align:top}
td{word-break:break-word}
@media print{.hint{display:none}}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<p class=\"hint\">Imprimer cette page (Ctrl+P), découper l'étiquette en suivant les pointillés et la coller sous le PC.</p>");
        sb.AppendLine("<div class=\"label\"><div class=\"top\">");
        if (ReportWriter.LogoDataUri(info.LogoPath) is { } logo) sb.AppendLine($"<img src=\"{logo}\" alt=\"\">");
        sb.AppendLine($"<div><b>{E(string.IsNullOrWhiteSpace(info.Company) ? "Fiche du PC" : info.Company)}</b>");
        if (!string.IsNullOrWhiteSpace(info.Contact)) sb.AppendLine($"<span>{E(info.Contact)}</span>");
        sb.AppendLine("</div></div><table>");
        foreach (var (label, value) in rows.Where(r => !string.IsNullOrWhiteSpace(r.Item2)))
            sb.AppendLine($"<tr><th>{E(label)}</th><td>{E(value)}</td></tr>");
        sb.AppendLine("</table></div></body></html>");
        return sb.ToString();
    }

    /// <summary>« Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz » devient « Intel Core i5-8250U ».</summary>
    internal static string? Shorten(string? cpu) => cpu?
        .Replace("(R)", "").Replace("(TM)", "").Replace(" CPU", "").Split('@')[0]
        .Replace("  ", " ").Trim();

    private static string? DisplayVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("DisplayVersion") as string;
        }
        catch (System.Security.SecurityException) { return null; }
    }
}
