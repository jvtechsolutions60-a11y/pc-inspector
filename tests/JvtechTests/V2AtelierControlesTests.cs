using System;
using System.IO;
using System.Linq;
using Jvtech.Atelier.Dossier;
using Jvtech.Lecture;
using Jvtech.Lecture.Regles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JvtechTests;

/// <summary>Ajustement 5 : batterie détaillée, seuils réglables, rien d'estimé.</summary>
[TestClass]
public class BatterieTests
{
    [TestMethod]
    public void Details_EtUsure()
    {
        var m = new MesureBatterie(true, 1, 57000, 41040, 312);
        Assert.AreEqual(72, m.SantePourcent);
        Assert.AreEqual(28, m.UsurePourcent);
        var d = m.Details();
        StringAssert.Contains(d[0], "57");
        StringAssert.Contains(d[2], "28 %");
        StringAssert.Contains(d[3], "312");
    }

    [TestMethod]
    public void ValeursAbsentes_NonDisponible()
    {
        var m = new MesureBatterie(true, 1, null, 41040, null);
        Assert.IsNull(m.SantePourcent);
        Assert.IsTrue(m.Details().Count(l => l.EndsWith("non disponible")) >= 3);
        var c = RegleControles.Batterie(m);
        Assert.AreEqual(Statut.AVerifier, c.Statut);
    }

    [TestMethod]
    public void NeuveAuDessusDeLaNominale_Plafonnee()
    {
        Assert.AreEqual(100, new MesureBatterie(true, 1, 50000, 52000, 3).SantePourcent);
    }

    [DataTestMethod]
    [DataRow(45000, Statut.Conforme, Portee.Information)]        // 90 %
    [DataRow(40000, Statut.Conforme, Portee.Information)]        // 80 %
    [DataRow(35500, Statut.Anomalie, Portee.ReserveCommerciale)] // 71 %
    [DataRow(25000, Statut.Anomalie, Portee.ReserveCommerciale)] // 50 %
    public void SeuilsParDefaut(int actuelle, Statut statut, Portee portee)
    {
        var c = RegleControles.Batterie(new MesureBatterie(true, 1, 50000, (ulong)actuelle, 100));
        Assert.AreEqual(statut, c.Statut);
        Assert.AreEqual(portee, c.Portee);
    }

    [TestMethod]
    public void MoinsDe60_RemplacementConseille()
    {
        StringAssert.Contains(RegleControles.Batterie(new MesureBatterie(true, 1, 50000, 25000, 100)).Constat, "remplacement conseillé");
        Assert.IsFalse(RegleControles.Batterie(new MesureBatterie(true, 1, 50000, 35500, 100)).Constat.Contains("remplacement"));
    }

    [TestMethod]
    public void SeuilsReglables()
    {
        var c = RegleControles.Batterie(new MesureBatterie(true, 1, 50000, 37500, 100), new SeuilsBatterie(70, 50)); // 75 %
        Assert.AreEqual(Statut.Conforme, c.Statut);
        // Seuils incohérents : valeurs par défaut.
        Assert.AreEqual(SeuilsBatterie.ParDefaut, new SeuilsBatterie(50, 70).Valides());
        Assert.AreEqual(SeuilsBatterie.ParDefaut, new SeuilsBatterie(120, 60).Valides());
    }

    [TestMethod]
    public void PcFixeOuBatterieAbsente()
    {
        Assert.AreEqual(Statut.NonApplicable, RegleControles.Batterie(new MesureBatterie(false, 0, null, null, null)).Statut);
        Assert.AreEqual(Statut.AVerifier, RegleControles.Batterie(new MesureBatterie(true, 0, null, null, null)).Statut);
    }
}

/// <summary>Ajustement 2 : contrôle rapide sans redémarrage, jamais présenté comme une garantie.</summary>
[TestClass]
public class MemoireTests
{
    private static readonly DateTime Date = new(2026, 11, 10);

