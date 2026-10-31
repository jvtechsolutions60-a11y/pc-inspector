using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using PcInspector.Collectors;
using PcInspector.Models;

namespace PcInspector.Report;

/// <summary>
/// Fiche de suivi PC JVTECH · JVDESTOCK (A4), pré-remplie avec ce que PC Inspector sait du PC : identification,
/// configuration, contrôles vérifiés par le bilan (cochés) et défauts constatés. Le reste se remplit à la main.
/// </summary>
public static class TrackingSheetWriter
{
    private const ulong Gb = 1024UL * 1024 * 1024;

    public static void Write(string path, ReportInfo info)
    {
        var html = Html(info, HealthCollector.Evaluate());
        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) PdfPrinter.FromHtml(html, path);
        else File.WriteAllText(path, html, new UTF8Encoding(true));
    }

    /// <summary>Case du formulaire : cochée si le contrôle est vérifié, avec ce qui a été constaté en petit.</summary>
    private sealed record Box(string Label, bool Checked = false, string? Note = null);

    public static string Html(ReportInfo info, HealthReport health)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

        var system = Wmi.Query("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault();
        var bios = Wmi.QueryOptional("SELECT SerialNumber, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
        var cpu = LabelWriter.Shorten(Wmi.Query("SELECT Name FROM Win32_Processor").FirstOrDefault()?.Str("Name"));
        var ram = system?.ULong("TotalPhysicalMemory") is { } bytes ? $"{Math.Round(bytes / (double)Gb)} Go" : null;
        var disks = string.Join(" + ", Wmi.QueryOptional("SELECT Size, MediaType FROM MSFT_PhysicalDisk", Wmi.StorageScope)
            .Select(d => $"{(d.ULong("MediaType") == 3 ? "HDD" : "SSD")} {Size(d.ULong("Size") ?? 0)}"));
        var gpus = string.Join(" + ", Wmi.Query("SELECT Name FROM Win32_VideoController")
            .Select(g => g.Str("Name")?.Replace("(R)", "").Replace("(TM)", "").Trim())
            .Where(n => !string.IsNullOrEmpty(n) && !n!.Contains("Basic") && !n.Contains("Remote")));
        var model = MachineIdentity.Model();

        // Un domaine est « vérifié » s'il a un point vert et aucun point rouge ou orange.
        bool Verified(params string[] areas) =>
            areas.All(a => health.Findings.Any(f => f.Area == a && f.Severity == Severity.Ok)
                           && !health.Findings.Any(f => f.Area == a && f.Severity != Severity.Ok));
        string? Note(params string[] areas) =>
            string.Join(" · ", health.Findings.Where(f => areas.Contains(f.Area)).Select(f => f.Message.TrimEnd('.'))) is { Length: > 0 } n ? n : null;

        var wireless = Wireless();
        var diagnostic = new[]
        {
            new Box("Démarrage / BIOS", true, bios is null ? null : $"BIOS {bios.Str("SMBIOSBIOSVersion")} du {bios.Date("ReleaseDate")}"),
            new Box("Écran / charnières"),
            new Box("Clavier / pavé tactile"),
            new Box("USB / audio / réseau", false, Verified("Périphériques") ? "Aucun périphérique en erreur (à tester)" : Note("Périphériques")),
            new Box("Wi-Fi / Bluetooth", false, wireless),
            new Box("RAM / SMART stockage", Verified("Mémoire", "Disque"), Note("Mémoire", "Disque")),
            new Box("Batterie / charge", Verified("Batterie"), Note("Batterie") ?? "Pas de batterie"),
            new Box("Ventilation / températures", false, Note("Température")),
            new Box("Défauts photographiés"),
        };
        var preparation = new[]
        {
            new Box("Nettoyage"),
            new Box("Réparation / pièces"),
            new Box("Effacement données"),
            new Box("Windows installé", Verified("Windows"), Note("Windows")),
            new Box("Activation vérifiée", Verified("Licence"), Note("Licence")),
            new Box("Pilotes / mises à jour", Verified("Périphériques") && !health.Findings.Any(f => f.Area == "Mises à jour" && f.Severity != Severity.Ok),
                Note("Mises à jour")),
            new Box("Logiciels de base"),
            new Box("Tests finaux OK"),
            new Box("PC prêt à vendre"),
        };
        var defects = health.Findings.Where(f => f.Severity != Severity.Ok).Select(f => $"{f.Area} : {f.Message}").ToList();

        string Field(string label, string? value, string blank = "__________________") =>
            $"{E(label)} : " + (string.IsNullOrWhiteSpace(value) ? blank : $"<span class=\"v\">{E(value)}</span>");
        string Grid(IEnumerable<Box> boxes)
        {
            var sb = new StringBuilder("<table class=\"g\">");
            foreach (var row in boxes.Select((b, i) => (b, i)).GroupBy(x => x.i / 3))
            {
                sb.Append("<tr>");
                foreach (var (b, _) in row)
                    sb.Append($"<td>{(b.Checked ? "<span class=\"v\">☑</span>" : "☐")} {E(b.Label)}" +
                              (b.Note is null ? "" : $"<small>{E(b.Note)}</small>") + "</td>");
                sb.Append("</tr>");
            }
            return sb.Append("</table>").ToString();
        }

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>Fiche de suivi PC</title><style>");
        html.AppendLine(@"@page{size:A4;margin:12mm 12mm}
body{font-family:'Segoe UI',Arial,sans-serif;color:#0f172a;font-size:9.5pt;margin:0}
h1{font-size:15pt;margin:0;letter-spacing:.2px}
.flow{font-size:8.5pt;color:#334155;margin:3px 0 0}
.stamp{float:right;font-size:7.5pt;color:#64748b;text-align:right}
.stamp img{max-height:11mm;max-width:35mm;display:block;margin:0 0 2px auto}
h2{font-size:10.5pt;color:#1e5a7a;margin:12px 0 4px}
table{width:100%;border-collapse:collapse;table-layout:fixed}
td{border:1px solid #1f2937;padding:4px 6px;vertical-align:top;height:15px}
.v{color:#1d4ed8;font-weight:600}
small{display:block;color:#475569;font-size:7pt;margin-top:1px;line-height:1.2}
.line{margin:6px 0 0;line-height:1.5}
.blank{border-bottom:1px solid #64748b;height:16px;margin:4px 0}
.foot{font-size:7pt;color:#94a3b8;margin-top:10px}");
        html.AppendLine("</style></head><body>");

        html.Append("<div class=\"stamp\">");
        if (ReportWriter.LogoDataUri(info.LogoPath) is { } logo) html.Append($"<img src=\"{logo}\" alt=\"\">");
        html.AppendLine($"Extrait par PC Inspector {E(AppInfo.Version)}<br>le {info.Date:dd/MM/yyyy HH:mm}</div>");
        html.AppendLine("<h1>FICHE DE SUIVI PC &nbsp;|&nbsp; JVTECH SOLUTIONS · JVDESTOCK</h1>");
        html.AppendLine("<p class=\"flow\">Réception → Diagnostic → Préparation → Publication → Vente</p>");

        html.AppendLine("<h2>1&nbsp; IDENTIFICATION / RÉCEPTION</h2><table>");
        html.AppendLine($"<tr><td>{Field("Réf. stock", info.JobNumber)}</td><td>{Field("Date entrée", info.Date.ToString("dd/MM/yyyy"))}</td><td>{Field("Responsable", info.Technician)}</td></tr>");
        html.AppendLine($"<tr><td>{Field("Marque / modèle", model)}</td><td>{Field("N° série", MachineIdentity.Serial())}</td><td>{Field("Origine", info.Client)}</td></tr>");
        html.AppendLine($"<tr><td>{Field("CPU", cpu)}<br>{Field("RAM", ram)}</td><td>{Field("SSD / HDD", disks)}</td><td>{Field("GPU", gpus)}</td></tr>");
        html.AppendLine("<tr><td>Accessoires : ☐ Chargeur ☐ Câble ☐ Autre : ________</td><td>État : ☐ Très bon ☐ Bon ☐ Moyen ☐ HS</td><td></td></tr></table>");

        html.AppendLine("<h2>2&nbsp; DIAGNOSTIC / ÉTAT INITIAL</h2>" + Grid(diagnostic));
        html.AppendLine("<p class=\"line\">Défauts constatés : " + (defects.Count == 0
            ? "<span class=\"v\">aucun relevé par le bilan</span> ____________________________________________"
            : "<span class=\"v\">" + string.Join(" · ", defects.Select(E)) + "</span>") + "</p>");

        html.AppendLine("<h2>3&nbsp; PRÉPARATION / CONTRÔLE FINAL</h2>" + Grid(preparation));
        html.AppendLine("<p class=\"line\">Interventions / pièces : ____________________________________________________________________</p>");

        html.AppendLine("<h2>4&nbsp; PUBLICATION / STOCK / VENTE</h2><table>");
        html.AppendLine("<tr><td>☐ Photos prises</td><td>☐ Annonce Leboncoin</td><td>☐ Publication Shopify</td></tr>");
        html.AppendLine("<tr><td>☐ Étiqueté / stocké</td><td>Emplacement : ____________</td><td>Prix affiché : __________ €</td></tr>");
        html.AppendLine("<tr><td>Date publication : __________</td><td>Date vente : ____________</td><td>Prix vendu : ___________ €</td></tr>");
        html.AppendLine("<tr><td>Canal : ☐ LBC ☐ Site ☐ Direct</td><td>Commande / facture : _________</td><td>☐ Expédié ☐ Remis en main propre</td></tr></table>");

        html.AppendLine("<h2>5&nbsp; OBSERVATIONS / VALIDATION</h2>");
        if (!string.IsNullOrWhiteSpace(info.Notes)) html.AppendLine($"<p class=\"line v\">{E(info.Notes)}</p>");
        html.AppendLine("<div class=\"blank\"></div><div class=\"blank\"></div>");
        html.AppendLine("<p class=\"line\">Date contrôle final : ______________ &nbsp;&nbsp; Validé par : ____________________ &nbsp;&nbsp; Signature : __________________</p>");
        html.AppendLine($"<p class=\"foot\">En bleu : relevé automatiquement par PC Inspector (bilan {health.Score}/100, {E(health.Counts)}). Les cases ☑ sont vérifiées par le logiciel ; les autres se contrôlent à la main.</p>");
        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static string Size(ulong bytes) => bytes >= 1_000_000_000_000 ? $"{Math.Round(bytes / 1e12, 1)} To" : $"{Math.Round(bytes / 1e9)} Go";

    /// <summary>Cartes Wi-Fi et Bluetooth présentes (le fonctionnement reste à tester).</summary>
    private static string? Wireless()
    {
        var adapters = Wmi.QueryOptional("SELECT Name FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE")
            .Select(a => a.Str("Name") ?? "").ToList();
        var wifi = adapters.Any(n => n.IndexOf("Wi-Fi", StringComparison.OrdinalIgnoreCase) >= 0
                                     || n.IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0
                                     || n.IndexOf("WLAN", StringComparison.OrdinalIgnoreCase) >= 0
                                     || n.Contains("802.11"));
        var bluetooth = Wmi.QueryOptional("SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth'").Count > 0;
        if (!wifi && !bluetooth) return "Ni Wi-Fi ni Bluetooth détecté";
        return string.Join(" · ", new[] { wifi ? "Wi-Fi présent" : "Pas de Wi-Fi", bluetooth ? "Bluetooth présent" : "Pas de Bluetooth" }) + " (à tester)";
    }
}
