extern alias inspector;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using inspector::PcInspector.Collectors;
using inspector::PcInspector.Models;

namespace JvtechTests;

/// <summary>Santé des disques à partir de compteurs SMART et de journaux NVMe simulés.</summary>
[TestClass]
public class DiskHealthTests
{
    /// <summary>Journal de santé NVMe (log page 02h, 512 octets) d'un SSD en bon état.</summary>
    private static byte[] NvmeLog(byte warning = 0, byte spare = 100, byte spareThreshold = 10, byte used = 3, ulong mediaErrors = 0)
    {
        var log = new byte[512];
        log[0] = warning;
        BitConverter.GetBytes((ushort)310).CopyTo(log, 1); // 37 °C
        log[3] = spare;
        log[4] = spareThreshold;
        log[5] = used;
        BitConverter.GetBytes(12345UL).CopyTo(log, 48);
        BitConverter.GetBytes(1500UL).CopyTo(log, 128);
        BitConverter.GetBytes(4UL).CopyTo(log, 144);
        BitConverter.GetBytes(mediaErrors).CopyTo(log, 160);
        return log;
    }

    [TestMethod]
    public void Nvme_Healthy_NoProblem()
    {
        var r = DiskHealth.JudgeNvme(NvmeLog());
        Assert.AreEqual(0, r.Problems.Count);
        Assert.IsNull(r.Unavailable);
        Assert.IsTrue(r.Lines.Any(l => l.Label == "Température" && l.Value == "37 °C"));
    }

    [TestMethod]
    public void Nvme_CriticalWarning_IsCritical()
    {
        var r = DiskHealth.JudgeNvme(NvmeLog(warning: 0x08));
        Assert.IsTrue(r.Problems.Any(p => p.Severity == Severity.Critical && p.Message.Contains("lecture seule")));
    }

    [TestMethod]
    public void Nvme_MediaErrors_IsCritical()
    {
        var r = DiskHealth.JudgeNvme(NvmeLog(mediaErrors: 2));
        Assert.IsTrue(r.Problems.Any(p => p.Severity == Severity.Critical));
    }

    [TestMethod]
    public void Nvme_SpareBelowThreshold_IsCritical_SpareUsed_IsWarning()
    {
        Assert.IsTrue(DiskHealth.JudgeNvme(NvmeLog(spare: 5, spareThreshold: 10)).Problems.Any(p => p.Severity == Severity.Critical));
        var partial = DiskHealth.JudgeNvme(NvmeLog(spare: 40, spareThreshold: 10));
        Assert.IsTrue(partial.Problems.All(p => p.Severity == Severity.Warning) && partial.Problems.Count == 1);
    }

    [TestMethod]
    public void Nvme_LifeUsed_ShownButNotAProblem()
    {
        var r = DiskHealth.JudgeNvme(NvmeLog(used: 90));
        Assert.IsTrue(r.Lines.Any(l => l.Label == "Vie consommée" && l.Value.StartsWith("!")));
    }

    [TestMethod]
    public void Unchecked_IsNotAFailure()
    {
        // Délai dépassé ou pilote incompatible : « contrôle impossible », jamais « défaillant ».
        var r = new DiskHealthResult();
        r.Unchecked("État de santé", "le disque n'a pas répondu à temps");
        Assert.AreEqual(0, r.Problems.Count);
        Assert.IsNotNull(r.Unavailable);
        Assert.IsTrue(r.Lines.Single().Value.StartsWith("? Contrôle impossible"));
    }

    /// <summary>Table SMART ATA simulée : 2 octets d'en-tête puis des entrées de 12 octets.</summary>
    private static byte[] AtaTable(params (byte Id, byte Current, ulong Raw)[] attributes)
    {
        var raw = new byte[2 + 30 * 12];
        for (var i = 0; i < attributes.Length; i++)
        {
            var o = 2 + i * 12;
            raw[o] = attributes[i].Id;
            raw[o + 3] = attributes[i].Current;
            var bytes = BitConverter.GetBytes(attributes[i].Raw);
            Array.Copy(bytes, 0, raw, o + 5, 6);
        }
        return raw;
    }

    private static byte[] Thresholds(params (byte Id, byte Limit)[] limits)
    {
        var raw = new byte[2 + 30 * 12];
        for (var i = 0; i < limits.Length; i++) { raw[2 + i * 12] = limits[i].Id; raw[2 + i * 12 + 1] = limits[i].Limit; }
        return raw;
    }

    [TestMethod]
    public void Ata_ParseAttributes_ReadsSixByteRawValue()
    {
        var attrs = DiskHealth.ParseAttributes(AtaTable((5, 100, 0x0102030405), (9, 99, 20000)));
        Assert.AreEqual(0x0102030405UL, attrs[5].Raw);
        Assert.AreEqual((byte)100, attrs[5].Current);
        Assert.AreEqual(20000UL, attrs[9].Raw);
    }

    [TestMethod]
    public void Ata_Healthy_NoProblem()
    {
        var attrs = DiskHealth.ParseAttributes(AtaTable((5, 100, 0), (197, 100, 0), (198, 100, 0), (199, 200, 0)));
        var r = DiskHealth.JudgeAta(attrs, DiskHealth.ParseThresholds(Thresholds((5, 10))), predictFailure: false);
        Assert.AreEqual(0, r.Problems.Count);
    }

    [TestMethod]
    public void Ata_ReallocatedAndPending()
    {
        var few = DiskHealth.JudgeAta(DiskHealth.ParseAttributes(AtaTable((5, 100, 3))), new Dictionary<byte, byte>(), false);
        Assert.AreEqual(Severity.Warning, few.Problems.Single().Severity);
        var many = DiskHealth.JudgeAta(DiskHealth.ParseAttributes(AtaTable((5, 90, 80))), new Dictionary<byte, byte>(), false);
        Assert.AreEqual(Severity.Critical, many.Problems.Single().Severity);
        var pending = DiskHealth.JudgeAta(DiskHealth.ParseAttributes(AtaTable((197, 100, 1))), new Dictionary<byte, byte>(), false);
        Assert.AreEqual(Severity.Critical, pending.Problems.Single().Severity);
    }

    [TestMethod]
    public void Ata_CrcErrors_AreCableWarningOnly()
    {
        var r = DiskHealth.JudgeAta(DiskHealth.ParseAttributes(AtaTable((199, 200, 50))), new Dictionary<byte, byte>(), false);
        Assert.AreEqual(Severity.Warning, r.Problems.Single().Severity);
        StringAssert.Contains(r.Problems.Single().Message, "câble");
    }

    [TestMethod]
    public void Ata_BelowManufacturerThreshold_IsCritical()
    {
        var attrs = DiskHealth.ParseAttributes(AtaTable((1, 5, 0)));
        var r = DiskHealth.JudgeAta(attrs, DiskHealth.ParseThresholds(Thresholds((1, 6))), false);
        Assert.IsTrue(r.Problems.Any(p => p.Severity == Severity.Critical));
    }

    [TestMethod]
    public void Ata_PredictFailure_IsCritical()
    {
        var r = DiskHealth.JudgeAta(new Dictionary<byte, DiskHealth.SmartAttribute>(), new Dictionary<byte, byte>(), predictFailure: true);
        Assert.AreEqual(Severity.Critical, r.Problems.Single().Severity);
    }
}