    [TestMethod]
    public void Rapide_SansErreur_PasDeGarantie()
    {
        var c = RegleControles.MemoireRapide(2, null, 0);
        Assert.AreEqual(Statut.Conforme, c.Statut);
        StringAssert.Contains(c.Constat, "Aucune erreur signalée");
        StringAssert.Contains(c.Constat, "ne garantit pas");
        Assert.IsFalse(c.Constat.Contains("sans défaut") || c.Constat.Contains("saine"));
    }

    [TestMethod]
    public void Rapide_ErreursBloquent()
    {
        Assert.AreEqual(Portee.Bloquante, RegleControles.MemoireRapide(2, new DiagnosticMemoire(Date, true), 0).Portee);
        Assert.AreEqual(Statut.Anomalie, RegleControles.MemoireRapide(2, null, 3).Statut);
        Assert.AreEqual(Statut.AVerifier, RegleControles.MemoireRapide(0, null, 0).Statut);
    }

    [TestMethod]
    public void Approfondi_Distinct()
    {
        Assert.AreEqual(RegleControles.IdMemoireApprofondi, RegleControles.MemoireApprofondi(null).Id);
        Assert.AreNotEqual(RegleControles.IdMemoire, RegleControles.IdMemoireApprofondi);
        Assert.AreEqual(Statut.AVerifier, RegleControles.MemoireApprofondi(null).Statut);
        Assert.AreEqual(Statut.Conforme, RegleControles.MemoireApprofondi(new DiagnosticMemoire(Date, false)).Statut);
        Assert.AreEqual(Statut.Anomalie, RegleControles.MemoireApprofondi(new DiagnosticMemoire(Date, true)).Statut);
    }
}

/// <summary>Disques : « contrôle impossible » n'est jamais « sain ».</summary>
[TestClass]
public class DisquesTests
{
    private static EtatDisque Sain(string nom) => new(nom, true, Array.Empty<string>(), Array.Empty<string>());

    [TestMethod]
    public void TousLus_Conforme() => Assert.AreEqual(Statut.Conforme, RegleControles.Disques(new[] { Sain("SSD"), Sain("HDD") }).Statut);

    [TestMethod]
    public void Critique_Bloque()
    {
        var c = RegleControles.Disques(new[] { Sain("SSD"), new EtatDisque("HDD", true, new[] { "a 12 secteurs en attente" }, Array.Empty<string>()) });
        Assert.AreEqual(Statut.Anomalie, c.Statut);
        Assert.AreEqual(Portee.Bloquante, c.Portee);
        StringAssert.Contains(c.Constat, "HDD : a 12 secteurs");
    }

    [TestMethod]
    public void NonLuOuAvertissement_AVerifier()
    {
        Assert.AreEqual(Statut.AVerifier, RegleControles.Disques(new[] { new EtatDisque("NVMe", false, Array.Empty<string>(), Array.Empty<string>(), "pilote RAID") }).Statut);
        Assert.AreEqual(Statut.AVerifier, RegleControles.Disques(new[] { new EtatDisque("HDD", true, Array.Empty<string>(), new[] { "1 secteur réalloué" }) }).Statut);
        Assert.AreEqual(Statut.AVerifier, RegleControles.Disques(Array.Empty<EtatDisque>()).Statut);
    }
}

/// <summary>Tests manuels enregistrés et statut avant vente de bout en bout.</summary>
[TestClass]
public class TestsManuelsTests
{
    private static readonly DateTime Le = new(2026, 11, 13, 11, 0, 0);

    [TestMethod]
    public void Applicables_SelonLeTypeDePc()
    {
        Assert.IsTrue(TestsManuels.Applicables(true).Any(t => t.Id == "pave-tactile"));
        Assert.IsFalse(TestsManuels.Applicables(false).Any(t => t.Id == "pave-tactile" || t.Id == "chargeur"));
        CollectionAssert.Contains(TestsManuels.Obligatoires(false).ToList(), "test-clavier");
    }

