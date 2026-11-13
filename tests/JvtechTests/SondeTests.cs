extern alias sonde;
using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using sonde::JvtechSonde;
using SondeChecks = sonde::PcSecurite.Checks;

namespace JvtechTests;

/// <summary>JVTECH Sonde : réglages, seuils, message envoyé à Uptime Kuma, résultats et tâche planifiée. Rien n'est mesuré sur le PC.</summary>
[TestClass]
public class SondeTests
{
    // ---------- sonde.ini ----------

    [TestMethod]
    public void Settings_Defaults_WhenEmpty()
    {
        var s = Settings.Parse("");
        Assert.AreEqual(85, s.DiskWarning);
        Assert.AreEqual(95, s.DiskCritical);
        Assert.AreEqual("", s.KumaPush);
        Assert.IsFalse(s.AlertOnWarning);
        Assert.AreEqual(Environment.MachineName, s.Machine);
    }

    [TestMethod]
    public void Settings_ReadsValues_CommentsAndFrenchDecimals()
    {
        var s = Settings.Parse("; commentaire\r\n# autre\r\n[section]\r\nclient = Mairie de Test\r\nposte = ACCUEIL-01\r\n" +
                               "kuma_push = https://kuma.exemple.fr/api/push/abc?status=up&msg=OK&ping=\r\n" +
                               "alerte_orange = oui\r\ndisque_orange = 80,5\r\nram_rouge = 98 %\r\nrecherche_maj_jours = 10\r\n");
        Assert.AreEqual("Mairie de Test", s.Client);
        Assert.AreEqual("ACCUEIL-01", s.Machine);
        Assert.IsTrue(s.KumaPush.StartsWith("https://kuma.exemple.fr/api/push/abc"));
        Assert.IsTrue(s.AlertOnWarning);
        Assert.AreEqual(80.5, s.DiskWarning);
        Assert.AreEqual(98, s.RamCritical);
        Assert.AreEqual(10, s.UpdateSearchDays);
        Assert.AreEqual(0, s.Ignored.Count);
    }

    [TestMethod]
    public void Settings_TypoKeepsDefault_AndIsReported()
    {
        var s = Settings.Parse("disque_orange = beaucoup\nclef_inconnue = 3\npas de signe égal\n");
        Assert.AreEqual(85, s.DiskWarning, "une valeur illisible ne doit jamais couper la surveillance");
        Assert.AreEqual(3, s.Ignored.Count);
    }

    [TestMethod]
    public void Settings_CpuSampleIsBounded()
    {
        Assert.AreEqual(120, Settings.Parse("cpu_mesure_secondes = 9999").CpuSampleSeconds);
        Assert.AreEqual(1, Settings.Parse("cpu_mesure_secondes = 0").CpuSampleSeconds);
    }

    // ---------- Seuils ----------

    [TestMethod]
    public void Above_Thresholds()
    {
        Assert.AreEqual(Level.Ok, Rules.Above(84.9, 85, 95));
        Assert.AreEqual(Level.Warning, Rules.Above(85, 85, 95));
        Assert.AreEqual(Level.Critical, Rules.Above(95, 85, 95));
        Assert.AreEqual(Level.Unknown, Rules.Above(null, 85, 95));
        Assert.AreEqual(Level.Unknown, Rules.Above(double.NaN, 85, 95));
    }

    [TestMethod]
    public void CpuUsage_NeverRed()
    {
        Assert.AreEqual(Level.Warning, Rules.CpuUsage(100, 90));
        Assert.AreEqual(Level.Ok, Rules.CpuUsage(12, 90));
        Assert.AreEqual(Level.Unknown, Rules.CpuUsage(null, 90));
    }

