using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PcMigration.Migration;

/// <summary>
/// Chiffre les fichiers sensibles de la sauvegarde (mots de passe exportés des navigateurs, clés Wi-Fi) avec le mot
/// de passe choisi par le technicien : AES-256 + HMAC-SHA256, clés dérivées par PBKDF2 (200 000 tours).
/// Un mot de passe faux est détecté (HMAC), le fichier n'est alors jamais déchiffré à moitié.
/// </summary>
internal static class Vault
{
    public const string Extension = ".jvenc";
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("JVENC1");
    private const int Iterations = 200_000;
    private const string CheckFile = "verification" + Extension;
    private const string CheckText = "JVTECH Migration";

    public static void Encrypt(byte[] plain, string destination, string password)
    {
        var salt = Random(16);
        var iv = Random(16);
        var (encKey, macKey) = Keys(password, salt);
        byte[] cipher;
        using (var aes = Aes.Create())
        {
            aes.Key = encKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var encryptor = aes.CreateEncryptor();
            cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
        }
        var body = Magic.Concat(salt).Concat(iv).Concat(cipher).ToArray();
        using var hmac = new HMACSHA256(macKey);
        File.WriteAllBytes(destination, body.Concat(hmac.ComputeHash(body)).ToArray());
    }

    /// <summary>Null si le mot de passe est faux ou le fichier abîmé.</summary>
    public static byte[]? Decrypt(string source, string password)
    {
        var data = File.ReadAllBytes(source);
        if (data.Length < Magic.Length + 16 + 16 + 16 + 32 || !data.Take(Magic.Length).SequenceEqual(Magic)) return null;
        var body = data.Take(data.Length - 32).ToArray();
        var mac = data.Skip(data.Length - 32).ToArray();
        var salt = body.Skip(Magic.Length).Take(16).ToArray();
        var iv = body.Skip(Magic.Length + 16).Take(16).ToArray();
        var (encKey, macKey) = Keys(password, salt);
        using (var hmac = new HMACSHA256(macKey))
        {
            var expected = hmac.ComputeHash(body);
            var diff = 0;
            for (var i = 0; i < 32; i++) diff |= expected[i] ^ mac[i];
            if (diff != 0) return null;
        }
        var cipher = body.Skip(Magic.Length + 32).ToArray();
        using var aes = Aes.Create();
        aes.Key = encKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
    }

    /// <summary>Chiffre un fichier en clair puis l'efface (écrasé avant suppression).</summary>
    public static string EncryptAndWipe(string plainFile, string password)
    {
        var destination = plainFile + Extension;
        Encrypt(File.ReadAllBytes(plainFile), destination, password);
        Tools.Wipe(plainFile);
        return destination;
    }

    /// <summary>Petit fichier témoin : permet de vérifier le mot de passe avant de commencer la restauration.</summary>
    public static void WriteCheck(string backupFolder, string password) =>
        Encrypt(Encoding.UTF8.GetBytes(CheckText), Path.Combine(backupFolder, CheckFile), password);

    public static bool HasCheck(string backupFolder) => File.Exists(Path.Combine(backupFolder, CheckFile));

    public static bool CheckPassword(string backupFolder, string password)
    {
        try
        {
            var plain = Decrypt(Path.Combine(backupFolder, CheckFile), password);
            return plain is not null && Encoding.UTF8.GetString(plain) == CheckText;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException) { return false; }
    }

    private static (byte[] Enc, byte[] Mac) Keys(string password, byte[] salt)
    {
        using var kdf = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        var keys = kdf.GetBytes(64);
        return (keys.Take(32).ToArray(), keys.Skip(32).ToArray());
    }

    private static byte[] Random(int length)
    {
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return bytes;
    }
}
