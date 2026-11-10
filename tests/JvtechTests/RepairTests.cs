extern alias maintenance;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using maintenance::PcMaintenance.Maintenance;

namespace JvtechTests;

/// <summary>Interprétation de DISM et SFC à partir de codes et de textes simulés (rien n'est lancé).</summary>
[TestClass]
public class RepairTests
{
    [TestMethod]
    public void Dism_Codes()
    {
        Assert.AreEqual((true, false), Pick(Repair.DismMeaning(true, false, 0)));
        Assert.AreEqual((true, true), Pick(Repair.DismMeaning(true, false, 3010)), "3010 = réparé, redémarrage nécessaire : une réussite");
        Assert.AreEqual((false, true), Pick(Repair.DismMeaning(true, false, unchecked((int)0x800F082F))));
        Assert.AreEqual((false, false), Pick(Repair.DismMeaning(true, false, unchecked((int)0x800F081F))));
        Assert.AreEqual((false, false), Pick(Repair.DismMeaning(true, true, null)), "délai dépassé : jamais une réussite");
        Assert.AreEqual((false, false), Pick(Repair.DismMeaning(false, false, null)));
        Assert.AreEqual((false, false), Pick(Repair.DismMeaning(true, false, 87)));
    }

    private static (bool Ok, bool Reboot) Pick((string Text, bool Ok, bool Reboot) m) => (m.Ok, m.Reboot);

    [DataTestMethod]
    [DataRow("La protection des ressources Windows n'a trouvé aucune violation d'intégrité.", "Clean")]
    [DataRow("Windows Resource Protection did not find any integrity violations.", "Clean")]
    [DataRow("La protection des ressources Windows a trouvé des fichiers corrompus et les a correctement réparés.", "Repaired")]
    [DataRow("Le programme de protection des ressources Windows a trouvé des fichiers corrompus et les a réparés.", "Repaired")]
    [DataRow("Windows Resource Protection found corrupt files and successfully repaired them.", "Repaired")]
    [DataRow("La protection des ressources Windows a trouvé des fichiers corrompus, mais n'a pas pu en réparer certains.", "Unrepairable")]
    [DataRow("Windows Resource Protection found corrupt files but was unable to fix some of them.", "Unrepairable")]
    [DataRow("La protection des ressources Windows n'a pas pu effectuer l'opération demandée.", "CouldNotRun")]
    [DataRow("Windows Resource Protection could not perform the requested operation.", "CouldNotRun")]
    [DataRow("Ochrona zasobów systemu Windows nie znalazła żadnych naruszeń integralności.", "Unknown")]
    [DataRow("", "Unknown")]
    public void Sfc_Output(string text, string expected) =>
        Assert.AreEqual(expected, Repair.SfcFromOutput(text).ToString());

    [TestMethod]
    public void Sfc_Cbs()
    {
        Assert.AreEqual("Unknown", Repair.SfcFromCbs(new[] { "2026-11-10 10:00:00, Info CBS Starting" }).ToString());
        Assert.AreEqual("Clean", Repair.SfcFromCbs(new[] { "2026-11-10 10:00:00, Info CSI 00000001 [SR] Verify complete" }).ToString());
        Assert.AreEqual("Repaired", Repair.SfcFromCbs(new[]
        {
            "2026-11-10 10:00:00, Info CSI 00000001 [SR] Repairing corrupted file \\??\\C:\\Windows\\x.dll",
            "2026-11-10 10:00:01, Info CSI 00000002 [SR] Verify complete",
        }).ToString());
        Assert.AreEqual("Unrepairable", Repair.SfcFromCbs(new[]
        {
            "2026-11-10 10:00:00, Info CSI 00000001 [SR] Repairing corrupted file x",
            "2026-11-10 10:00:00, Info CSI 00000002 [SR] Cannot repair member file x",
        }).ToString());
    }

    [TestMethod]
    public void Sfc_UnknownVerdict_IsNeverASuccess()
    {
        Assert.IsFalse(Repair.SfcMeaning(Repair.SfcVerdict.Unknown, false, "texte inattendu").Ok);
        Assert.IsFalse(Repair.SfcMeaning(Repair.SfcVerdict.PendingReboot, false, "").Ok);
        Assert.IsFalse(Repair.SfcMeaning(Repair.SfcVerdict.Clean, true, "").Ok, "délai dépassé : résultat inconnu");
        Assert.IsTrue(Repair.SfcMeaning(Repair.SfcVerdict.Clean, false, "").Ok);
        Assert.IsTrue(Repair.SfcMeaning(Repair.SfcVerdict.Repaired, false, "").Ok);
    }
}
