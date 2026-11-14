extern alias inspector;
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using inspector::PcInspector.Collectors;
using inspector::PcInspector.Models;

namespace JvtechTests;

/// <summary>Âge du pilote de la carte graphique (données simulées).</summary>
[TestClass]
public class GpuDriverTests
{
    private static readonly DateTime Today = new(2026, 11, 12);

    [DataTestMethod]
    [DataRow("32.0.15.6094", "560.94")]
    [DataRow("31.0.15.3623", "536.23")]
    [DataRow("32.0.15.8129", "581.29")]
    [DataRow("26.20.100.7870", null)]
    [DataRow("abc", null)]
    [DataRow(null, null)]
    public void NvidiaVersion(string? windows, string? expected) => Assert.AreEqual(expected, GpuDriver.NvidiaVersion(windows));

    [TestMethod]
    public void Nvidia_Recent_IsOk()
    {
        var v = GpuDriver.Judge("NVIDIA GeForce RTX 4070", "NVIDIA", "32.0.15.8129", Today.AddDays(-30), Today)!;
        Assert.AreEqual(Severity.Ok, v.Severity);
        Assert.AreEqual("581.29", v.Version);
    }

    [TestMethod]
    public void Nvidia_SixMonthsAndOneYear_AreWarnings()
    {
        Assert.AreEqual(Severity.Warning, GpuDriver.Judge("NVIDIA GeForce GTX 1660", "NVIDIA", "32.0.15.6094", Today.AddDays(-200), Today)!.Severity);
        var old = GpuDriver.Judge("NVIDIA GeForce GTX 1660", "NVIDIA", "31.0.15.3623", Today.AddDays(-500), Today)!;
        Assert.AreEqual(Severity.Warning, old.Severity);
        StringAssert.Contains(old.Message, "plus d'un an");
        Assert.AreEqual(GpuDriver.NvidiaUrl, old.DownloadUrl);
    }

    [TestMethod]
    public void Amd_ShowsAdrenalinVersion()
    {
        var v = GpuDriver.Judge("AMD Radeon RX 7800 XT", "Advanced Micro Devices, Inc.", "32.0.21013.1000", Today.AddDays(-20), Today, "25.10.2")!;
        Assert.AreEqual("AMD", v.Vendor);
        Assert.AreEqual("Adrenalin 25.10.2", v.Version);
        Assert.AreEqual(Severity.Ok, v.Severity);
    }

    [TestMethod]
    public void Intel_OnlyAfterTwoYears()
    {
        Assert.AreEqual(Severity.Ok, GpuDriver.Judge("Intel(R) UHD Graphics 620", "Intel Corporation", "31.0.101.2111", Today.AddDays(-400), Today)!.Severity);
        Assert.AreEqual(Severity.Warning, GpuDriver.Judge("Intel(R) UHD Graphics 620", "Intel Corporation", "26.20.100.7870", Today.AddDays(-900), Today)!.Severity);
    }

    [TestMethod]
    public void BasicDisplayDriver_IsCritical()
    {
        Assert.AreEqual(Severity.Critical, GpuDriver.Judge("Microsoft Basic Display Adapter", "(Standard display types)", "10.0.22621.1", Today.AddDays(-10), Today)!.Severity);
        Assert.AreEqual(Severity.Critical, GpuDriver.Judge("Carte vidéo de base Microsoft", "(Types d'écran standard)", "10.0.22621.1", Today.AddDays(-10), Today)!.Severity);
    }

    [TestMethod]
    public void VirtualAdapters_AreIgnored()
    {
        Assert.IsNull(GpuDriver.Judge("Microsoft Remote Display Adapter", "Microsoft", "10.0.1", Today, Today));
        Assert.IsNull(GpuDriver.Judge("Microsoft Hyper-V Video", "Microsoft", "10.0.1", Today, Today));
    }

    [TestMethod]
    public void UnknownDate_IsWarning() =>
        Assert.AreEqual(Severity.Warning, GpuDriver.Judge("NVIDIA GeForce RTX 3060", "NVIDIA", "32.0.15.6094", null, Today)!.Severity);
}

/// <summary>Audit M01 : le test d'écriture disque (1 Go) est sauté si l'espace libre est faible ou inconnu.</summary>
[TestClass]
public class DiskWriteGuardTests
{
    [DataTestMethod]
    [DataRow(null, false)]
    [DataRow(0L, false)]
    [DataRow(4L * 1024 * 1024 * 1024, false)]
    [DataRow(5L * 1024 * 1024 * 1024, true)]
    [DataRow(200L * 1024 * 1024 * 1024, true)]
    public void FreeSpaceRequired(long? free, bool allowed) => Assert.AreEqual(allowed, Benchmark.DiskWriteAllowed(free));
}
