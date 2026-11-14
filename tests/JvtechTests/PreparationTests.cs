extern alias maintenance;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using maintenance::PcMaintenance.Preparation;

namespace JvtechTests;

/// <summary>Garde-fou de « /preparation » : jamais lancée ni Sysprep hors mode audit sans confirmation explicite.</summary>
[TestClass]
public class PreparationTests
{
    [DataTestMethod]
    [DataRow(1, "IMAGE_STATE_UNDEPLOYABLE", SetupMode.Audit)]
    [DataRow(1, "IMAGE_STATE_SPECIALIZE_RESEAL_TO_AUDIT", SetupMode.Audit)]
    [DataRow(1, "IMAGE_STATE_GENERALIZE_RESEAL_TO_AUDIT", SetupMode.Audit)]
    [DataRow(1, null, SetupMode.Unknown)]                          // ImageState absent : on ne devine pas (audit C02)
    [DataRow(1, "", SetupMode.Unknown)]
    [DataRow(1, "valeur inattendue", SetupMode.Unknown)]
    [DataRow(1, "IMAGE_STATE_COMPLETE", SetupMode.Unknown)]       // indicateurs contradictoires : refus
    [DataRow(null, "IMAGE_STATE_UNDEPLOYABLE", SetupMode.Unknown)] // ImageState seul : on ne devine pas
    [DataRow(0, "IMAGE_STATE_COMPLETE", SetupMode.Normal)]
    [DataRow(null, "IMAGE_STATE_COMPLETE", SetupMode.Normal)]
    [DataRow(0, null, SetupMode.Normal)]
    [DataRow(null, null, SetupMode.Unknown)]
    [DataRow(null, "valeur inattendue", SetupMode.Unknown)]
    public void Classify(int? audit, string? imageState, object expected) =>
        Assert.AreEqual((SetupMode)expected, PreparationGuard.Classify(audit, imageState));

    [TestMethod]
    public void AuditMode_IsAllowed_AndResealFollowsOption()
    {
        var on = PreparationGuard.Decide(SetupMode.Audit, null, "PC-1", resealOption: true);
        Assert.IsTrue(on.Allowed);
        Assert.IsTrue(on.ResealAllowed);
        var off = PreparationGuard.Decide(SetupMode.Audit, null, "PC-1", resealOption: false);
        Assert.IsTrue(off.Allowed);
        Assert.IsFalse(off.ResealAllowed);
    }

    [DataTestMethod]
    [DataRow(SetupMode.Normal)]
    [DataRow(SetupMode.Unknown)]
    public void OutsideAudit_WithoutConfirmation_IsRefused(object mode)
    {
        var permission = PreparationGuard.Decide((SetupMode)mode, null, "PC-CLIENT", resealOption: true);
        Assert.IsFalse(permission.Allowed);
        Assert.IsFalse(permission.ResealAllowed);
    }

    [DataTestMethod]
    [DataRow(SetupMode.Normal)]
    [DataRow(SetupMode.Unknown)]
    public void OutsideAudit_Confirmed_IsAllowed_ButNeverSysprep(object mode)
    {
        var permission = PreparationGuard.Decide((SetupMode)mode, "pc-revente ", "PC-REVENTE", resealOption: true);
        Assert.IsTrue(permission.Allowed);
        Assert.IsFalse(permission.ResealAllowed, "Sysprep ne doit jamais être autorisé hors mode audit");
    }

    [TestMethod]
    public void ConfirmationForAnotherPc_DoesNotCarryOver()
    {
        // Le kit copié d'un PC à l'autre garde etat.ini : la confirmation ne vaut que pour le PC confirmé.
        var permission = PreparationGuard.Decide(SetupMode.Normal, "PC-REVENTE", "PC-CLIENT", resealOption: false);
        Assert.IsFalse(permission.Allowed);
    }

    [DataTestMethod]
    [DataRow("PC-1", "PC-1", true)]
    [DataRow(" pc-1 ", "PC-1", true)]
    [DataRow("", "PC-1", false)]
    [DataRow("   ", "PC-1", false)]
    [DataRow(null, "PC-1", false)]
    [DataRow("PC-", "PC-1", false)]
    [DataRow("oui", "PC-1", false)]
    public void Confirmation_RequiresExactMachineName(string? typed, string machine, bool expected) =>
        Assert.AreEqual(expected, PreparationGuard.IsConfirmation(typed, machine));

    [TestMethod]
    public void Options_WithoutFile_NeverReseal()
    {
        var options = PreparationOptions.Parse(new string[0]);
        Assert.IsFalse(options.Reseal);
        Assert.IsTrue(options.Debloat);
    }

    [DataTestMethod]
    [DataRow("PremierDemarrage=1", true)]
    [DataRow("PremierDemarrage = 1", true)]
    [DataRow("premierdemarrage=1", true)]
    [DataRow("PremierDemarrage=0", false)]
    [DataRow("PremierDemarrage=oui", false)]
    [DataRow("PremierDemarrage=", false)]
    [DataRow("PremierDemarrage=11", false)]
    [DataRow("# PremierDemarrage=1", false)]
    [DataRow("PremierDemarage=1", false)]
    public void Options_ResealOnlyWithExactValue(string line, bool expected) =>
        Assert.AreEqual(expected, PreparationOptions.Parse(new[] { line }).Reseal);