    [TestMethod]
    public void EnControle()
    {
        var clavier = TestsManuels.Liste.First(t => t.Id == "clavier");
        Assert.AreEqual(Statut.AVerifier, TestsManuels.EnControle(clavier, null).Statut);
        var reussi = TestsManuels.EnControle(clavier, new ResultatTestManuel("clavier", ResultatTest.Reussi, Portee.Bloquante, null, "Julien", Le));
        Assert.AreEqual(Statut.Conforme, reussi.Statut);
        StringAssert.Contains(reussi.Constat, "Julien");
        var reserve = TestsManuels.EnControle(clavier, new ResultatTestManuel("clavier", ResultatTest.Echec, Portee.ReserveCommerciale, "touche E lustrée", "Julien", Le));
        Assert.AreEqual(Portee.ReserveCommerciale, reserve.Portee);
        var bloque = TestsManuels.EnControle(clavier, new ResultatTestManuel("clavier", ResultatTest.Echec, Portee.Information, null, "Julien", Le));
        Assert.AreEqual(Portee.Bloquante, bloque.Portee, "un échec sans portée bloque");
    }

    [TestMethod]
    public void StatutVente_DeBoutEnBout()
    {
        var auto = new[]
        {
            RegleControles.Disques(new[] { new EtatDisque("SSD", true, Array.Empty<string>(), Array.Empty<string>()) }),
            RegleControles.MemoireRapide(2, null, 0),
            RegleControles.Batterie(new MesureBatterie(true, 1, 50000, 35500, 200)),
        };
        var tests = TestsManuels.Applicables(true).Select(t => TestsManuels.EnControle(t,
            new ResultatTestManuel(t.Id, ResultatTest.Reussi, Portee.Bloquante, null, "J", Le))).ToList();
        var bilan = RegleVente.Evaluer(auto.Concat(tests), TestsManuels.Obligatoires(true));
        Assert.AreEqual(StatutVente.PretAvecReserve, bilan.Statut);

        // Un seul test manuel non fait : Incomplet, jamais Prêt.
        tests[0] = TestsManuels.EnControle(TestsManuels.Applicables(true).First(), null);
        Assert.AreEqual(StatutVente.Incomplet, RegleVente.Evaluer(auto.Concat(tests), TestsManuels.Obligatoires(true)).Statut);

        // Le diagnostic approfondi non fait n'empêche pas la vente (il n'est pas obligatoire).
        var avecApprofondi = auto.Append(RegleControles.MemoireApprofondi(null));
        var tousOk = TestsManuels.Applicables(true).Select(t => TestsManuels.EnControle(t,
            new ResultatTestManuel(t.Id, ResultatTest.Reussi, Portee.Bloquante, null, "J", Le)));
        Assert.AreEqual(StatutVente.PretAvecReserve, RegleVente.Evaluer(avecApprofondi.Concat(tousOk), TestsManuels.Obligatoires(true)).Statut);
    }

    [TestMethod]
    public void Dossier_AllerRetourDesTests()
    {
        var racine = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jvtech-tests-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var d = DossierPc.OuvrirOuCreer(racine, "V1");
            d.EnregistrerTests(new[]
            {
                new ResultatTestManuel("clavier", ResultatTest.Echec, Portee.ReserveCommerciale, "touche E lustrée", "Julien", Le),
                new ResultatTestManuel("ecran", ResultatTest.Reussi, Portee.Bloquante, null, "Julien", Le),
            });
            var relus = d.LireTests();
            Assert.AreEqual(ResultatTest.Echec, relus["clavier"].Resultat);
            Assert.AreEqual(Portee.ReserveCommerciale, relus["clavier"].PorteeEchec);
            Assert.AreEqual("touche E lustrée", relus["clavier"].Remarque);
            Assert.AreEqual(Le, relus["ecran"].Le);
            Assert.IsFalse(relus.ContainsKey("webcam"));
        }
        finally { Directory.Delete(racine, true); }
    }

    [TestMethod]
    public void Reglages_SeuilsEtTechnicien()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jvtech-reglages-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllLines(Path.Combine(dir, DossierPc.FichierReglages), new[]
            {
                "Technicien=Julien ; nom sur les fiches", "BatterieSansMention=75", "BatterieReserve=55",
            });
            var r = ReglagesAtelier.Lire(dir);
            Assert.AreEqual("Julien", r.Technicien);
            Assert.AreEqual(new SeuilsBatterie(75, 55), r.Seuils);
            Assert.AreEqual(Path.Combine(dir, "Atelier"), r.Racine(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
