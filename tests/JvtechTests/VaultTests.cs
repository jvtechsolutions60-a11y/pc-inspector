extern alias migration;
using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using migration::PcMigration.Migration;

namespace JvtechTests;

/// <summary>Chiffrement des secrets de la migration (fichiers dans un dossier temporaire du test, supprimé à la fin).</summary>
[TestClass]
public class VaultTests
{
    private string _folder = "";

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_folder = Path.Combine(Path.GetTempPath(), "JvtechTests-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Delete() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    [TestMethod]
    public void RoundTrip()
    {
        var plain = Encoding.UTF8.GetBytes("url,username,password\nhttps://banque.fr,client,MotDePasse!");
        var file = Path.Combine(_folder, "mots-de-passe.csv" + Vault.Extension);
        Vault.Encrypt(plain, file, "phrase de passe du technicien");
        CollectionAssert.AreEqual(plain, Vault.Decrypt(file, "phrase de passe du technicien"));
        Assert.IsFalse(Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains("MotDePasse"), "le fichier ne contient pas le texte en clair");
    }

    [TestMethod]
    public void WrongPassword_ReturnsNull()
    {
        var file = Path.Combine(_folder, "wifi.xml" + Vault.Extension);
        Vault.Encrypt(Encoding.UTF8.GetBytes("<keyMaterial>secret</keyMaterial>"), file, "bon");
        Assert.IsNull(Vault.Decrypt(file, "mauvais"));
    }

    [TestMethod]
    public void TamperedFile_ReturnsNull()
    {
        var file = Path.Combine(_folder, "x" + Vault.Extension);
        Vault.Encrypt(new byte[100], file, "pw");
        var data = File.ReadAllBytes(file);
        data[40] ^= 1;
        File.WriteAllBytes(file, data);
        Assert.IsNull(Vault.Decrypt(file, "pw"), "un fichier modifié n'est jamais déchiffré à moitié");
    }

    [TestMethod]
    public void TruncatedOrForeignFile_ReturnsNull()
    {
        var file = Path.Combine(_folder, "court" + Vault.Extension);
        File.WriteAllBytes(file, Encoding.ASCII.GetBytes("JVENC1"));
        Assert.IsNull(Vault.Decrypt(file, "pw"));
        File.WriteAllBytes(file, new byte[200]);
        Assert.IsNull(Vault.Decrypt(file, "pw"));
    }

    [TestMethod]
    public void SameInput_DifferentCiphertext()
    {
        var a = Path.Combine(_folder, "a"); var b = Path.Combine(_folder, "b");
        Vault.Encrypt(new byte[10], a, "pw");
        Vault.Encrypt(new byte[10], b, "pw");
        CollectionAssert.AreNotEqual(File.ReadAllBytes(a), File.ReadAllBytes(b), "sel et IV aléatoires");
    }

    [TestMethod]
    public void PasswordCheckFile()
    {
        Vault.WriteCheck(_folder, "pw");
        Assert.IsTrue(Vault.HasCheck(_folder));
        Assert.IsTrue(Vault.CheckPassword(_folder, "pw"));
        Assert.IsFalse(Vault.CheckPassword(_folder, "PW"));
    }

    [TestMethod]
    public void EncryptAndWipe_RemovesPlainFile()
    {
        var plain = Path.Combine(_folder, "export.csv");
        File.WriteAllText(plain, "secret");
        var encrypted = Vault.EncryptAndWipe(plain, "pw");
        Assert.IsFalse(File.Exists(plain));
        Assert.AreEqual("secret", Encoding.UTF8.GetString(Vault.Decrypt(encrypted, "pw")!));
    }

    [TestMethod]
    public void WiFiNameFromXml()
    {
        Assert.AreEqual("Livebox-1234", Backup.WiFiNameFromXml("<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>Livebox-1234</name></WLANProfile>"));
    }
}
