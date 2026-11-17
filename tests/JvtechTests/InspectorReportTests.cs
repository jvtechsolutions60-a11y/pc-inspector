extern alias inspector;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using inspector::PcInspector.Collectors;
using inspector::PcInspector.Models;
using inspector::PcInspector.Report;

namespace JvtechTests;

/// <summary>Rapport de PC Inspector (aussi fait par PC Maintenance) : jamais bloqué, jamais perdu.</summary>
[TestClass]
public class InspectorReportTests
{
    private sealed class Lent : ICollector
    {
        public string Title => "Lent";
        public string Icon => "";
        public InfoSection Collect() { Thread.Sleep(TimeSpan.FromSeconds(40)); return new InfoSection(Title); }
    }

    private sealed class Plante : ICollector
    {
        public string Title => "Plante";
        public string Icon => "";
        public InfoSection Collect() => throw new InvalidOperationException("WMI cassé");
    }

    private sealed class Bon : ICollector
    {
        public string Title => "Bon";
        public string Icon => "";
        public InfoSection Collect()
        {
            var s = new InfoSection(Title);
            s.Groups.Add(new InfoGroup("Groupe").Add("Clé", "Valeur"));
            return s;
        }
    }

    [TestMethod]
    public async Task UnePartieBloqueeOuEnErreur_NeBloquePasLeRapport()
    {
        var debut = DateTime.Now;
        var sections = await InspectorReport.CollectAsync(new ICollector[] { new Bon(), new Lent(), new Plante() }, TimeSpan.FromSeconds(2));
        Assert.IsTrue(DateTime.Now - debut < TimeSpan.FromSeconds(30), "le délai doit couper l'attente");
        Assert.AreEqual(3, sections.Count);
        Assert.IsNull(sections[0].Error);
        StringAssert.StartsWith(sections[1].Error, "Non lu");
        StringAssert.Contains(sections[2].Error, "WMI cassé");
    }

    [TestMethod]
    public void Enregistrement_DossierPrevuPuisRepli()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "jvtech-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var info = new ReportInfo { Client = "Test", Date = new DateTime(2026, 11, 17) };
            var sections = new List<InfoSection> { new Bon().Collect() };
            var ok = InspectorReport.Save(info, sections, "Rapport:PC/Test", dossier, pdf: false);
            Assert.IsNotNull(ok.Path);
            Assert.AreEqual(dossier, Path.GetDirectoryName(ok.Path));
            Assert.AreEqual("Rapport-PC-Test.html", Path.GetFileName(ok.Path));
            StringAssert.Contains(File.ReadAllText(ok.Path!), "Valeur");

            // Même nom : un second fichier, l'ancien n'est pas écrasé.
            var second = InspectorReport.Save(info, sections, "Rapport:PC/Test", dossier, pdf: false);
            Assert.AreNotEqual(ok.Path, second.Path);
            Assert.IsTrue(File.Exists(ok.Path));

            // Dossier prévu impossible : un autre emplacement est essayé.
            var repli = InspectorReport.Save(info, sections, "Rapport repli " + Guid.NewGuid().ToString("N"), "Z:\\<interdit>|", pdf: false);
            Assert.IsNotNull(repli.Path, string.Join(" | ", repli.Problems));
            Assert.IsTrue(repli.Problems.Count >= 1);
            File.Delete(repli.Path!);
        }
        finally
        {
            try { Directory.Delete(dossier, true); } catch (IOException) { }
        }
    }

    [TestMethod]
    public void Emplacements_LeDossierClientDAbord()
    {
        var liste = InspectorReport.Folders(@"C:\Client").ToList();
        Assert.AreEqual(@"C:\Client", liste[0]);
        Assert.IsTrue(liste.Count >= 3);
        Assert.AreEqual(liste.Count, liste.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    public void NomDeFichier()
    {
        Assert.AreEqual("Rapport", InspectorReport.FileName("  "));
        Assert.AreEqual("a-b", InspectorReport.FileName("a?b"));
        Assert.AreEqual(120, InspectorReport.FileName(new string('x', 300)).Length);
    }

    [TestMethod]
    public void LesPartiesDuRapport_SansTestDeDebitNiPerformances()
    {
        var titres = InspectorReport.Collectors().Select(c => c.GetType().Name).ToList();
        Assert.AreEqual("HealthCollector", titres[0]);
        CollectionAssert.DoesNotContain(titres, "SpeedTestCollector");
        CollectionAssert.DoesNotContain(titres, "PerformanceCollector");
        Assert.IsTrue(titres.Count >= 20);
    }
}
