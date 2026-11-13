using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jvtech.Lecture.Regles;

/// <summary>Options d'export choisies par le technicien.</summary>
public sealed record OptionsRapportAudit(bool Annexe = true, bool Anonymiser = false, string? Client = null, string? Technicien = null);

/// <summary>
/// Rapport de JVTECH Audit (cahier § 4.5) : synthèse client (identification, caractéristiques, alertes,
/// recommandations, limites) puis, en option, annexe technicien (tous les points, sources, versions des règles,
/// environnement). Page HTML autonome : pas de script, pas de ressource externe, valeurs échappées.
/// Jamais de mot de passe, de clé BitLocker ni de secret Wi-Fi (l'audit ne les lit pas). Anonymisation : numéro de
/// série, nom du PC, domaine, comptes et adresses IP masqués.
/// </summary>
public static class RapportAudit
{
    public const string Masque = "(masqué)";

    public static string Html(DonneesAudit d, ResultatAudit r, OptionsRapportAudit o, string version)
    {
        string E(string? t) => Rapport.E(o.Anonymiser ? Anonymiser(t, d) : t);
        var nom = Annonce.NomPc(d.Fabricant, d.Modele) ?? "PC non identifié";
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"fr\"><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>Audit {E(nom)} · {d.Date:dd/MM/yyyy}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:'Segoe UI',Arial,sans-serif;color:#0F172A;background:#F1F5F9;margin:0;padding:24px}");
        sb.AppendLine("main{max-width:960px;margin:0 auto;background:#fff;border:1px solid #E2E8F0;border-radius:10px;padding:24px 28px}");
        sb.AppendLine("h1{font-size:22px;margin:0 0 4px}h2{font-size:15px;text-transform:uppercase;letter-spacing:.04em;color:#475569;margin:22px 0 8px}h3{font-size:14px;margin:14px 0 4px}");
        sb.AppendLine("p,li{margin:4px 0;font-size:14px}.petit{color:#475569;font-size:12px}");
        sb.AppendLine("table{border-collapse:collapse;width:100%;font-size:13px}td,th{border-top:1px solid #E2E8F0;padding:6px 8px;text-align:left;vertical-align:top}th{color:#475569;font-weight:600}");
        sb.AppendLine(".pastille{border-radius:999px;padding:2px 8px;font-size:12px;font-weight:600;white-space:nowrap}");
        sb.AppendLine(".ok{background:#DCFCE7;color:#14532D}.av{background:#FEF3C7;color:#78350F}.ko{background:#FEE2E2;color:#7F1D1D}.na{background:#E2E8F0;color:#334155}");
        sb.AppendLine(".annexe{page-break-before:always;break-before:page}");
        sb.AppendLine("@media print{body{background:#fff;padding:0}main{border:0}}");
        sb.AppendLine("</style></head><body><main>");

        // ---------- Synthèse client ----------
        sb.AppendLine($"<h1>Diagnostic de votre ordinateur · {E(nom)}</h1>");
        var qui = new List<string> { $"Audit du {d.Date:dd/MM/yyyy à HH:mm}" };
        if (!string.IsNullOrWhiteSpace(o.Client)) qui.Add("Client : " + o.Client!.Trim());
        if (!string.IsNullOrWhiteSpace(o.Technicien)) qui.Add("Technicien : " + o.Technicien!.Trim());
        qui.Add("JVTECH Solutions");
        sb.AppendLine($"<p class=\"petit\">{E(string.Join(" · ", qui))}</p>");

        sb.AppendLine("<h2>Votre ordinateur</h2><table>");
        foreach (var id in new[] { "identification", "windows", "windows11", "processeur", "memoire", "stockage", "batterie" })
        {
            var b = r.Blocs.FirstOrDefault(x => x.Id == id);
            if (b is null || b.Statut == Statut.NonApplicable && id == "batterie") continue;
            var valeur = id == "identification" ? (d.Portable ? "Portable" : "PC fixe") + (d.NumeroSerie is null ? "" : " · N° de série " + d.NumeroSerie) : b.Valeur;
            sb.AppendLine($"<tr><th>{E(b.Titre)}</th><td>{E(valeur)}{(b.Detail.Length > 0 && id != "identification" ? $"<br><span class=\"petit\">{E(b.Detail)}</span>" : "")}</td><td>{Pastille(b.Statut)}</td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine("<h2>Points importants</h2>");
        var prioritaires = r.Prioritaires;
        if (prioritaires.Count == 0) sb.AppendLine("<p>Aucun point à signaler parmi les contrôles faits.</p>");
        else
        {
            sb.AppendLine("<ul>");
            foreach (var a in prioritaires) sb.AppendLine($"<li><b>{E(RegleAudit.Libelle(a.Urgence))} · {E(a.Titre)}</b> : {E(a.Constat)}</li>");
            sb.AppendLine("</ul>");
        }