    [TestMethod]
    public void Options_MaxRoundsCapped()
    {
        Assert.AreEqual(20, PreparationOptions.Parse(new[] { "PassagesMax=500" }).MaxRounds);
        Assert.AreEqual(8, PreparationOptions.Parse(new[] { "PassagesMax=-3" }).MaxRounds);
        Assert.AreEqual(3, PreparationOptions.Parse(new[] { "PassagesMax=3" }).MaxRounds);
    }
}

/// <summary>Relevé et remise exacte des valeurs de registre changées par la préparation (audit I02).</summary>
[TestClass]
public class PolicyBackupTests
{
    [DataTestMethod]
    [DataRow(Microsoft.Win32.RegistryValueKind.DWord, 2)]
    [DataRow(Microsoft.Win32.RegistryValueKind.DWord, -1)]
    [DataRow(Microsoft.Win32.RegistryValueKind.String, "valeur | avec : séparateurs")]
    public void EncodeDecode_RoundTrip(Microsoft.Win32.RegistryValueKind kind, object value)
    {
        var decoded = PolicyBackup.Decode(PolicyBackup.Encode(kind, value));
        Assert.IsNotNull(decoded);
        Assert.AreEqual(kind, decoded!.Value.Kind);
        Assert.AreEqual(value, decoded.Value.Value);
    }

    [DataTestMethod]
    [DataRow("Autre:Binary")]
    [DataRow("DWord:pas un nombre")]
    [DataRow("n'importe quoi")]
    public void UnknownSnapshot_IsNotRestored(string text) => Assert.IsNull(PolicyBackup.Decode(text));

    [TestMethod]
    public void Snapshot_HasNoPipe_SoEtatIniKeepsIt() =>
        Assert.IsFalse(PolicyBackup.Encode(Microsoft.Win32.RegistryValueKind.String, "a|b").Contains("|"));
}

/// <summary>Seule la fenêtre Sysprep du mode audit est fermée (audit I03).</summary>
[TestClass]
public class SysprepWindowTests
{
    private const string Official = @"C:\Windows\System32\Sysprep\sysprep.exe";

    [DataTestMethod]
    [DataRow(@"C:\Windows\System32\Sysprep\sysprep.exe", @"""C:\Windows\System32\Sysprep\sysprep.exe""", true)]
    [DataRow(@"C:\Windows\System32\Sysprep\sysprep.exe", @"C:\Windows\System32\Sysprep\sysprep.exe", true)]
    [DataRow(@"C:\Windows\System32\Sysprep\sysprep.exe", @"""C:\Windows\System32\Sysprep\sysprep.exe"" /oobe /shutdown /quiet", false)]
    [DataRow(@"C:\Windows\System32\Sysprep\sysprep.exe", @"C:\Windows\System32\Sysprep\sysprep.exe /generalize", false)]
    [DataRow(@"C:\Users\x\Downloads\sysprep.exe", @"C:\Users\x\Downloads\sysprep.exe", false)]
    [DataRow("", "", false)]
    public void OnlyAuditWindow(string path, string commandLine, bool expected) =>
        Assert.AreEqual(expected, maintenance::PcMaintenance.Preparation.PreparationRunner.IsAuditSysprepWindow(path, commandLine, Official));
}

/// <summary>Empreinte des scripts Win11Debloat (audit I09) : même calcul que build.yml, journaux ignorés.</summary>
[TestClass]
public class Win11DebloatHashTests
{
    private static string Fixture()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jvtech-debloat-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, "Regfiles"));
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "Win11Debloat.ps1"), System.Text.Encoding.UTF8.GetBytes("Write-Host 1\n"));
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "Regfiles", "b.reg"), System.Text.Encoding.UTF8.GetBytes("Windows Registry Editor\n"));
        return dir;
    }

    [TestMethod]
    public void KnownValue_SameAsBuildScript()
    {
        var dir = Fixture();
        try
        {
            // Valeur calculée hors de .NET (Python, même règle que build.yml) : les deux calculs doivent concorder.
            Assert.AreEqual("706ae9c663a91b00bebd86927849daae6620736fb3c62a5dedd073e3c64a0d19", Win11Debloat.FolderHash(dir));
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void LogsIgnored_ScriptChangeDetected()
    {
        var dir = Fixture();
        try
        {
            var before = Win11Debloat.FolderHash(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "Win11Debloat.log"), "journal");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "LastUsedSettings.json"), "{}");
            Assert.AreEqual(before, Win11Debloat.FolderHash(dir));
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "Win11Debloat.ps1"), "Remove-Item C:\\ -Recurse\n");
            Assert.AreNotEqual(before, Win11Debloat.FolderHash(dir));
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void NoExpectedHash_InTestBuild() => Assert.AreEqual("", Win11Debloat.ExpectedHash);
}
