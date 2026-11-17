extern alias entretien;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using entretien::JvtechEntretien;

namespace JvtechTests;

/// <summary>JVTECH Entretien : réglages, points à voir, tableaux et tâche planifiée (données simulées, rien n'est lancé).</summary>
[TestClass]
public class EntretienTests
{
    private static Etat Sain(DateTime? date = null) => new()
    {
        Date = date ?? new DateTime(2026, 11, 17, 9, 5, 0),
        MajWindows = new List<string>(),
        AutresLogiciels = 2,
        DisqueLibre = 120_000_000_000,
        DisqueTotal = 256_000_000_000,
        Antivirus = new List<Antivirus> { new("Microsoft Defender Antivirus", true, true) },
        Windows = "Windows 11 Pro 24H2 (26100.2314)",
    };

    [TestMethod]
    public void Reglages_ListeSureParDefaut_MotDePasseJamaisDansLeJournal()
    {
        var r = Reglages.Lire("client = C042\npartage_mot_de_passe = secret\nlogiciels =\ndossier_partage = \\\\NAS\\JVTECH\\Entretien\\\nfaute\nsmtp_mot_de_passe_x = abc\ndisque_libre_alerte = 20 %");
        Assert.AreEqual("C042", r.Client);
        Assert.AreEqual("secret", r.PartageMotDePasse);
        Assert.AreEqual(@"\\NAS\JVTECH\Entretien", r.DossierPartage);
        Assert.AreEqual(20, r.DisqueLibreAlerte);
        CollectionAssert.AreEqual(Reglages.ListeSure.ToList(), r.Logiciels);
        Assert.IsFalse(r.Ignorees.Any(l => l.Contains("abc") || l.Contains("secret")), string.Join(" | ", r.Ignorees));
        Assert.IsTrue(r.Ignorees.Contains("faute"));

        var liste = Reglages.Lire("logiciels = Google.Chrome; 7zip.7zip");
        CollectionAssert.AreEqual(new[] { "Google.Chrome", "7zip.7zip" }, liste.Logiciels);
    }

    [TestMethod]
    public void Nas_RacineDuPartage()
    {
        Assert.AreEqual(@"\\NAS\JVTECH", Nas.Racine(@"\\NAS\JVTECH\Entretien\Clients"));
        Assert.AreEqual(@"\\192.168.1.10\partage", Nas.Racine(@"\\192.168.1.10\partage"));
        Assert.IsNull(Nas.Racine(@"\\NAS"));
        Assert.IsNull(Nas.Racine(@"D:\Rapports"));
    }

    [TestMethod]
    public void Releve_PointsAVoir()
    {
        var reglages = Reglages.Lire("client = C042\nposte = COMPTA-01");
        Assert.AreEqual(0, Rapport.ProblemesEtat(Sain(), reglages).Count);

        var mauvais = new Etat
        {
            DisqueLibre = 10_000_000_000, DisqueTotal = 256_000_000_000,
            Antivirus = new List<Antivirus> { new("Avast", false, true) },
            RedemarrageNecessaire = true,
        };
        var p = Rapport.ProblemesEtat(mauvais, reglages);
        Assert.AreEqual(3, p.Count, string.Join(" | ", p));
        Assert.IsTrue(p.Any(x => x.StartsWith("Disque système presque plein")));
        Assert.IsTrue(p.Any(x => x.StartsWith("Antivirus désactivé")));

        Assert.IsTrue(Rapport.ProblemesEtat(new Etat { Antivirus = new List<Antivirus>() }, reglages).Contains("Aucun antivirus détecté"));
        // Antivirus illisible (Windows Server) : pas d'alerte inventée.
        Assert.AreEqual(0, Rapport.ProblemesEtat(new Etat { Antivirus = null }, reglages).Count);
    }

    [TestMethod]
    public void Entretien_ResumeEtTableau()
    {
        var reglages = Reglages.Lire("client = C042\nposte = COMPTA-01");
        var avant = Sain();
        var r = new Resultat
        {
            Avant = new Etat { Date = avant.Date, MajWindows = new List<string> { "KB1", "KB2", "KB3", "KB4" }, RedemarrageNecessaire = false },
            Apres = new Etat { MajWindows = new List<string> { "KB4" }, RedemarrageNecessaire = true, Antivirus = null },
        };
        r.WindowsInstallees.AddRange(new[] { "KB1", "KB2", "KB3" });
        r.WindowsEchecs.Add("KB4");
        StringAssert.StartsWith(Rapport.Resume(r), "4 mise(s) à jour Windows à faire, 3 passée(s)");
        StringAssert.Contains(Rapport.Resume(r), "redémarrage nécessaire");

        var problemes = Rapport.Problemes(r, reglages);
        Assert.IsTrue(problemes.Contains("1 mise(s) à jour Windows en échec"));
        Assert.IsFalse(problemes.Any(p => p.Contains("Redémarrage")), "un redémarrage après les mises à jour est normal");

        var champs = Rapport.LigneCsv(r, reglages, problemes).Split(';');
        Assert.AreEqual(Rapport.EnteteCsv.Split(';').Length, champs.Length);
        Assert.AreEqual("4", champs[3]);
        Assert.AreEqual("3", champs[4]);

        var html = Rapport.Html(r, reglages, problemes, "v1.0.0");
        StringAssert.Contains(html, "KB1");
        StringAssert.Contains(html, "COMPTA-01");
    }

    [TestMethod]
    public void Csv_PasDeFormuleExcel()
    {
        Assert.AreEqual("'=1+1", Rapport.Csv("=1+1"));
        Assert.AreEqual("\"a;b\"", Rapport.Csv("a;b"));
        Assert.AreEqual("a b", Rapport.Csv("a\r\nb").Replace("  ", " "));
    }

    [TestMethod]
    public void Winget_VersionDuDossier()
    {
        Assert.AreEqual(new Version(1, 24, 25200, 0), Winget.VersionDe("Microsoft.DesktopAppInstaller_1.24.25200.0_x64__8wekyb3d8bbwe"));
        Assert.AreEqual(new Version(0, 0), Winget.VersionDe("autre"));
    }

    [TestMethod]
    public void Tache_OuvertureDeSession_SystemCacheeSansReveil()
    {
        var xml = new XmlDocument();
        xml.LoadXml(Installation.TacheXml(@"C:\Program Files\JVTECH\Entretien\JvtechEntretien.exe", @"C:\Program Files\JVTECH\Entretien"));
        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
        Assert.IsNotNull(xml.SelectSingleNode("//t:LogonTrigger", ns));
        Assert.AreEqual("S-1-5-18", xml.SelectSingleNode("//t:UserId", ns)!.InnerText);
        Assert.AreEqual("true", xml.SelectSingleNode("//t:Hidden", ns)!.InnerText);
        Assert.AreEqual("false", xml.SelectSingleNode("//t:WakeToRun", ns)!.InnerText);
        Assert.AreEqual("--tache", xml.SelectSingleNode("//t:Arguments", ns)!.InnerText);
    }

    [TestMethod]
    public void NomDuDossierDuPoste()
    {
        Assert.AreEqual("COMPTA-01", Envois.NomDossier("COMPTA-01"));
        Assert.AreEqual("A-B", Envois.NomDossier("A/B"));
        Assert.AreEqual("poste", Envois.NomDossier("  "));
    }
}
