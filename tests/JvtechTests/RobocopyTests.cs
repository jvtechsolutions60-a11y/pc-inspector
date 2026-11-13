extern alias migration;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using migration::PcMigration.Migration;

namespace JvtechTests;

/// <summary>Lecture des résultats de robocopy simulés : jamais « réussi » s'il manque des fichiers.</summary>
[TestClass]
public class RobocopyTests
{
    private static readonly string[] FrenchSummary =
    {
        "",
        "               Total    Copié   Ignoré  Incompatibilité      ÉCHEC    Extras",
        "Répertoires :        12         3         9         0         0         0",
        "  Fichiers :       100        90         5         0         5         0",
        "    Octets :   1000000    900000     50000         0     50000         0",
        "    Heures :   0:00:05   0:00:04                       0:00:00   0:00:01",
    };

    private static readonly string[] EnglishComplete =
    {
        "               Total    Copied   Skipped  Mismatch    FAILED    Extras",
        "    Dirs :        12         3         9         0         0         0",
        "   Files :       100        60        40         0         0         0",
        "   Bytes :   1000000    600000    400000         0         0         0",
        "   Times :   0:00:05   0:00:04                       0:00:00   0:00:01",
    };

    [TestMethod]
    public void Summary_French_WithFailures()
    {
        var result = new CopyResult { ExitCode = 9 };
        Assert.IsTrue(Tools.ReadSummary(FrenchSummary, result));
        Assert.AreEqual(100, result.Source.Files);
        Assert.AreEqual(95, result.Copied.Files, "copiés + ignorés (déjà identiques) sont présents à l'arrivée");
        Assert.AreEqual(5, result.Failed);
        Assert.AreEqual(5, result.Missing);
        Assert.IsFalse(result.IsComplete);
        StringAssert.Contains(result.Summary(), "INCOMPLET");
    }

    [TestMethod]
    public void Summary_English_Complete()
    {
        var result = new CopyResult { ExitCode = 1 };
        Assert.IsTrue(Tools.ReadSummary(EnglishComplete, result));
        Assert.AreEqual(100, result.Copied.Files);
        Assert.IsTrue(result.IsComplete);
        Assert.IsFalse(result.Summary().Contains("INCOMPLET"));
    }

    [TestMethod]
    public void Summary_Missing_ReturnsFalse()
    {
        Assert.IsFalse(Tools.ReadSummary(new[] { "ERREUR 32 (0x00000020) Copie du fichier C:\\a.txt" }, new CopyResult()));
    }

    [TestMethod]
    public void ExitCode8_WithFullCount_IsStillIncomplete()
    {
        var result = new CopyResult { ExitCode = 8 };
        Tools.ReadSummary(EnglishComplete, result);
        Assert.IsFalse(result.IsComplete, "robocopy signale un échec : on ne l'ignore pas même si les nombres concordent");
    }

    [DataTestMethod]
    [DataRow(-1, true)]
    [DataRow(16, true)]
    [DataRow(24, true)]
    [DataRow(8, false)]
    [DataRow(0, false)]
    public void Fatal(int code, bool fatal)
    {
        var result = new CopyResult { ExitCode = code };
        Assert.AreEqual(fatal, result.Fatal);
        if (fatal) StringAssert.StartsWith(result.Summary(), "ÉCHEC");
    }

    [TestMethod]
    public void Unverified_IsNotComplete()
    {
        var result = new CopyResult { ExitCode = 1, Unverified = true };
        Assert.IsFalse(result.IsComplete);
        StringAssert.Contains(result.Summary(), "NON VÉRIFIÉ");
    }

    [TestMethod]
    public void Meaning_AllBits()
    {
        for (var code = 0; code < 32; code++) Assert.IsFalse(string.IsNullOrWhiteSpace(Tools.RobocopyMeaning(code)));
        StringAssert.Contains(Tools.RobocopyMeaning(0), "identique");
        StringAssert.Contains(Tools.RobocopyMeaning(3), "en plus");
        StringAssert.Contains(Tools.RobocopyMeaning(8), "pas pu");
        StringAssert.Contains(Tools.RobocopyMeaning(16), "grave");
        StringAssert.Contains(Tools.RobocopyMeaning(-1), "pas pu s'exécuter");
    }

