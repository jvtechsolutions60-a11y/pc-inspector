using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jvtech.Atelier.Dossier;
using Jvtech.Lecture;
using Jvtech.Lecture.Collecte;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JvtechTests;

/// <summary>JVTECH Atelier, lot 1a : mode de Windows, finalisation, identifiant du PC.</summary>
[TestClass]
public class InstallationTests
{
    [DataTestMethod]
    [DataRow(1, "IMAGE_STATE_UNDEPLOYABLE", ModeWindows.Audit)]
    [DataRow(1, "IMAGE_STATE_SPECIALIZE_RESEAL_TO_AUDIT", ModeWindows.Audit)]
    [DataRow(1, (string?)null, ModeWindows.Inconnu)]
    [DataRow(1, "IMAGE_STATE_COMPLETE", ModeWindows.Inconnu)]
    [DataRow(0, "IMAGE_STATE_COMPLETE", ModeWindows.Normal)]
    [DataRow(null, "IMAGE_STATE_COMPLETE", ModeWindows.Normal)]
    [DataRow(null, "IMAGE_STATE_UNDEPLOYABLE", ModeWindows.Inconnu)]
    [DataRow(null, (string?)null, ModeWindows.Inconnu)]
    public void Classer(int? audit, string? state, ModeWindows attendu) => Assert.AreEqual(attendu, Installation.Classer(audit, state));

    [TestMethod]
    public void Sysprep_SeulementEnModeAudit()
    {
        Assert.IsTrue(Installation.FinalisationPermise(ModeWindows.Audit));
        Assert.IsFalse(Installation.FinalisationPermise(ModeWindows.Normal));
        Assert.IsFalse(Installation.FinalisationPermise(ModeWindows.Inconnu));
    }
}

[TestClass]
public class IdentifiantAtelierTests
{
    private static readonly DateTime Quand = new(2026, 11, 13, 10, 42, 0);

    [DataTestMethod]
    [DataRow("V1042", "V1042")]
    [DataRow(" v 1042 ", "V1042")]
    [DataRow("V-77", "V77")]
    [DataRow("1042", (string?)null)]
    [DataRow("VX12", (string?)null)]
    [DataRow("", (string?)null)]
    public void Normaliser(string saisie, string? attendu) => Assert.AreEqual(attendu, IdentifiantAtelier.NormaliserInventaire(saisie));

    [TestMethod]
    public void Calculer()
    {
        Assert.AreEqual("V1042", IdentifiantAtelier.Calculer("v1042", "PF1ABCDE", Quand));
        Assert.AreEqual("AT-20261113-BCDE", IdentifiantAtelier.Calculer(null, "PF1-ABCDE", Quand));
        Assert.AreEqual("AT-20261113-1042", IdentifiantAtelier.Calculer("", "To Be Filled By O.E.M.", Quand));
    }

    [TestMethod]
    public void Valide()
    {
        Assert.IsTrue(IdentifiantAtelier.InventaireValide(""));
        Assert.IsTrue(IdentifiantAtelier.InventaireValide("V12"));
        Assert.IsFalse(IdentifiantAtelier.InventaireValide("12"));
    }
}

/// <summary>Mise en forme de l'inventaire : rien n'est deviné.</summary>
[TestClass]
public class InventaireTests
{
    [TestMethod]
    public void Lenovo_NomCommercialDansVersion()
    {
        Assert.AreEqual(("ThinkPad T480", "20L5CTO1WW"), Inventaire.ChoisirModele("LENOVO", "20L5CTO1WW", "ThinkPad T480", "LENOVO_MT_20L5"));
        Assert.AreEqual(("20L5CTO1WW", "LENOVO_MT_20L5"), Inventaire.ChoisirModele("LENOVO", "20L5CTO1WW", "Lenovo", "LENOVO_MT_20L5"));
        Assert.AreEqual(("Latitude 5490", null), Inventaire.ChoisirModele("Dell Inc.", "Latitude 5490", "", null));
        Assert.AreEqual(((string?)null, (string?)null), Inventaire.ChoisirModele("System manufacturer", "System Product Name", null, "Default string"));
    }

    [TestMethod]
    public void Memoire()
    {
        Assert.AreEqual("16 Go DDR4 (2 barrettes)", Inventaire.FormatMemoire(17179869184UL, 2, "DDR4"));
        Assert.AreEqual("8 Go (1 barrette)", Inventaire.FormatMemoire(8589934592UL, 1, null));
        Assert.IsNull(Inventaire.FormatMemoire(0, 0, "DDR4"));
    }

    [TestMethod]
    public void Disques()
    {
        Assert.AreEqual("SSD NVMe 256 Go (SAMSUNG MZVLB256)", Inventaire.FormatDisque(4, 17, 256060514304UL, "SAMSUNG MZVLB256"));
        Assert.AreEqual("HDD SATA 1 To", Inventaire.FormatDisque(3, 11, 1000204886016UL, null));
        Assert.AreEqual("Disque 500 Go", Inventaire.FormatDisque(0, 8, 500107862016UL, ""));
        Assert.IsNull(Inventaire.FormatDisque(4, 7, 64000000000UL, "Clé USB"), "la clé USB ne fait pas partie du PC");
    }

    [TestMethod]
    public void Ecran_Approche()
    {
        Assert.AreEqual("environ 15,3 pouces, 1920 × 1080", Inventaire.FormatEcran(34, 19, 1920, 1080));
        Assert.AreEqual("1366 × 768", Inventaire.FormatEcran(0, 0, 1366, 768));
        Assert.IsNull(Inventaire.FormatEcran(null, null, null, null));
    }