    [TestMethod]
    public void CpuPercent_FromSystemTimes()
    {
        // 10 s de temps noyau (dont 7 s d'inactivité) et 2 s de temps utilisateur : 5 s actives sur 12 = 41,7 %.
        Assert.AreEqual(41.7, Rules.CpuPercent(0, 0, 0, 70, 100, 20));
        Assert.IsNull(Rules.CpuPercent(0, 0, 0, 0, 0, 0), "aucun temps écoulé");
        Assert.IsNull(Rules.CpuPercent(0, 0, 0, 200, 100, 20), "inactivité supérieure au total");
    }

    [TestMethod]
    public void UpdateSearch_Age()
    {
        var now = new DateTime(2026, 11, 13, 12, 0, 0);
        Assert.AreEqual(Level.Ok, Rules.UpdateSearch(now.AddDays(-2), now, 7));
        Assert.AreEqual(Level.Warning, Rules.UpdateSearch(now.AddDays(-8), now, 7));
        Assert.AreEqual(Level.Critical, Rules.UpdateSearch(now.AddDays(-22), now, 7));
        Assert.AreEqual(Level.Unknown, Rules.UpdateSearch(null, now, 7));
    }

    [TestMethod]
    public void Crashes_Thresholds()
    {
        Assert.AreEqual(Level.Ok, Rules.Crashes(0, 1, 3));
        Assert.AreEqual(Level.Warning, Rules.Crashes(2, 1, 3));
        Assert.AreEqual(Level.Critical, Rules.Crashes(3, 1, 3));
        Assert.AreEqual(Level.Unknown, Rules.Crashes(null, 1, 3));
    }

    [TestMethod]
    public void Security_SameLightsAsPcSecurite()
    {
        Assert.AreEqual(Level.Critical, Rules.FromSecurity(SondeChecks.CheckStatus.Danger));
        Assert.AreEqual(Level.Warning, Rules.FromSecurity(SondeChecks.CheckStatus.Warning));
        Assert.AreEqual(Level.Ok, Rules.FromSecurity(SondeChecks.CheckStatus.Info));
        Assert.AreEqual(Level.Unknown, Rules.FromSecurity(SondeChecks.CheckStatus.Unknown));
        Assert.AreEqual(Level.NotApplicable, Rules.FromSecurity(SondeChecks.CheckStatus.NotApplicable));
    }

    [TestMethod]
    public void Overall_IgnoresUnknown()
    {
        var points = new[]
        {
            new Point("Disque", "C:", Level.Ok, ""),
            new Point("Processeur", "température", Level.Unknown, ""),
            new Point("Batterie", "usure", Level.NotApplicable, ""),
        };
        Assert.AreEqual(Level.Ok, Rules.Overall(points), "une sonde absente n'est jamais une alerte");
        Assert.AreEqual(Level.Warning, Rules.Overall(points.Append(new Point("Mémoire", "", Level.Warning, ""))));
        Assert.AreEqual(Level.Unknown, Rules.Overall(new[] { new Point("x", "", Level.Unknown, "") }));
    }

    // ---------- Uptime Kuma ----------

    private static readonly Point[] Mixed =
    {
        new("Disque", "C:", Level.Critical, "96 % rempli"),
        new("Mémoire", "", Level.Warning, "91 % utilisée"),
        new("Pare-feu", "", Level.Ok, "activé"),
        new("Processeur", "température", Level.Unknown, "pas de sonde"),
    };

    [TestMethod]
    public void Kuma_Status_RedOnly_UnlessAlertOnWarning()
    {
        Assert.AreEqual("down", Kuma.Status(Mixed, alertOnWarning: false));
        var orangeOnly = Mixed.Where(p => p.Level != Level.Critical).ToArray();
        Assert.AreEqual("up", Kuma.Status(orangeOnly, alertOnWarning: false));
        Assert.AreEqual("down", Kuma.Status(orangeOnly, alertOnWarning: true));
    }

