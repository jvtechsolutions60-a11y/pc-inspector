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