    [TestMethod]
    public void Windows()
    {
        Assert.AreEqual("Windows 11 Professionnel (24H2, build 26100)", Inventaire.FormatWindows("Microsoft Windows 11 Professionnel", "24H2", "26100"));
        Assert.AreEqual("Windows 10 Famille", Inventaire.FormatWindows("Microsoft Windows 10 Famille", null, ""));
        Assert.IsNull(Inventaire.FormatWindows(null, "24H2", "26100"));
    }
}

/// <summary>Dossier du PC : corrections gardées avec la valeur détectée, pas d'écrasement entre deux PC.</summary>
[TestClass]
public class DossierPcTests
{
    private string _racine = "";

    [TestInitialize]
    public void Init() => _racine = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jvtech-atelier-" + Guid.NewGuid().ToString("N"))).FullName;

    [TestCleanup]
    public void Clean()
    {
        try { Directory.Delete(_racine, true); } catch (IOException) { }
    }

    private static List<Champ> Champs(string serie) => new()
    {
        new Champ("Modèle", "20L5CTO1WW", "Win32_ComputerSystem") { Id = "modele" },
        new Champ("N° de série", serie, "Win32_BIOS") { Id = "serie" },
        new Champ("Écran", null) { Id = "ecran" },
    };

    [TestMethod]
    public void AllerRetour_GardeDetecteEtCorrection()
    {
        var champs = Champs("PF1ABCDE");
        champs[0].Corriger("ThinkPad T480", "Julien", new DateTime(2026, 11, 13, 10, 0, 0));
        champs[2].Corriger("14 pouces = 35,6 cm", "Julien", new DateTime(2026, 11, 13, 10, 1, 0));
        var dossier = DossierPc.OuvrirOuCreer(_racine, "AT-20261113-BCDE");
        dossier.EnregistrerInventaire(null, champs);

        var relus = Champs("PF1ABCDE");
        var (inventaire, corrections) = DossierPc.Trouver(_racine, "PF1ABCDE")!.LireInventaire();
        DossierPc.AppliquerCorrections(relus, corrections);
        Assert.IsNull(inventaire);
        Assert.AreEqual("ThinkPad T480", relus[0].Valeur);
        Assert.AreEqual("20L5CTO1WW", relus[0].Detecte);
        Assert.AreEqual("Julien", relus[0].CorrigePar);
        Assert.AreEqual(new DateTime(2026, 11, 13, 10, 0, 0), relus[0].CorrigeLe);
        Assert.AreEqual("14 pouces = 35,6 cm", relus[2].Valeur);
        Assert.IsFalse(relus[1].EstCorrige);
    }

    [TestMethod]
    public void Trouver_ParNumeroDeSerie()
    {
        DossierPc.OuvrirOuCreer(_racine, "AT-1").EnregistrerInventaire(null, Champs("AAA111"));
        DossierPc.OuvrirOuCreer(_racine, "AT-2").EnregistrerInventaire(null, Champs("BBB222"));
        Assert.AreEqual("AT-2", DossierPc.Trouver(_racine, "bbb222")!.Identifiant);
        Assert.IsNull(DossierPc.Trouver(_racine, "CCC333"));
        Assert.IsNull(DossierPc.Trouver(_racine, "Default string"), "sans numéro lisible, rien n'est retrouvé");
    }

    [TestMethod]
    public void Renommer_RefuseUnNumeroDejaPris()
    {
        DossierPc.OuvrirOuCreer(_racine, "V1042");
        var autre = DossierPc.OuvrirOuCreer(_racine, "AT-20261113-BCDE");
        autre.Journaliser("test");
        Assert.ThrowsException<IOException>(() => autre.Renommer("V1042"));
        Assert.IsTrue(File.Exists(Path.Combine(_racine, "AT-20261113-BCDE", DossierPc.FichierJournal)), "rien n'est déplacé");

        autre.Renommer("V1043");
        Assert.AreEqual("V1043", autre.Identifiant);
        StringAssert.Contains(File.ReadAllText(Path.Combine(_racine, "V1043", DossierPc.FichierJournal)), "AT-20261113-BCDE → V1043");
    }

    [TestMethod]
    public void IdentifiantInvalide_Refuse()
    {
        Assert.ThrowsException<ArgumentException>(() => DossierPc.OuvrirOuCreer(_racine, @"..\x"));
        Assert.ThrowsException<ArgumentException>(() => DossierPc.OuvrirOuCreer(_racine, "a/b"));
    }

    [TestMethod]
    public void Racine_ParDefautOuReglee()
    {
        Assert.AreEqual(Path.Combine(_racine, "Atelier"), DossierPc.Racine(_racine));
        File.WriteAllLines(Path.Combine(_racine, DossierPc.FichierReglages), new[] { "; partage de l'atelier", "Dossiers=\\\\nas\\atelier" });
        Assert.AreEqual(@"\\nas\atelier", DossierPc.Racine(_racine));
    }

    [TestMethod]
    public void Valeur_MultiLigneEtAntislash()
    {
        var champs = new List<Champ> { new("Notes", "a") { Id = "notes" } };
        champs[0].Corriger(@"C:\chemin" + "\n" + "deuxième ligne", "J", DateTime.Now);
        var d = DossierPc.OuvrirOuCreer(_racine, "AT-X");
        d.EnregistrerInventaire("V1", champs);
        var (inv, corr) = d.LireInventaire();
        Assert.AreEqual("V1", inv);
        Assert.AreEqual(@"C:\chemin" + "\n" + "deuxième ligne", corr["notes"].Corrige);
    }
}
