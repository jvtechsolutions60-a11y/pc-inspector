using System;
using System.Collections.Generic;
using System.Linq;
using Jvtech.Lecture;
using Jvtech.Lecture.Regles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JvtechTests;

/// <summary>Lot 1f : rapport HTML autonome (AT-13), valeurs échappées, preuves présentes, rien d'inventé.</summary>
[TestClass]
public class RapportTests
{
    private static readonly DateTime Le = new(2026, 11, 13, 14, 0, 0);

    private static DonneesFiche Donnees()
    {
        var modele = new Champ("Modèle", "ThinkPad T480") { Id = "modele" };
        var ecran = new Champ("Écran", null) { Id = "ecran" };
        ecran.Corriger("14 pouces <FHD>", "Julien", Le);
        var champs = new List<Champ> { new("Fabricant", "LENOVO", "Win32_ComputerSystem") { Id = "fabricant" }, modele, ecran };
        var controles = new List<Controle>
        {
            new("disque", "Santé des disques", Statut.Conforme, "Aucun indicateur de panne lu (SSD).") { Source = "SMART / journal NVMe" },
            new("batterie", "Batterie", Statut.Anomalie, "71 % de la capacité d'origine.") { Portee = Portee.ReserveCommerciale },
            Controle.NonRealise("test-webcam", "Webcam"),
        };
        var bilan = RegleVente.Evaluer(controles, new[] { "disque", "batterie", "test-webcam" });
        return new DonneesFiche("v1042", champs, controles, bilan, new MesureBatterie(true, 1, 50000, 35500, 312), "Bon état", null);
    }

    [TestMethod]
    public void Contenu_StatutIdentiteControlesJournal()
    {
        var html = Rapport.Html(Donnees(), "Windows en mode audit", "v2.0.120", Le, new[] { "2026-11-13 10:00:00 | Action windows-update | Reussi" });
        StringAssert.Contains(html, "<title>Rapport V1042 · Lenovo ThinkPad T480</title>");
        StringAssert.Contains(html, "Incomplet");
        StringAssert.Contains(html, "Webcam");
        StringAssert.Contains(html, "Réserve commerciale");
        StringAssert.Contains(html, "corrigée par Julien le 13/11/2026 14:00");
        StringAssert.Contains(html, "Non détecté");
        StringAssert.Contains(html, "Capacité nominale : 50");
        StringAssert.Contains(html, "312");
        StringAssert.Contains(html, "SMART / journal NVMe");
        StringAssert.Contains(html, "Action windows-update");
        StringAssert.Contains(html, "ne garantissent pas l'absence de défaut");
    }

    [TestMethod]
    public void Autonome_EtEchappe()
    {
        var html = Rapport.Html(Donnees(), "mode <b>", "v2", Le, new[] { "<script>alert(1)</script>" });
        Assert.IsFalse(html.Contains("<script"), "aucun script, même venu du journal");
        StringAssert.Contains(html, "&lt;script&gt;");
        StringAssert.Contains(html, "14 pouces &lt;FHD&gt;");
        StringAssert.Contains(html, "mode &lt;b&gt;");
        Assert.IsFalse(html.Contains("http://") || html.Contains("https://"), "aucune ressource externe");
    }
}
