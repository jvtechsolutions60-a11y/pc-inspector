extern alias inspector;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using inspector::PcInspector.Collectors;

namespace JvtechTests;

/// <summary>« Mauvais tweaks » (sorties de bcdedit et valeurs de registre simulées).</summary>
[TestClass]
public class TweaksTests
{
    private const string CleanBcd = "Chargeur de démarrage Windows\n-------------------\nidentificateur          {current}\ndevice                  partition=C:\nnx                      OptIn\n";

    [TestMethod]
    public void DefaultPc_NothingReported() =>
        Assert.AreEqual(0, Tweaks.Judge(CleanBcd, null, 0, "381b4222-f694-41f0-9685-ff5bb260df2e", "Intel(R) Core(TM) i5-12400").Count);

    [TestMethod]
    public void UnreadValues_NothingReported() => Assert.AreEqual(0, Tweaks.Judge(null, null, null, null, null).Count);

    [TestMethod]
    public void HpetTweak_Reported()
    {
        var found = Tweaks.Judge(CleanBcd + "useplatformclock        Oui\ndisabledynamictick      Yes\n", null, null, null, null);
        Assert.AreEqual(1, found.Count);
        StringAssert.Contains(found[0].Fix, "bcdedit /deletevalue {current} useplatformclock");
        StringAssert.Contains(found[0].Fix, "disabledynamictick");
        Assert.IsFalse(found[0].Fix.Contains("useplatformtick"));
    }

    [TestMethod]
    public void BcdKey_MustMatchWholeWord() => Assert.IsFalse(Tweaks.BcdHas("useplatformclockXYZ  Oui", "useplatformclock"));

    [TestMethod]
    public void MaintenanceAndTrim_Reported()
    {
        var found = Tweaks.Judge(CleanBcd, 1, 1, null, null);
        Assert.AreEqual(2, found.Count);
        StringAssert.Contains(found[1].Fix, "DisableDeleteNotify 0");
    }

    [TestMethod]
    public void UltimatePlan_OnlyOnRecentCpu()
    {
        Assert.AreEqual(1, Tweaks.Judge(null, null, null, Tweaks.UltimatePerformance, "AMD Ryzen 7 5800X 8-Core Processor").Count);
        Assert.AreEqual(0, Tweaks.Judge(null, null, null, Tweaks.UltimatePerformance, "AMD Ryzen 5 3600 6-Core Processor").Count);
    }

    [DataTestMethod]
    [DataRow("Intel(R) Core(TM) i7-12700K", true)]
    [DataRow("12th Gen Intel(R) Core(TM) i5-1235U", true)]
    [DataRow("Intel(R) Core(TM) Ultra 7 155H", true)]
    [DataRow("11th Gen Intel(R) Core(TM) i5-1135G7 @ 2.40GHz", false)]
    [DataRow("Intel(R) Core(TM) i7-8700 CPU @ 3.20GHz", false)]
    [DataRow("AMD Ryzen 7 PRO 4750U with Radeon Graphics", false)]
    [DataRow("AMD Ryzen 9 7950X 16-Core Processor", true)]
    [DataRow("Processeur inconnu", false)]
    [DataRow(null, false)]
    public void RecentCpu(string? name, bool expected) => Assert.AreEqual(expected, Tweaks.IsRecentCpu(name));
}
