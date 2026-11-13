using System;
using System.IO;
using System.Linq;
using Jvtech.Atelier.Actions.Preparation;
using Jvtech.Atelier.Dossier;
using Jvtech.Lecture;
using Jvtech.Lecture.Regles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JvtechTests;

/// <summary>Lot 1c : enchaînement des passes Windows Update, sans boucle de redémarrages.</summary>
[TestClass]
public class PassesWindowsUpdateTests
{
    [DataTestMethod]
    [DataRow(1, 0, 0, false, SuitePasse.Terminee)]
    [DataRow(1, 0, 0, true, SuitePasse.Redemarrer)]
    [DataRow(1, 14, 14, true, SuitePasse.Redemarrer)]
    [DataRow(1, 5, 5, false, SuitePasse.NouvellePasse)]
    [DataRow(2, 3, 0, false, SuitePasse.Bloquee)]
    [DataRow(2, 3, 0, true, SuitePasse.Bloquee)]
    [DataRow(2, 3, 2, true, SuitePasse.Redemarrer)]
    [DataRow(PlanPreparation.PassesMax, 2, 2, true, SuitePasse.ArretLimite)]
    [DataRow(PlanPreparation.PassesMax, 0, 0, true, SuitePasse.ArretLimite)]
    [DataRow(PlanPreparation.PassesMax, 0, 0, false, SuitePasse.Terminee)]
    public void Decider(int passe, int trouvees, int installees, bool redemarrage, SuitePasse attendu)
    {
        Assert.AreEqual(attendu, PlanPreparation.Decider(passe, trouvees, installees, redemarrage));
    }

    [TestMethod]
    public void JamaisPlusDePassesQueLaLimite()
    {
        // Un PC qui réclame toujours un redémarrage (clé de registre coincée) : arrêt à la limite, pas de boucle.
        for (var passe = 1; passe <= PlanPreparation.PassesMax + 3; passe++)
        {
            var suite = PlanPreparation.Decider(passe, 1, 1, true);
            if (passe < PlanPreparation.PassesMax) Assert.AreEqual(SuitePasse.Redemarrer, suite);
            else Assert.AreEqual(SuitePasse.ArretLimite, suite);
        }
    }
}

/// <summary>Contrôle « préparation » obligatoire avant vente (ajustement 3) et pilotes en réserve.</summary>
[TestClass]
public class ControlePreparationTests
{
    private static readonly DateTime Le = new(2026, 11, 13, 10, 30, 0);

    private static EtatPreparation Faite => new()
    {
        Passes = 3, MisesAJourInstallees = 21, DerniereSuite = SuitePasse.Terminee, PackReussi = true, PackDetail = "7-Zip et VLC installés",
    };

    [TestMethod]
    public void NonLancee_AVerifier()
    {
        Assert.AreEqual(Statut.AVerifier, PlanPreparation.EnControle(null).Statut);
        Assert.AreEqual(Statut.AVerifier, PlanPreparation.EnControle(new EtatPreparation()).Statut);
    }

    [TestMethod]
    public void Faite_Conforme()
    {
        var c = PlanPreparation.EnControle(Faite);
        Assert.AreEqual(Statut.Conforme, c.Statut);
        StringAssert.Contains(c.Constat, "21 mise(s) à jour");
    }

    [TestMethod]
    public void MisesAJourBloquees_OuPackEnEchec_AVerifier()
    {
        Assert.AreEqual(Statut.AVerifier, PlanPreparation.EnControle(Faite with { DerniereSuite = SuitePasse.Bloquee }).Statut);
        Assert.AreEqual(Statut.AVerifier, PlanPreparation.EnControle(Faite with { DerniereSuite = SuitePasse.Redemarrer }).Statut);
        var pack = PlanPreparation.EnControle(Faite with { PackReussi = false, PackDetail = "VLC : échec, code winget 0x8A150011" });
        Assert.AreEqual(Statut.AVerifier, pack.Statut);
        StringAssert.Contains(pack.Constat, "VLC");
    }

    [TestMethod]
    public void AcceptationExigeNomHeureEtRaison()
    {
        var bloquee = Faite with { DerniereSuite = SuitePasse.Bloquee };
        Assert.AreEqual(Statut.AVerifier, PlanPreparation.EnControle(bloquee with { AccepteePar = "Julien", AccepteeLe = Le }).Statut);
        Assert.AreEqual(Statut.AVerifier, PlanPreparation.EnControle(bloquee with { AccepteePar = "Julien", RaisonAcceptation = "KB connu" }).Statut);
        var ok = PlanPreparation.EnControle(bloquee with { AccepteePar = "Julien", AccepteeLe = Le, RaisonAcceptation = "KB5034441 en échec connu" });
        Assert.AreEqual(Statut.Conforme, ok.Statut);
        StringAssert.Contains(ok.Constat, "Julien");
        StringAssert.Contains(ok.Constat, "KB5034441");
        StringAssert.Contains(ok.Constat, "malgré");
    }

    [TestMethod]
    public void PreparationObligatoire_SansElle_JamaisPret()
    {
        var obligatoires = PlanPreparation.Obligatoires(false);
        CollectionAssert.Contains(obligatoires.ToList(), PlanPreparation.IdPreparation);
        var controles = obligatoires.Where(id => id != PlanPreparation.IdPreparation)
            .Select(id => new Controle(id, id, Statut.Conforme, "ok")).ToList();
        Assert.AreEqual(StatutVente.Incomplet, RegleVente.Evaluer(controles.Append(PlanPreparation.EnControle(null)), obligatoires).Statut);
        Assert.AreEqual(StatutVente.Pret, RegleVente.Evaluer(controles.Append(PlanPreparation.EnControle(Faite)), obligatoires).Statut);
    }

