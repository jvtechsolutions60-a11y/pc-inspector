using System;
using System.Collections.Generic;
using System.Linq;
using Jvtech.Lecture;
using Jvtech.Lecture.Regles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JvtechTests;

/// <summary>Fiches et annonce : V… en tête, rien d'inventé, réserves écrites, pas de lien ni de prix.</summary>
[TestClass]
public class AnnonceTests
{
    private static readonly DateTime Le = new(2026, 11, 13, 11, 0, 0);

    private static List<Champ> Champs(bool complet = true)
    {
        var l = new List<Champ>
        {
            new("Fabricant", "LENOVO") { Id = "fabricant" },
            new("Modèle", "ThinkPad T480") { Id = "modele" },
            new("Processeur", "Intel(R) Core(TM) i5-8350U CPU @ 1.70GHz") { Id = "processeur" },
            new("Mémoire", "16 Go DDR4 (2 barrettes)") { Id = "memoire" },
            new("Stockage", complet ? "SSD NVMe 256 Go (SAMSUNG MZVLB256)" : null) { Id = "stockage" },
            new("Carte graphique", "Intel(R) UHD Graphics 620") { Id = "graphique" },
            new("Écran", null) { Id = "ecran" },
            new("Windows", "Windows 11 Professionnel (24H2, build 26100)") { Id = "windows" },
        };
        return l;
    }

    private static DonneesFiche Donnees(string? inventaire = "V1042", List<Champ>? champs = null, string? remarques = null)
    {
        var batterie = new MesureBatterie(true, 1, 50000, 35500, 312);
        var controles = new List<Controle>
        {
            RegleControles.Disques(new[] { new EtatDisque("SSD", true, Array.Empty<string>(), Array.Empty<string>()) }),
            RegleControles.MemoireRapide(2, null, 0),
            RegleControles.Batterie(batterie),
        };
        controles.AddRange(TestsManuels.Applicables(true).Select(t => TestsManuels.EnControle(t,
            t.Id == "clavier"
                ? new ResultatTestManuel(t.Id, ResultatTest.Echec, Portee.ReserveCommerciale, "touche E lustrée", "Julien", Le)
                : new ResultatTestManuel(t.Id, ResultatTest.Reussi, Portee.Bloquante, null, "Julien", Le))));
        var bilan = RegleVente.Evaluer(controles, TestsManuels.Obligatoires(true));
        return new DonneesFiche(inventaire, champs ?? Champs(), controles, bilan, batterie, "Très bon état", remarques);
    }

    [TestMethod]
    public void Titre_VEnTete()
    {
        Assert.AreEqual("V1042 · Lenovo ThinkPad T480 · i5-8350U · 16 Go · SSD 256 Go", Annonce.Titre(Donnees()));
    }

    [TestMethod]
    public void Titre_ValeurInconnueOmise_JamaisNonDetecte()
    {
        var t = Annonce.Composer(Donnees(champs: Champs(complet: false)));
        Assert.AreEqual("V1042 · Lenovo ThinkPad T480 · i5-8350U · 16 Go", t.Titre);
        Assert.IsFalse(t.Description.Contains(Champ.NonDetecte));
        Assert.IsFalse(t.Description.Contains("Écran"), "écran non détecté : pas de ligne inventée");
    }

    [TestMethod]
    public void Correction_UtiliseeDansLAnnonce()
    {
        var champs = Champs();
        champs.Single(c => c.Id == "ecran").Corriger("14 pouces Full HD (1920 × 1080)", "Julien", Le);
        var t = Annonce.Composer(Donnees(champs: champs));
        StringAssert.Contains(t.Description, "- Écran : 14 pouces Full HD (1920 × 1080)");
    }

    [TestMethod]
    public void Reserves_Ecrites_SansPreuveInterne()
    {
        var t = Annonce.Composer(Donnees());
        StringAssert.Contains(t.Description, "À savoir :");
        StringAssert.Contains(t.Description, "- Clavier : touche E lustrée");
        StringAssert.Contains(t.Description, "- Batterie : 71 % de la capacité d'origine : autonomie réduite");
        Assert.IsFalse(t.Description.Contains("Julien"), "la preuve interne reste dans la fiche interne");
        StringAssert.Contains(t.Description, "- Batterie : 71 % de la capacité d'origine, 312 cycles");
    }

