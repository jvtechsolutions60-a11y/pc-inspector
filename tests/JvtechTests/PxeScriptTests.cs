extern alias serveur;
using System;
using System.Linq;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using serveur::JvtechServeur.Pxe;

namespace JvtechTests;

/// <summary>Script Windows PE : rien ne s'installe sans confirmation tapée sur le poste et avec un seul disque (audit C01).</summary>
[TestClass]
public class PxeScriptTests
{
    private static readonly string Script = ImageBuilder.WinPeScript(IPAddress.Parse("192.168.1.10"), "fictif");

    [TestMethod]
    public void DiskCheck_ComesBeforeAnyInstallation()
    {
        var check = Script.IndexOf("diskpart /s", StringComparison.Ordinal);
        var confirm = Script.IndexOf(":confirme", StringComparison.Ordinal);
        var share = Script.IndexOf("net use J:", StringComparison.Ordinal);
        var setup = Script.IndexOf(@"J:\setup.exe", StringComparison.Ordinal);
        Assert.IsTrue(check >= 0 && confirm > check && share > confirm && setup > share);
    }

    [TestMethod]
    public void OnlyTypedEffacer_Continues_OtherwiseShutdown()
    {
        var lines = ImageBuilder.WinPeDiskCheck.Split(new[] { "\r\n" }, StringSplitOptions.None).ToList();
        var ask = lines.FindIndex(l => l.StartsWith("set /p REPONSE=", StringComparison.Ordinal));
        Assert.IsTrue(ask > 0);
        StringAssert.Contains(lines[ask + 1], "\"%REPONSE%\"==\"EFFACER\" goto confirme");
        Assert.IsTrue(lines.Skip(ask + 2).TakeWhile(l => l != ":confirme").Contains("wpeutil shutdown"));
    }

    [TestMethod]
    public void NotExactlyOneDisk_Stops()
    {
        var lines = ImageBuilder.WinPeDiskCheck.Split(new[] { "\r\n" }, StringSplitOptions.None).ToList();
        var test = lines.FindIndex(l => l.Contains("\"%DISQUES%\"==\"1\""));
        var label = lines.IndexOf(":unseuldisque");
        Assert.IsTrue(test >= 0 && label > test);
        Assert.IsTrue(lines.Skip(test + 1).Take(label - test - 1).Contains("wpeutil shutdown"));
    }
}

/// <summary>Mises à jour intégrées à l'image : HTTPS chez Microsoft seulement, jamais de retour en arrière (audit I15).</summary>
[TestClass]
public class ImageUpdaterTests
{
    [DataTestMethod]
    [DataRow("KB5066835", "", null)]
    [DataRow("KB5066835", "KB5065426", null)]
    [DataRow("KB5065426", "KB5066835", "refus")]
    [DataRow("KB5066835", "KB5066835", "refus")]
    [DataRow("", "KB5066835", "refus")]
    public void Downgrade(string candidate, string last, string? expected) =>
        Assert.AreEqual(expected is null, ImageUpdater.Downgrade(candidate, last) is null);

    [DataTestMethod]
    [DataRow("http://catalog.s.download.windowsupdate.com/c/msdownload/update/software/secu/2026/11/windows11.0-kb5066835-x64.msu", "https://catalog.s.download.windowsupdate.com/c/msdownload/update/software/secu/2026/11/windows11.0-kb5066835-x64.msu")]
    [DataRow("https://catalog.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/a/public/windows11.0-kb5066835-x64.msu", "https://catalog.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/a/public/windows11.0-kb5066835-x64.msu")]
    [DataRow("http://exemple.com/windows11.0-kb5066835-x64.msu", null)]
    [DataRow("ftp://download.windowsupdate.com/x.msu", null)]
    [DataRow("pas une adresse", null)]
    public void SecureUrl(string url, string? expected) => Assert.AreEqual(expected, ImageUpdater.SecureUrl(url));
}