        sb.AppendLine("<h2>Recommandations</h2>");
        if (r.Recommandations.Count == 0) sb.AppendLine("<p>Aucune recommandation.</p>");
        foreach (var c in r.Recommandations)
        {
            sb.AppendLine($"<h3>{E(RegleAudit.Libelle(c.Categorie))} · {E(c.Titre)} <span class=\"petit\">({E(RegleAudit.Libelle(c.Urgence))}, {E(RegleAudit.Libelle(c.Confiance))})</span></h3>");
            sb.AppendLine($"<p>Constat : {E(c.Constat)}</p><p>Solution proposée : {E(c.Solution)}</p><p class=\"petit\">Conditions : {E(c.Conditions)}</p>");
        }
        sb.AppendLine("<p class=\"petit\">Devis : à établir séparément, aucun prix n'est calculé par ce rapport.</p>");

        sb.AppendLine("<h2>Limites de ce diagnostic</h2><ul>");
        foreach (var l in r.Limites) sb.AppendLine($"<li class=\"petit\">{E(l)}</li>");
        sb.AppendLine("</ul>");

        // ---------- Annexe technicien ----------
        if (o.Annexe)
        {
            sb.AppendLine("<section class=\"annexe\">");
            sb.AppendLine("<h1>Annexe technicien</h1>");
            sb.AppendLine($"<p class=\"petit\">{E($"JVTECH Audit {version} · {d.Date:dd/MM/yyyy HH:mm} · durée {(d.Duree is { } t ? $"{t.TotalSeconds:0} s" : "?")} · " +
                $"{(d.Administrateur ? "avec" : "sans")} droits administrateur · Windows en {d.LangueWindows ?? "?"} · {r.Windows11?.Base ?? "base Windows 11 non utilisée"} · {SupportWindows.Libelle}")}</p>");
            if (!o.Anonymiser && d.NomPc is not null) sb.AppendLine($"<p class=\"petit\">Nom du PC : {E(d.NomPc)}</p>");
            foreach (var b in r.Blocs)
            {
                sb.AppendLine($"<h2>{E(b.Titre)} {Pastille(b.Statut)}</h2>");
                sb.AppendLine($"<p><b>{E(b.Valeur)}</b>{(b.Detail.Length > 0 ? " · " + E(b.Detail) : "")}</p>");
                if (b.Points.Count > 0)
                {
                    sb.AppendLine("<table>");
                    foreach (var p in b.Points) sb.AppendLine($"<tr><td>{Pastille(p.Statut)}</td><td>{E(p.Texte)}</td></tr>");
                    sb.AppendLine("</table>");
                }
                if (b.Source is not null) sb.AppendLine($"<p class=\"petit\">Source : {E(b.Source)}</p>");
            }
            if (d.Administrateurs is { Count: > 0 } admins && !o.Anonymiser)
                sb.AppendLine($"<p class=\"petit\">Administrateurs locaux : {E(string.Join(", ", admins))}</p>");
            sb.AppendLine("</section>");
        }
        sb.AppendLine("</main></body></html>");
        return sb.ToString();
    }

    private static string Pastille(Statut s)
    {
        var (classe, symbole) = s switch
        {
            Statut.Conforme => ("ok", "✓"),
            Statut.Anomalie => ("ko", "✕"),
            Statut.NonApplicable => ("na", "–"),
            _ => ("av", "?"),
        };
        return $"<span class=\"pastille {classe}\">{symbole} {Rapport.E(Libelles.De(s))}</span>";
    }

    private static readonly Regex Ipv4 = new(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", RegexOptions.CultureInvariant);

    /// <summary>Masque le numéro de série, le nom du PC, le domaine, les comptes et les adresses IP.</summary>
    public static string? Anonymiser(string? texte, DonneesAudit d)
    {
        if (string.IsNullOrEmpty(texte)) return texte;
        var t = texte!;
        var secrets = new List<string?> { d.NumeroSerie, d.NomPc, d.NomDomaine };
        if (d.Administrateurs is not null) secrets.AddRange(d.Administrateurs);
        foreach (var s in secrets.Where(s => s is { Length: >= 3 }).Distinct().OrderByDescending(s => s!.Length))
            t = Regex.Replace(t, $@"(?<![\w-]){Regex.Escape(s!)}(?![\w-])", Masque, RegexOptions.CultureInvariant);
        return Ipv4.Replace(t, Masque);
    }
}
