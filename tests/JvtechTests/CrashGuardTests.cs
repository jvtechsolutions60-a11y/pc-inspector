extern alias inspector;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using inspector::PcInspector;

namespace JvtechTests;

/// <summary>Aucun mot de passe ni jeton dans les journaux.</summary>
[TestClass]
public class CrashGuardTests
{
    [DataTestMethod]
    [DataRow("MotDePasse=Azerty123", "MotDePasse=***")]
    [DataRow("MotDePasseProtege=AQAAANCMnd8B", "MotDePasseProtege=***")]
    [DataRow("Jeton=abcDEF_123-xyz", "Jeton=***")]
    [DataRow("X-JVTECH-Jeton: abcdef", "X-JVTECH-Jeton: ***")]
    [DataRow("password: \"mon secret\" suite", "password: *** suite")]
    [DataRow("net use Z: \\\\SERVEUR\\Images /user:jvtechpxe /p:Secret1", "net use Z: \\\\SERVEUR\\Images /user:jvtechpxe /p:***")]
    [DataRow("net user jvtechpxe Secret1 /add", "net user jvtechpxe *** /add")]
    [DataRow("net user jvtechpxe /active:no", "net user jvtechpxe /active:no")]
    [DataRow("Clic : Sauvegarder — PC Migration", "Clic : Sauvegarder — PC Migration")]
    [DataRow("Copie de C:\\Users\\Client\\Documents : 120 fichiers", "Copie de C:\\Users\\Client\\Documents : 120 fichiers")]
    public void Redact(string text, string expected) => Assert.AreEqual(expected, CrashGuard.Redact(text));

    [TestMethod]
    public void Redact_Null() => Assert.AreEqual("", CrashGuard.Redact(null));
}
