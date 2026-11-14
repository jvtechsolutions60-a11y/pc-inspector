using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jvtech.Atelier.Actions.Finaliser;
using Jvtech.Atelier.Dossier;
using Jvtech.Lecture;
using Jvtech.Lecture.Regles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JvtechTests;

/// <summary>
/// Lot 1e : finalisation (Sysprep) en mode audit seulement (ajustement 1), statut Prêt ou Prêt avec réserve
/// (ajustement 3), fiche à jour enregistrée, confirmation par le numéro d'inventaire.
/// Ces tests vérifient la décision ; le lancement réel de Sysprep reste à valider sur un PC de test (T26).
/// </summary>
[TestClass]
public class FinalisationTests
{
    private static Controle Ok(string id) => new(id, id, Statut.Conforme, "ok");

    private static BilanVente Bilan(StatutVente statut) => statut switch
    {
        StatutVente.Pret => RegleVente.Evaluer(new[] { Ok("disque") }, new[] { "disque" }),
        StatutVente.PretAvecReserve => RegleVente.Evaluer(new[] { Ok("disque"),
            new Controle("batterie", "Batterie", Statut.Anomalie, "71 %") { Portee = Portee.ReserveCommerciale } }, new[] { "disque" }),
        StatutVente.Bloque => RegleVente.Evaluer(new[] { new Controle("disque", "Disque", Statut.Anomalie, "SMART") { Portee = Portee.Bloquante } }, new[] { "disque" }),
        _ => RegleVente.Evaluer(Array.Empty<Controle>(), new[] { "disque" }),
    };

    [DataTestMethod]
    [DataRow(StatutVente.Pret)]
    [DataRow(StatutVente.PretAvecReserve)]
    public void ToutesConditions_Permise(StatutVente statut)
    {
        var b = Bilan(statut);
        Assert.AreEqual(statut, b.Statut);
        var d = Finalisation.Decider(ModeWindows.Audit, b, "V1042", b.Empreinte, " v1042 ");
        Assert.IsTrue(d.Permise, string.Join(" ; ", d.Raisons));
    }

    [DataTestMethod]
    [DataRow(ModeWindows.Normal)]
    [DataRow(ModeWindows.Inconnu)]
    public void HorsModeAudit_JamaisPermise(ModeWindows mode)
    {
        var b = Bilan(StatutVente.Pret);
        var d = Finalisation.Decider(mode, b, "V1042", b.Empreinte, "V1042");
        Assert.IsFalse(d.Permise);
        Assert.IsTrue(d.Raisons.Any(r => r.Contains("mode audit")));
    }

    [DataTestMethod]
    [DataRow(StatutVente.Incomplet)]
    [DataRow(StatutVente.Bloque)]
    public void StatutNonPret_Refuse(StatutVente statut)
    {
        var b = Bilan(statut);
        Assert.AreEqual(statut, b.Statut);
        Assert.IsFalse(Finalisation.Decider(ModeWindows.Audit, b, "V1042", b.Empreinte, "V1042").Permise);
    }

    [TestMethod]
    public void FicheAbsenteOuPerimee_Refuse()
    {
        var b = Bilan(StatutVente.PretAvecReserve);
        Assert.IsFalse(Finalisation.Decider(ModeWindows.Audit, b, "V1042", null, "V1042").Permise);
        // Fiche enregistrée quand le PC était « Prêt » sans réserve : l'annonce ne mentionne pas la batterie.
        var ancienne = Bilan(StatutVente.Pret).Empreinte;
        var d = Finalisation.Decider(ModeWindows.Audit, b, "V1042", ancienne, "V1042");
        Assert.IsFalse(d.Permise);
        Assert.IsTrue(d.Raisons.Any(r => r.Contains("enregistrer à nouveau")));
    }

