using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Jvtech.Lecture.Regles;

/// <summary>
/// Rapport de préparation d'un PC (cahier AT-13) : une page HTML autonome (pas de script, pas de ressource
/// externe), à ouvrir dans le navigateur et à imprimer en PDF si besoin. Toutes les valeurs sont échappées.
/// Contenu : identité (valeur retenue et détectée), statut avant vente et ses trois listes, batterie,
/// contrôles et tests avec leur preuve, journal des actions.
/// </summary>
public static class Rapport
{
    public static string Html(DonneesFiche d, string modeWindows, string version, DateTime quand, IEnumerable<string>? journal = null)
    {
        var inventaire = IdentifiantAtelier.NormaliserInventaire(d.Inventaire) ?? "sans numéro d'inventaire";
        var nom = Annonce.NomPc(d.Champs.FirstOrDefault(c => c.Id == "fabricant")?.Valeur, d.Champs.FirstOrDefault(c => c.Id == "modele")?.Valeur) ?? Champ.NonDetecte;
        var (classe, couleur) = d.Bilan.Statut switch
        {
            StatutVente.Pret => ("ok", "#14532D"),
            StatutVente.PretAvecReserve => ("res", "#78350F"),
            StatutVente.Bloque => ("ko", "#7F1D1D"),
            _ => ("res", "#78350F"),
        };

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"fr\"><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>Rapport {E(inventaire)} · {E(nom)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:'Segoe UI',Arial,sans-serif;color:#0F172A;background:#F1F5F9;margin:0;padding:24px}");
        sb.AppendLine("main{max-width:960px;margin:0 auto;background:#fff;border:1px solid #E2E8F0;border-radius:10px;padding:24px 28px}");
        sb.AppendLine("h1{font-size:22px;margin:0 0 4px}h2{font-size:15px;text-transform:uppercase;letter-spacing:.04em;color:#475569;margin:24px 0 8px}");
        sb.AppendLine("p{margin:4px 0;font-size:14px}.petit{color:#475569;font-size:12px}");
        sb.AppendLine("table{border-collapse:collapse;width:100%;font-size:13px}td,th{border-top:1px solid #E2E8F0;padding:6px 8px;text-align:left;vertical-align:top}th{color:#475569;font-weight:600}");
        sb.AppendLine(".statut{display:inline-block;border-radius:8px;padding:8px 14px;font-weight:700;font-size:18px;margin:8px 0}");
        sb.AppendLine(".ok{background:#DCFCE7;color:#14532D}.res{background:#FEF3C7;color:#78350F}.ko{background:#FEE2E2;color:#7F1D1D}.na{background:#E2E8F0;color:#334155}");
        sb.AppendLine(".pastille{border-radius:999px;padding:2px 8px;font-size:12px;font-weight:600;white-space:nowrap}");
        sb.AppendLine("pre{white-space:pre-wrap;font-size:11px;background:#F8FAFC;border:1px solid #E2E8F0;border-radius:6px;padding:8px}");
        sb.AppendLine("@media print{body{background:#fff;padding:0}main{border:0}}");
        sb.AppendLine("</style></head><body><main>");

        sb.AppendLine($"<h1>{E(inventaire)} · {E(nom)}</h1>");
        sb.AppendLine($"<p class=\"petit\">Rapport JVTECH Atelier {E(version)} · {quand:dd/MM/yyyy HH:mm} · {E(modeWindows)}</p>");

        sb.AppendLine("<h2>Statut avant vente</h2>");
        sb.AppendLine($"<div class=\"statut {classe}\" style=\"color:{couleur}\">{E(d.Bilan.Libelle)}</div>");
        foreach (var b in d.Bilan.Bloquants) sb.AppendLine($"<p>✕ Bloquant : <b>{E(b.Titre)}</b> : {E(b.Constat)}</p>");
        foreach (var m in d.Bilan.Manquants) sb.AppendLine($"<p>○ Non fait ou à vérifier : {E(m)}</p>");
        foreach (var r in d.Bilan.Reserves) sb.AppendLine($"<p>? Réserve commerciale : <b>{E(r.Titre)}</b> : {E(r.Constat)}</p>");

        sb.AppendLine("<h2>Identification</h2>");
        sb.AppendLine("<table><tr><th>Information</th><th>Valeur retenue</th><th>Détectée sur le PC</th><th>Source</th></tr>");
        foreach (var c in d.Champs)
        {
            var corrige = c.EstCorrige ? $"<br><span class=\"petit\">corrigée par {E(c.CorrigePar)} le {c.CorrigeLe:dd/MM/yyyy HH:mm}</span>" : "";
            sb.AppendLine($"<tr><td>{E(c.Nom)}</td><td>{E(c.Affichage)}{corrige}</td><td>{E(c.Detecte ?? Champ.NonDetecte)}</td><td class=\"petit\">{E(c.Source)}</td></tr>");
        }
        sb.AppendLine("</table>");

        if (d.Batterie is { NombreBatteries: > 0 } bat)
        {
            sb.AppendLine("<h2>Batterie</h2>");
            foreach (var l in bat.Details()) sb.AppendLine($"<p>{E(l)}</p>");
        }

        sb.AppendLine("<h2>Contrôles et tests</h2>");
        sb.AppendLine("<table><tr><th>Résultat</th><th>Contrôle</th><th>Constat et preuve</th></tr>");
        foreach (var c in d.Controles)
        {
            var pastille = c.Statut switch
            {
                Statut.Conforme => "ok",
                Statut.Anomalie => c.Portee == Portee.ReserveCommerciale ? "res" : "ko",
                Statut.NonApplicable => "na",
                _ => "res",
            };
            var libelle = c.Statut == Statut.Anomalie ? $"{Libelles.De(c.Statut)}, {Libelles.De(c.Portee)}" : Libelles.De(c.Statut);
            var source = c.Source is null ? "" : $"<br><span class=\"petit\">{E(c.Source)}</span>";
            sb.AppendLine($"<tr><td><span class=\"pastille {pastille}\">{E(libelle)}</span></td><td>{E(c.Titre)}</td><td>{E(c.Constat)}{source}</td></tr>");
        }
        sb.AppendLine("</table>");
        sb.AppendLine("<p class=\"petit\">Les contrôles rapides ne garantissent pas l'absence de défaut. « À vérifier » signifie que le contrôle n'a pas été fait ou n'a pas permis de conclure.</p>");

        var lignes = (journal ?? Enumerable.Empty<string>()).ToList();
        if (lignes.Count > 0)
        {
            sb.AppendLine("<h2>Journal des actions</h2>");
            sb.AppendLine("<pre>" + E(string.Join("\n", lignes)) + "</pre>");
        }
        sb.AppendLine("</main></body></html>");
        return sb.ToString();
    }

    /// <summary>Échappe &amp; &lt; &gt; et les guillemets ; les lettres accentuées restent lisibles (fichier en UTF-8).</summary>
    public static string E(string? texte) => (texte ?? "")
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
}
