extern alias securite;
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using securite::PcSecurite.Checks;

namespace JvtechTests;

/// <summary>Note de sécurité et références de PC Sécurité.</summary>
[TestClass]
public class SecurityTests
{
    [TestMethod]
    public void Score_IgnoresInfoNotApplicableAndUnknown()
    {
        var checks = new[]
        {
            SecurityCheck.Ok("a", ""),
            SecurityCheck.Ok("b", ""),
            SecurityCheck.Info("c", ""),
            SecurityCheck.NotApplicable("d", "pas de Wi-Fi"),
            SecurityCheck.Unknown("e", "accès refusé"),
        };
        Assert.AreEqual(100, SecurityScore.Compute(checks), "un contrôle impossible n'est jamais compté comme un échec");
    }

    [TestMethod]
    public void Score_WarningHalf_DangerCaps()
    {
        Assert.AreEqual(75, SecurityScore.Compute(new[] { SecurityCheck.Ok("a", ""), SecurityCheck.Warning("b", "", "") }));
        var withDanger = Enumerable.Range(0, 20).Select(i => SecurityCheck.Ok("ok" + i, "")).Append(SecurityCheck.Danger("x", "", "")).ToList();
        Assert.IsTrue(SecurityScore.Compute(withDanger) <= 69, "un seul point rouge suffit à ne pas afficher une note rassurante");
    }

    [TestMethod]
    public void Score_NothingScored_IsZero() =>
        Assert.AreEqual(0, SecurityScore.Compute(new[] { SecurityCheck.Unknown("a", "") }));

    [TestMethod]
    public void UnscoredStatuses_NeverNeedAction()
    {
        foreach (var check in new[] { SecurityCheck.Info("a", ""), SecurityCheck.NotApplicable("b", ""), SecurityCheck.Unknown("c", "") })
        {
            Assert.IsFalse(check.IsScored);
            Assert.IsFalse(check.NeedsAction);
        }
    }

    [TestMethod]
    public void References_Staleness()
    {
        Assert.IsFalse(References.IsStale(References.UpdatedOn.AddDays(30)));
        Assert.IsTrue(References.IsStale(References.UpdatedOn.AddDays(400)));
    }

    [TestMethod]
    public void References_WindowsSupport_SortedAndComplete()
    {
        var builds = References.WindowsSupport.Select(s => s.Build).ToList();
        CollectionAssert.AreEqual(builds.OrderByDescending(b => b).ToList(), builds, "du plus récent au plus ancien (First() prend la première qui convient)");
        Assert.AreEqual(0, builds.Last(), "Windows 10 et plus ancien en dernier recours");
    }

    [TestMethod]
    public void References_KnownDns_AreValidAddresses()
    {
        foreach (var dns in References.KnownDns.Keys)
            Assert.IsTrue(System.Net.IPAddress.TryParse(dns, out _), dns);
    }

    [TestMethod]
    public void References_SoftwareRules()
    {
        Assert.IsTrue(References.VulnerableSoftware.Any(r => r.Name.IsMatch("WinRAR 6.02 (64-bit)")));
        Assert.IsFalse(References.ObsoleteSoftware.Any(r => r.Name.IsMatch("Microsoft Office Professionnel Plus 2024")));
        Assert.IsTrue(References.ObsoleteSoftware.Any(r => r.Name.IsMatch("Microsoft Office Famille et Petite Entreprise 2016")));
        Assert.IsFalse(References.ObsoleteSoftware.Any(r => r.Name.IsMatch("Microsoft Office 2016 Click-to-Run Extensibility Component")));
    }

    [TestMethod]
    public void JvtechTools_InOurFolderOnly()
    {
        const string ours = @"C:\Users\Technicien\Downloads\JVTECH-Outils-v1.0.88";
        Assert.IsTrue(SecurityChecks.IsJvtechTool(ours + @"\PcSecurite.exe", ours));
        Assert.IsTrue(SecurityChecks.IsJvtechTool(ours + @"\PcInspector.exe", ours + @"\"));
        Assert.IsFalse(SecurityChecks.IsJvtechTool(@"C:\Users\Technicien\AppData\Local\Temp\PcSecurite.exe", ours), "même nom ailleurs : contrôlé normalement");
        Assert.IsFalse(SecurityChecks.IsJvtechTool(ours + @"\autre.exe", ours));
    }
}
