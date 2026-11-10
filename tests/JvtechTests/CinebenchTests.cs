extern alias inspector;
using System;
using System.IO;
using System.IO.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using inspector::PcInspector.Collectors;

namespace JvtechTests;

/// <summary>Téléchargement de Cinebench : lien trouvé seulement chez Maxon, archive dézippée sans sortir du dossier.</summary>
[TestClass]
public class CinebenchTests
{
    [TestMethod]
    public void FindZipUrl_PicksWindowsArchiveOnMaxon()
    {
        var html = "<a href=\"https://mx-app-blob-prod.maxon.net/mx-package-production/website/macos/maxon/cinebench/Cinebench2026_macOS.zip\">Mac</a>" +
                   "<a href=\"https://mx-app-blob-prod.maxon.net/mx-package-production/website/windows/maxon/cinebench/Cinebench2026_win_x86_64.zip\">Windows</a>";
        Assert.AreEqual("https://mx-app-blob-prod.maxon.net/mx-package-production/website/windows/maxon/cinebench/Cinebench2026_win_x86_64.zip",
            CinebenchDownload.FindZipUrl(html));
    }

    [TestMethod]
    public void FindZipUrl_ReadsEscapedJson()
    {
        var json = "{\"url\":\"https:\\/\\/mx-app-blob-prod.maxon.net\\/x\\/windows\\/Cinebench2026_win_x86_64.zip\"}";
        Assert.AreEqual("https://mx-app-blob-prod.maxon.net/x/windows/Cinebench2026_win_x86_64.zip", CinebenchDownload.FindZipUrl(json));
    }

    [TestMethod]
    public void FindZipUrl_RefusesOtherSitesAndHttp()
    {
        Assert.IsNull(CinebenchDownload.FindZipUrl("https://maxon.net.pirate.example/Cinebench_win.zip"));
        Assert.IsNull(CinebenchDownload.FindZipUrl("http://dl.maxon.net/Cinebench_win.zip"));
        Assert.IsNull(CinebenchDownload.FindZipUrl("aucun lien"));
        Assert.IsFalse(CinebenchDownload.IsMaxonHttps("https://evilmaxon.net/a.zip"));
        Assert.IsTrue(CinebenchDownload.IsMaxonHttps("https://www.maxon.net/a.zip"));
    }

    [TestMethod]
    public void ExtractSafely_RefusesPathTraversal()
    {
        var folder = Path.Combine(Path.GetTempPath(), "JvtechTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var good = Path.Combine(folder, "bon.zip");
            using (var archive = ZipFile.Open(good, ZipArchiveMode.Create))
                using (var w = new StreamWriter(archive.CreateEntry("Cinebench/Cinebench.exe").Open())) w.Write("x");
            CinebenchDownload.ExtractSafely(good, Path.Combine(folder, "ok"));
            Assert.IsTrue(File.Exists(Path.Combine(folder, "ok", "Cinebench", "Cinebench.exe")));

            var bad = Path.Combine(folder, "mauvais.zip");
            using (var archive = ZipFile.Open(bad, ZipArchiveMode.Create))
                using (var w = new StreamWriter(archive.CreateEntry("../../evade.txt").Open())) w.Write("x");
            Assert.ThrowsException<InvalidDataException>(() => CinebenchDownload.ExtractSafely(bad, Path.Combine(folder, "ko")));
            Assert.IsFalse(File.Exists(Path.Combine(folder, "evade.txt")));
        }
        finally { Directory.Delete(folder, true); }
    }
}
