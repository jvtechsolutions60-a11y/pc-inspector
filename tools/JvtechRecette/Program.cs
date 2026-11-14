using System;
using System.IO;
using System.Linq;
using System.Text;
using Jvtech.Lecture;
using Jvtech.Lecture.Collecte;
using Jvtech.Lecture.Regles;

// Recette automatique sur une vraie machine Windows (jetable) : lectures seulement.
var sortie = args.Length > 0 ? args[0] : "recette";
Directory.CreateDirectory(sortie);
var utf8 = new UTF8Encoding(true);
var code = 0;

void Ecrire(string nom, string texte) => File.WriteAllText(Path.Combine(sortie, nom), texte, utf8);

try
{
    var sb = new StringBuilder();
    sb.AppendLine($"Mode de Windows : {Installation.Libelle(Installation.Lire())}");
    sb.AppendLine($"Administrateur : {LectureAudit.EstAdministrateur()}");
    sb.AppendLine();
    foreach (var c in Inventaire.Lire()) sb.AppendLine($"{c.Nom} : {c.Affichage}   [{c.Source}]");
    Ecrire("atelier-inventaire.txt", sb.ToString());

    sb.Clear();
    var portable = LectureControles.EstPortable();
    var memoire = LectureControles.DernierDiagnosticMemoire();
    foreach (var c in new[]
    {
        RegleControles.Disques(LectureControles.Disques()),
        RegleControles.MemoireRapide(LectureControles.NombreBarrettes(), memoire, LectureControles.ErreursMemoireMaterielles()),
        RegleControles.MemoireApprofondi(memoire),
        RegleControles.Batterie(LectureControles.Batterie(portable)),
        PlanPreparation.Pilotes(LectureControles.PeripheriquesEnErreur()),
    })
        sb.AppendLine($"[{Libelles.De(c.Statut)}] {c.Titre} : {c.Constat}");
    Ecrire("atelier-controles.txt", sb.ToString());
}
catch (Exception ex)
{
    Ecrire("atelier-erreur.txt", ex.ToString());
    code = 1;
}

try
{
    var d = LectureAudit.Lire(t => Console.WriteLine("Audit : " + t));
    var r = RegleAudit.Evaluer(d, BaseWindows11.Integree);
    var sb = new StringBuilder();
    sb.AppendLine($"Durée : {d.Duree?.TotalSeconds:0.0} s · administrateur : {d.Administrateur} · langue : {d.LangueWindows}");
    sb.AppendLine(r.Synthese);
    sb.AppendLine();
    foreach (var b in r.Blocs)
    {
        sb.AppendLine($"== {b.Titre} [{Libelles.De(b.Statut)}] {b.Valeur} · {b.Detail}");
        foreach (var p in b.Points) sb.AppendLine($"   [{Libelles.De(p.Statut)}] {p.Texte}");
    }
    sb.AppendLine();
    sb.AppendLine("Alertes prioritaires :");
    foreach (var a in r.Prioritaires) sb.AppendLine($"   {RegleAudit.Libelle(a.Urgence)} · {a.Titre} : {a.Constat}");
    sb.AppendLine("Recommandations :");
    foreach (var c in r.Recommandations) sb.AppendLine($"   {RegleAudit.Libelle(c.Categorie)} · {c.Titre} ({RegleAudit.Libelle(c.Confiance)})");
    sb.AppendLine("Limites :");
    foreach (var l in r.Limites) sb.AppendLine("   " + l);
    Ecrire("audit-resume.txt", sb.ToString());
    Ecrire("audit-rapport.html", RapportAudit.Html(d, r, new OptionsRapportAudit(Annexe: true), "recette"));
    Ecrire("audit-rapport-anonyme.html", RapportAudit.Html(d, r, new OptionsRapportAudit(Annexe: true, Anonymiser: true), "recette"));
    Console.WriteLine(sb.ToString());
}
catch (Exception ex)
{
    Ecrire("audit-erreur.txt", ex.ToString());
    Console.WriteLine(ex);
    code = 1;
}
return code;