    [TestMethod]
    public void FailedPaths_AnyLanguage()
    {
        var output = new[]
        {
            "2026/11/10 10:00:00 ERREUR 32 (0x00000020) Copie du fichier C:\\Users\\Client\\Documents\\ouvert.xlsx",
            "Le processus ne peut pas accéder au fichier car ce fichier est utilisé par un autre processus.",
            "2026/11/10 10:00:01 ERROR 5 (0x00000005) Copying File C:\\Users\\Client\\Desktop\\refusé.pdf",
            "2026/11/10 10:00:01 ERROR 5 (0x00000005) Copying File C:\\Users\\Client\\Desktop\\refusé.pdf",
            "2026/11/10 10:00:02 ERREUR 3 (0x00000003) \\\\NAS\\partage\\absent",
        };
        var paths = Tools.FailedPaths(output);
        CollectionAssert.AreEqual(new List<string>
        {
            "C:\\Users\\Client\\Documents\\ouvert.xlsx",
            "C:\\Users\\Client\\Desktop\\refusé.pdf",
            "\\\\NAS\\partage\\absent",
        }, paths);
    }

    [TestMethod]
    public void Arguments_NeverDeleteOrMove()
    {
        foreach (var neverOverwrite in new[] { false, true })
        {
            var args = Tools.RobocopyArguments(@"C:\Users\Client\Documents", @"E:\Sauvegarde\Documents", neverOverwrite,
                new[] { "AppData" }, new[] { "*.tmp" }).ToUpperInvariant();
            foreach (var forbidden in Tools.ForbiddenSwitches)
                Assert.IsFalse(args.Split(' ').Contains(forbidden), $"{forbidden} ne doit jamais être utilisé");
            StringAssert.Contains(args, "/E ");
            StringAssert.Contains(args, "/BYTES");
            // Restauration : aucun fichier existant remplacé, ni plus ancien (/XO), ni plus récent (/XN), ni modifié (/XC).
            foreach (var skip in new[] { " /XO", " /XN", " /XC" }) Assert.AreEqual(neverOverwrite, args.Contains(skip), skip);
        }
    }

    [TestMethod]
    public void Conflicts_BothVersionsKept_NothingReplaced()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jvtech-test-" + Guid.NewGuid().ToString("N"));
        var source = System.IO.Path.Combine(root, "sauvegarde");
        var target = System.IO.Path.Combine(root, "nouveau");
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(source, "Factures"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(target, "Factures"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(source, "Factures", "devis.txt"), "version ancien PC");
            System.IO.File.WriteAllText(System.IO.Path.Combine(target, "Factures", "devis.txt"), "version nouveau PC, différente");
            System.IO.File.SetLastWriteTimeUtc(System.IO.Path.Combine(target, "Factures", "devis.txt"), new DateTime(2020, 1, 1));
            System.IO.File.WriteAllText(System.IO.Path.Combine(source, "identique.txt"), "même");
            System.IO.File.WriteAllText(System.IO.Path.Combine(target, "identique.txt"), "même");
            System.IO.File.SetLastWriteTimeUtc(System.IO.Path.Combine(target, "identique.txt"), System.IO.File.GetLastWriteTimeUtc(System.IO.Path.Combine(source, "identique.txt")));
            System.IO.File.WriteAllText(System.IO.Path.Combine(source, "nouveau-fichier.txt"), "seul");

            var saved = Tools.SaveConflicts(source, target, "En double", new Progress<string>());

            CollectionAssert.AreEqual(new[] { System.IO.Path.Combine("Factures", "devis.txt") }, saved);
            Assert.AreEqual("version nouveau PC, différente", System.IO.File.ReadAllText(System.IO.Path.Combine(target, "Factures", "devis.txt")));
            Assert.AreEqual("version ancien PC", System.IO.File.ReadAllText(System.IO.Path.Combine(target, "En double", "Factures", "devis.txt")));
        }
        finally
        {
            try { System.IO.Directory.Delete(root, true); } catch (System.IO.IOException) { }
        }
    }

    [TestMethod]
    public void Arguments_QuotePathsWithSpacesAndTrailingSlash()
    {
        var args = Tools.RobocopyArguments(@"C:\Users\Jean Dupont\", @"E:\Sauvegarde PC\", false);
        StringAssert.StartsWith(args, "\"C:\\Users\\Jean Dupont\" \"E:\\Sauvegarde PC\"");
    }
}