    [TestMethod]
    public void Pilotes_ManquantDevientReserve_PasBloquant()
    {
        Assert.AreEqual(Statut.AVerifier, PlanPreparation.Pilotes(null).Statut);
        Assert.AreEqual(Statut.Conforme, PlanPreparation.Pilotes(Array.Empty<string>()).Statut);
        var p = PlanPreparation.Pilotes(new[] { "Lecteur d'empreintes Synaptics (code 28)" });
        Assert.AreEqual(Statut.Anomalie, p.Statut);
        Assert.AreEqual(Portee.ReserveCommerciale, p.Portee);

        var obligatoires = PlanPreparation.Obligatoires(false);
        var controles = obligatoires.Select(id => id == PlanPreparation.IdPreparation
            ? PlanPreparation.EnControle(Faite)
            : new Controle(id, id, Statut.Conforme, "ok")).Append(p);
        Assert.AreEqual(StatutVente.PretAvecReserve, RegleVente.Evaluer(controles, obligatoires).Statut);
    }
}

/// <summary>preparation.ini : relu après un redémarrage, valeurs illisibles ignorées (jamais devinées).</summary>
[TestClass]
public class EtatPreparationTests
{
    [TestMethod]
    public void AllerRetour()
    {
        var e = new EtatPreparation
        {
            Passes = 2, MisesAJourInstallees = 17, MisesAJourEchouees = 1, DerniereSuite = SuitePasse.Redemarrer,
            DernierDetail = "16 installée(s) sur 17\r\nsuite", PackReussi = false, PackDetail = "VLC : échec",
            RepriseProgrammee = true, AccepteePar = "Julien", AccepteeLe = new DateTime(2026, 11, 13, 10, 0, 0),
            RaisonAcceptation = "test", MisAJourLe = new DateTime(2026, 11, 13, 10, 5, 0),
        };
        var relu = EtatPreparation.Lire(e.Ecrire());
        Assert.AreEqual(e with { DernierDetail = "16 installée(s) sur 17  suite" }, relu);
    }

    [TestMethod]
    public void ValeursIllisibles_ParDefaut()
    {
        var e = EtatPreparation.Lire("passes=-3\nderniere_suite=42\npack_reussi=peut-être\nreprise_programmee=oui\nmis_a_jour_le=hier\n");
        Assert.AreEqual(0, e.Passes);
        Assert.IsNull(e.DerniereSuite);
        Assert.IsNull(e.PackReussi);
        Assert.IsFalse(e.RepriseProgrammee);
        Assert.IsNull(e.MisAJourLe);
        Assert.AreEqual(new EtatPreparation(), EtatPreparation.Lire(null));
    }

    [TestMethod]
    public void Dossier_AllerRetour()
    {
        var racine = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jvtech-prep-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var d = DossierPc.OuvrirOuCreer(racine, "V7");
            Assert.IsNull(d.LirePreparation());
            var e = new EtatPreparation { Passes = 1, DerniereSuite = SuitePasse.NouvellePasse };
            d.EnregistrerPreparation(e);
            d.EnregistrerPreparation(e with { Passes = 2 });
            Assert.AreEqual(e with { Passes = 2 }, d.LirePreparation());
            Assert.IsFalse(File.Exists(Path.Combine(d.Chemin, DossierPc.FichierPreparation + ".tmp")));
        }
        finally { Directory.Delete(racine, true); }
    }
}

/// <summary>Logiciels : pack de base 7-Zip + VLC, codes winget, relance après redémarrage.</summary>
[TestClass]
public class LogicielsEtRepriseTests
{
    [TestMethod]
    public void PackDeBase_SeptZipEtVlc()
    {
        CollectionAssert.AreEquivalent(new[] { "7zip.7zip", "VideoLAN.VLC" }, PackAtelier.DeBase.Select(l => l.Id).ToList());
        Assert.AreEqual(PackAtelier.Logiciels.Count, PackAtelier.Logiciels.Select(l => l.Id).Distinct().Count());
    }

    [TestMethod]
    public void CodesWinget_Interpretation()
    {
        Assert.IsTrue(CodesWinget.Interpreter(0).Reussi);
        Assert.AreEqual((true, "déjà installé"), CodesWinget.Interpreter(CodesWinget.DejaAJour));
        Assert.AreEqual((true, "déjà installé"), CodesWinget.Interpreter(CodesWinget.DejaInstalle));
        Assert.IsFalse(CodesWinget.Interpreter(null).Reussi);
        Assert.IsFalse(CodesWinget.Interpreter(unchecked((int)0x8A150014)).Reussi);
        var autre = CodesWinget.Interpreter(1603);
        Assert.IsFalse(autre.Reussi);
        StringAssert.Contains(autre.Texte, "0x00000643");
    }

    [TestMethod]
    public void Reprise_CommandeEtArgument()
    {
        Assert.AreEqual("\"E:\\JVTECH Outils\\JvtechAtelier.exe\" /reprendre", Reprise.Commande(@"E:\JVTECH Outils\JvtechAtelier.exe"));
        Assert.ThrowsException<ArgumentException>(() => Reprise.Commande("a\" & calc"));
        Assert.ThrowsException<ArgumentException>(() => Reprise.Commande(" "));
        Assert.IsTrue(Reprise.Demandee(new[] { @"C:\x.exe", "/REPRENDRE" }));
        Assert.IsFalse(Reprise.Demandee(new[] { @"C:\x.exe" }));
    }
}
