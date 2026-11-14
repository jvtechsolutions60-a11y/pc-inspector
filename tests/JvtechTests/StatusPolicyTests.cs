extern alias serveur;
using System;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using serveur::JvtechServeur.Pxe;

namespace JvtechTests;

/// <summary>Suivi des postes du serveur PXE (port 8081) : jeton, réseau local, contenu et débit limités.</summary>
[TestClass]
public class StatusPolicyTests
{
    [TestMethod]
    public void Token_IsRandomAndLong()
    {
        var a = StatusPolicy.NewToken();
        var b = StatusPolicy.NewToken();
        Assert.AreNotEqual(a, b);
        Assert.IsTrue(a.Length >= 40);
        StringAssert.Matches(a, new System.Text.RegularExpressions.Regex("^[A-Za-z0-9_-]+$"));
    }

    [TestMethod]
    public void TokenMatches()
    {
        Assert.IsTrue(StatusPolicy.TokenMatches("abc123", "abc123"));
        Assert.IsFalse(StatusPolicy.TokenMatches("abc123", "abc124"));
        Assert.IsFalse(StatusPolicy.TokenMatches("abc123", "abc12"));
        Assert.IsFalse(StatusPolicy.TokenMatches("abc123", ""));
        Assert.IsFalse(StatusPolicy.TokenMatches("abc123", null));
        Assert.IsFalse(StatusPolicy.TokenMatches("", ""), "un jeton vide côté serveur ne doit rien accepter");
        Assert.IsFalse(StatusPolicy.TokenMatches(null, null));
    }

    [DataTestMethod]
    [DataRow("192.168.1.50", true)]
    [DataRow("192.168.1.254", true)]
    [DataRow("192.168.2.50", false)]
    [DataRow("8.8.8.8", false)]
    [DataRow("127.0.0.1", true)]
    [DataRow("::1", true)]
    [DataRow("fe80::1", false)]
    public void Source_SameSubnetOnly(string remote, bool expected) =>
        Assert.AreEqual(expected, StatusPolicy.IsAllowedSource(IPAddress.Parse(remote), IPAddress.Parse("192.168.1.10"), IPAddress.Parse("255.255.255.0")));

    [TestMethod]
    public void Source_UnknownServerNetwork_OnlyLoopback()
    {
        Assert.IsFalse(StatusPolicy.IsAllowedSource(IPAddress.Parse("192.168.1.50"), null, null));
        Assert.IsTrue(StatusPolicy.IsAllowedSource(IPAddress.Loopback, null, null));
        Assert.IsFalse(StatusPolicy.IsAllowedSource(null, IPAddress.Parse("192.168.1.10"), IPAddress.Parse("255.255.255.0")));
    }

    [TestMethod]
    public void ParseStatus_KeepsKnownFieldsOnly_AndCleansValues()
    {
        var values = StatusPolicy.ParseStatus("Serie=ABC123\nEtape=Mises à jour\u0007\nInconnu=x\nMacs=00:11:22:33:44:55,pas-une-mac\n");
        Assert.IsNotNull(values);
        Assert.AreEqual("ABC123", values!["Serie"]);
        Assert.AreEqual("Mises à jour", values["Etape"]);
        Assert.IsFalse(values.ContainsKey("Inconnu"));
        Assert.AreEqual("00:11:22:33:44:55", values["Macs"]);
    }

    [TestMethod]
    public void ParseStatus_RejectsUnidentifiablePosts()
    {
        Assert.IsNull(StatusPolicy.ParseStatus(""));
        Assert.IsNull(StatusPolicy.ParseStatus("n'importe quoi"));
        Assert.IsNull(StatusPolicy.ParseStatus("Etape=1\nMacs=zz"));
    }

    [TestMethod]
    public void ParseStatus_TruncatesLongValues()
    {
        var values = StatusPolicy.ParseStatus("Serie=S1\nDetail=" + new string('x', 5000));
        Assert.AreEqual(300, values!["Detail"].Length);
    }

    [DataTestMethod]
    [DataRow(@"..\..\Windows\win.ini", "_.._Windows_win.ini.html")]
    [DataRow("rapport.html", "rapport.html")]
    [DataRow("....", "rapport.html")]
    [DataRow("a<b>c|d", "a_b_c_d.html")]
    public void SafeReportName_StaysInFolder(string name, string expected)
    {
        var safe = StatusPolicy.SafeReportName(name, "S", new DateTime(2026, 11, 10));
        Assert.AreEqual(expected, safe);
        Assert.IsFalse(safe.Contains("\\") || safe.Contains("/") || safe.Contains(":"));
    }

    [TestMethod]
    public void RateLimiter_PerAddressPerMinute()
    {
        var limiter = new RateLimiter(3);
        var t = new DateTime(2026, 11, 10, 12, 0, 0);
        Assert.IsTrue(limiter.Allow("a", t));
        Assert.IsTrue(limiter.Allow("a", t));
        Assert.IsTrue(limiter.Allow("a", t));
        Assert.IsFalse(limiter.Allow("a", t.AddSeconds(10)));
        Assert.IsTrue(limiter.Allow("b", t.AddSeconds(10)), "une autre adresse n'est pas bloquée");
        Assert.IsTrue(limiter.Allow("a", t.AddMinutes(1)), "la limite repart après une minute");
    }
}