    [DataTestMethod]
    [DataRow((string?)null)]
    [DataRow("")]
    [DataRow("oui")]
    [DataRow("V1043")]
    [DataRow("1042")]
    public void ConfirmationIncorrecte_Refuse(string? saisie)
    {
        var b = Bilan(StatutVente.Pret);
        Assert.IsFalse(Finalisation.Decider(ModeWindows.Audit, b, "V1042", b.Empreinte, saisie).Permise);
    }

    [TestMethod]
    public void SansNumeroDInventaire_Refuse()
    {
        var b = Bilan(StatutVente.Pret);
        Assert.IsFalse(Finalisation.Decider(ModeWindows.Audit, b, null, b.Empreinte, "V1042").Permise);
        Assert.IsFalse(Finalisation.Decider(ModeWindows.Audit, b, "AT-20261113-AB12", b.Empreinte, "AT-20261113-AB12").Permise);
    }

    [TestMethod]
    public void Empreinte_StatutEtReserves()
    {
        Assert.AreEqual("Prêt", Bilan(StatutVente.Pret).Empreinte);
        Assert.AreEqual("Prêt avec réserve : batterie", Bilan(StatutVente.PretAvecReserve).Empreinte);
    }

    [TestMethod]
    public void Dossier_EmpreinteAllerRetour()
    {
        var racine = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jvtech-fin-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var d = DossierPc.OuvrirOuCreer(racine, "V9");
            Assert.IsNull(d.LireEmpreinteFiche());
            d.EnregistrerEmpreinteFiche("Prêt avec réserve : batterie, test-clavier", new DateTime(2026, 11, 13, 12, 0, 0));
            Assert.AreEqual("Prêt avec réserve : batterie, test-clavier", d.LireEmpreinteFiche());
        }
        finally { Directory.Delete(racine, true); }
    }

    private const string Officiel = @"C:\Windows\System32\Sysprep\sysprep.exe";

    [DataTestMethod]
    [DataRow(Officiel, "\"C:\\Windows\\System32\\Sysprep\\sysprep.exe\"", true)]
    [DataRow(Officiel, Officiel, true)]
    [DataRow(Officiel, "\"C:\\Windows\\System32\\Sysprep\\sysprep.exe\" /oobe /shutdown /quiet", false)]
    [DataRow(Officiel, Officiel + " /generalize", false)]
    [DataRow(@"C:\Users\x\Downloads\sysprep.exe", @"C:\Users\x\Downloads\sysprep.exe", false)]
    [DataRow("", "", false)]
    public void FenetreDuModeAudit_SeuleFermee(string chemin, string ligne, bool attendu)
    {
        Assert.AreEqual(attendu, SysprepOobe.EstFenetreAudit(chemin, ligne, Officiel));
    }
}

/// <summary>Sysprep n'est lancé qu'à un seul endroit, qui relit lui-même le mode audit.</summary>
[TestClass]
public class SysprepUnSeulEndroitTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PcInspector.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Racine du dépôt introuvable");
    }

    [TestMethod]
    public void SeulSysprepOobeLanceSysprep()
    {
        var root = Root();
        var fichiers = new[] { "JvtechAtelier", "JvtechAtelier.Actions", "JvtechLecture", "JvtechAudit" }
            .SelectMany(p => Directory.GetFiles(Path.Combine(root, "src", p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
        var lanceurs = fichiers.Where(f => File.ReadAllText(f).Contains("/oobe")).Select(Path.GetFileName).ToList();
        CollectionAssert.AreEquivalent(new List<string?> { "SysprepOobe.cs" }, lanceurs);

        var code = File.ReadAllText(Path.Combine(root, "src", "JvtechAtelier.Actions", "Finaliser", "SysprepOobe.cs"));
        var execution = code.Substring(code.IndexOf("public SortieAction Executer", StringComparison.Ordinal));
        Assert.IsTrue(execution.IndexOf("FinalisationPermise", StringComparison.Ordinal) < execution.IndexOf("Process.Start", StringComparison.Ordinal),
            "le mode audit doit être relu avant le lancement de Sysprep");
    }
}