    [TestMethod]
    public void PasDeLienNiDePrix()
    {
        var t = Annonce.Composer(Donnees(remarques: "Voir aussi https://www.leboncoin.fr/x et www.exemple.fr. Livraison possible."));
        Assert.IsFalse(t.Description.Contains("http") || t.Description.Contains("www."));
        StringAssert.Contains(t.Description, "Livraison possible.");
        Assert.IsTrue(t.Avertissements.Any(a => a.Contains("Lien retiré")));
        Assert.IsFalse(t.Description.Contains("€"));
    }

    [TestMethod]
    public void Avertissements()
    {
        var sansNumero = Annonce.Composer(Donnees(inventaire: null));
        Assert.IsTrue(sansNumero.Avertissements.Any(a => a.Contains("V…")));
        Assert.IsFalse(sansNumero.Titre.StartsWith("V"));
        Assert.AreEqual(0, Annonce.Composer(Donnees()).Avertissements.Count);
    }

    [TestMethod]
    public void TitreTropLong_RaccourciSansPerdreNumeroNiModele()
    {
        var champs = Champs();
        champs.Single(c => c.Id == "modele").Corriger("ThinkPad T480 édition très spéciale avec un nom vraiment interminable pour le test", "J", Le);
        var titre = Annonce.Titre(Donnees(champs: champs));
        StringAssert.StartsWith(titre, "V1042 · Lenovo ThinkPad T480 édition");
        Assert.IsFalse(titre.Contains("SSD"));
    }

    [DataTestMethod]
    [DataRow("Intel(R) Core(TM) i5-8350U CPU @ 1.70GHz", "i5-8350U")]
    [DataRow("13th Gen Intel(R) Core(TM) i7-1355U", "i7-1355U")]
    [DataRow("AMD Ryzen 5 3500U with Radeon Vega Mobile Gfx", "Ryzen 5 3500U")]
    [DataRow("AMD Ryzen 7 5800X 8-Core Processor", "Ryzen 7 5800X")]
    [DataRow("Intel(R) Celeron(R) N4020 CPU @ 1.10GHz", "Intel Celeron N4020")]
    public void ProcesseurCourt(string nom, string attendu) => Assert.AreEqual(attendu, Annonce.ProcesseurCourt(nom));

    [DataTestMethod]
    [DataRow("Dell Inc.", "Latitude 5490", "Dell Latitude 5490")]
    [DataRow("HP", "HP EliteBook 840 G5", "HP EliteBook 840 G5")]
    [DataRow("Hewlett-Packard", "ProBook 650 G1", "HP ProBook 650 G1")]
    [DataRow("ASUSTeK COMPUTER INC.", "VivoBook 15", "ASUS VivoBook 15")]
    public void NomPc(string fabricant, string modele, string attendu) => Assert.AreEqual(attendu, Annonce.NomPc(fabricant, modele));

    [TestMethod]
    public void StockageCourt() =>
        Assert.AreEqual("SSD 256 Go + HDD 1 To", Annonce.StockageCourt("SSD NVMe 256 Go (SAMSUNG) + HDD SATA 1 To (WDC)"));

    [TestMethod]
    public void FicheInterne_GardeDetecteEtPreuves()
    {
        var champs = Champs();
        champs.Single(c => c.Id == "modele").Corriger("ThinkPad T480s", "Julien", Le);
        var fiche = Annonce.FicheInterne(Donnees(champs: champs), "Windows en mode audit", Le);
        StringAssert.Contains(fiche, "ThinkPad T480s (corrigé par Julien, détecté : ThinkPad T480)");
        StringAssert.Contains(fiche, "Prêt avec réserve");
        StringAssert.Contains(fiche, "touche E lustrée (Julien");
        StringAssert.Contains(fiche, "Capacité nominale");
    }
}