    [TestMethod]
    public void Kuma_Message_ListsRedThenOrange()
    {
        var message = Kuma.Message(Mixed);
        Assert.IsTrue(message.StartsWith("ROUGE Disque C: : 96 % rempli"));
        Assert.IsTrue(message.Contains("ORANGE Mémoire : 91 % utilisée"));
        Assert.IsFalse(message.Contains("Pare-feu"));
        Assert.AreEqual("OK, 1 points vérifiés", Kuma.Message(new[] { Mixed[2], Mixed[3] }));
    }

    [TestMethod]
    public void Kuma_Message_IsShortened()
    {
        var many = Enumerable.Range(0, 50).Select(i => new Point("Disque", $"D{i}:", Level.Critical, new string('x', 30))).ToArray();
        Assert.AreEqual(Kuma.MaxMessage, Kuma.Message(many).Length);
    }

    [TestMethod]
    public void Kuma_Url_ReplacesExampleParameters_AndEncodes()
    {
        var url = Kuma.BuildUrl("https://kuma.exemple.fr/api/push/AbC123?status=up&msg=OK&ping=", "down", "ROUGE Disque C: : 96 % & plus");
        Assert.AreEqual("https://kuma.exemple.fr/api/push/AbC123?status=down&msg=ROUGE%20Disque%20C%3A%20%3A%2096%20%25%20%26%20plus&ping=", url);
    }

    [TestMethod]
    public void Kuma_Url_RejectsInvalidAddress()
    {
        Assert.IsNull(Kuma.BuildUrl("", "up", "OK"));
        Assert.IsNull(Kuma.BuildUrl("kuma.exemple.fr/api/push/x", "up", "OK"));
        Assert.IsNull(Kuma.BuildUrl("file:///C:/Windows/x", "up", "OK"));
    }

    [TestMethod]
    public void Kuma_Masked_HidesToken()
    {
        var masked = Kuma.Masked("https://kuma.exemple.fr/api/push/SecretToken?status=up");
        Assert.AreEqual("https://kuma.exemple.fr/api/push/…", masked);
        Assert.IsFalse(masked.Contains("SecretToken"));
    }

    // ---------- Résultats ----------

    [TestMethod]
    public void Json_IsEscaped_AndCarriesOverallLevel()
    {
        var settings = Settings.Parse("client = Cabinet \"Martin\"\nposte = PC\\01");
        var json = Results.ToJson(settings, new DateTime(2026, 11, 13, 12, 0, 0), Mixed);
        StringAssert.Contains(json, "\"client\":\"Cabinet \\\"Martin\\\"\"");
        StringAssert.Contains(json, "\"poste\":\"PC\\\\01\"");
        StringAssert.Contains(json, "\"etat\":\"rouge\"");
        StringAssert.Contains(json, "\"niveau\":\"non mesurable\"");
        Assert.AreEqual("a\\nb\\u0001", Results.Escape("a\nb\u0001"));
    }

    // ---------- Tâche planifiée ----------

    [TestMethod]
    public void TaskXml_IsValid_SystemAccount_DailyAndAtBoot()
    {
        var xml = Installer.TaskXml(@"C:\Program Files\JVTECH & Co\Sonde\JvtechSonde.exe", @"C:\Program Files\JVTECH & Co\Sonde");
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.AreEqual("S-1-5-18", doc.Descendants(ns + "UserId").Single().Value, "compte SYSTEM");
        Assert.AreEqual(@"C:\Program Files\JVTECH & Co\Sonde\JvtechSonde.exe", doc.Descendants(ns + "Command").Single().Value);
        Assert.AreEqual("--mesurer", doc.Descendants(ns + "Arguments").Single().Value);
        Assert.AreEqual(1, doc.Descendants(ns + "CalendarTrigger").Count());
        Assert.AreEqual(1, doc.Descendants(ns + "BootTrigger").Count());
        Assert.AreEqual("PT5M", doc.Descendants(ns + "ExecutionTimeLimit").Single().Value);
        Assert.AreEqual("true", doc.Descendants(ns + "StartWhenAvailable").Single().Value, "rattrapage si le PC était éteint");
    }
}
