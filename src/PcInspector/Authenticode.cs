using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;

namespace PcInspector;

/// <summary>
/// Vérifie la signature numérique d'un fichier téléchargé avant de l'utiliser (AdwCleaner, mises à jour Windows).
/// WinVerifyTrust contrôle que le fichier n'a pas été modifié depuis sa signature et que le certificat est reconnu
/// par Windows ; ensuite on vérifie que le signataire est bien l'éditeur attendu. Lire seulement le certificat ne
/// suffit pas : un fichier modifié garde le certificat de l'original.
/// </summary>
public static class Authenticode
{
    public static bool IsSignedBy(string path, string publisher)
    {
        if (!File.Exists(path) || !IsTrusted(path)) return false;
        try
        {
            var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            return certificate.GetNameInfo(X509NameType.SimpleName, false).IndexOf(publisher, StringComparison.OrdinalIgnoreCase) >= 0
                   || certificate.Subject.IndexOf("O=" + publisher, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch (CryptographicException) { return false; }
    }

    /// <summary>Signé par un éditeur reconnu par Windows, et pas modifié depuis.</summary>
    public static bool IsSigned(string path) => File.Exists(path) && IsTrusted(path);

    /// <summary>Nom de l'éditeur qui a signé le fichier, ou null.</summary>
    public static string? Publisher(string path)
    {
        try { return new X509Certificate2(X509Certificate.CreateFromSignedFile(path)).GetNameInfo(X509NameType.SimpleName, false); }
        catch (CryptographicException) { return null; }
    }

    /// <summary>
    /// Signé par Microsoft, signature intégrée au fichier OU signature par catalogue. La plupart des programmes de
    /// System32 (svchost, rundll32, sc…) n'ont pas de signature dans le fichier : Windows les signe dans un catalogue
    /// (.cat de CatRoot). Ne regarder que la signature intégrée les ferait tous passer pour non signés.
    /// </summary>
    public static bool IsMicrosoftSigned(string path)
    {
        if (!File.Exists(path)) return false;
        if (IsTrusted(path)) return IsMicrosoft(path);
        return CatalogOf(path) is { } catalog && IsMicrosoft(catalog);
    }

    private static bool IsMicrosoft(string signedFile)
    {
        try
        {
            var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(signedFile));
            return certificate.GetNameInfo(X509NameType.SimpleName, false).StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase)
                   && certificate.Subject.IndexOf("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch (CryptographicException) { return false; }
    }

    /// <summary>Catalogue Windows qui contient l'empreinte du fichier, vérifié par WinVerifyTrust ; null sinon.</summary>
    private static string? CatalogOf(string path)
    {
        IntPtr admin = IntPtr.Zero, catalog = IntPtr.Zero;
        try
        {
            if (!CryptCATAdminAcquireContext2(out admin, IntPtr.Zero, "SHA256", IntPtr.Zero, 0)) return null;
            byte[] hash;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                uint size = 0;
                CryptCATAdminCalcHashFromFileHandle2(admin, file.SafeFileHandle, ref size, null, 0);
                if (size == 0) return null;
                hash = new byte[size];
                if (!CryptCATAdminCalcHashFromFileHandle2(admin, file.SafeFileHandle, ref size, hash, 0)) return null;
            }
            var previous = IntPtr.Zero;
            catalog = CryptCATAdminEnumCatalogFromHash(admin, hash, (uint)hash.Length, 0, ref previous);
            if (catalog == IntPtr.Zero) return null;
            var info = new CatalogInfo { StructSize = (uint)Marshal.SizeOf(typeof(CatalogInfo)) };
            if (!CryptCATCatalogInfoFromContext(catalog, ref info, 0)) return null;

            var member = new WinTrustCatalogInfo
            {
                CatalogFilePath = info.CatalogFile,
                MemberTag = BitConverter.ToString(hash).Replace("-", ""),
                MemberFilePath = path,
                CatalogAdmin = admin,
            };
            var memberPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustCatalogInfo)));
            var hashPtr = Marshal.AllocHGlobal(hash.Length);
            try
            {
                Marshal.Copy(hash, 0, hashPtr, hash.Length);
                member.CalculatedFileHash = hashPtr;
                member.CalculatedFileHashSize = (uint)hash.Length;
                Marshal.StructureToPtr(member, memberPtr, false);
                var data = new WinTrustData(memberPtr) { UnionChoice = 2 }; // WTD_CHOICE_CATALOG
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                var result = WinVerifyTrust(IntPtr.Zero, action, data);
                data.StateAction = 2;
                WinVerifyTrust(IntPtr.Zero, action, data);
                return result == 0 ? info.CatalogFile : null;
            }
            finally
            {
                Marshal.DestroyStructure(memberPtr, typeof(WinTrustCatalogInfo));
                Marshal.FreeHGlobal(memberPtr);
                Marshal.FreeHGlobal(hashPtr);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            return null;
        }
        finally
        {
            if (catalog != IntPtr.Zero) CryptCATAdminReleaseCatalogContext(admin, catalog, 0);
            if (admin != IntPtr.Zero) CryptCATAdminReleaseContext(admin, 0);
        }
    }

    [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptCATAdminAcquireContext2(out IntPtr admin, IntPtr subsystem, string hashAlgorithm, IntPtr strongHashPolicy, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr admin, SafeFileHandle file, ref uint hashSize, byte[]? hash, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr admin, byte[] hash, uint hashSize, uint flags, ref IntPtr previous);

    [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptCATCatalogInfoFromContext(IntPtr catalog, ref CatalogInfo info, uint flags);

    [DllImport("wintrust.dll")]
    private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr admin, IntPtr catalog, uint flags);

    [DllImport("wintrust.dll")]
    private static extern bool CryptCATAdminReleaseContext(IntPtr admin, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CatalogInfo
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string CatalogFile;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustCatalogInfo
    {
        public uint StructSize = (uint)Marshal.SizeOf(typeof(WinTrustCatalogInfo));
        public uint CatalogVersion = 0;
        [MarshalAs(UnmanagedType.LPWStr)] public string? CatalogFilePath;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MemberTag;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MemberFilePath;
        public IntPtr MemberFile = IntPtr.Zero;
        public IntPtr CalculatedFileHash = IntPtr.Zero;
        public uint CalculatedFileHashSize;
        public IntPtr CatalogContext = IntPtr.Zero;
        public IntPtr CatalogAdmin = IntPtr.Zero;
    }

    private static bool IsTrusted(string path)
    {
        var fileInfo = new WinTrustFileInfo(path);
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
        try
        {
            Marshal.StructureToPtr(fileInfo, filePtr, false);
            var data = new WinTrustData(filePtr);
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            var result = WinVerifyTrust(IntPtr.Zero, action, data);
            // Libère l'état gardé par Windows pour cette vérification.
            data.StateAction = 2; // WTD_STATEACTION_CLOSE
            WinVerifyTrust(IntPtr.Zero, action, data);
            return result == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException) { return false; }
        finally
        {
            Marshal.DestroyStructure(filePtr, typeof(WinTrustFileInfo));
            Marshal.FreeHGlobal(filePtr);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, [In, Out] WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo
    {
        public WinTrustFileInfo(string path) { FilePath = path; }
        public uint StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
        [MarshalAs(UnmanagedType.LPTStr)] public string FilePath;
        public IntPtr File = IntPtr.Zero;
        public IntPtr KnownSubject = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustData
    {
        public WinTrustData(IntPtr fileInfo) { FileInfo = fileInfo; }
        public uint StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData));
        public IntPtr PolicyCallbackData = IntPtr.Zero;
        public IntPtr SipClientData = IntPtr.Zero;
        public uint UiChoice = 2;               // WTD_UI_NONE : aucune fenêtre
        public uint RevocationChecks = 0;       // WTD_REVOKE_NONE : fonctionne aussi hors ligne
        public uint UnionChoice = 1;            // WTD_CHOICE_FILE
        public IntPtr FileInfo;
        public uint StateAction = 1;            // WTD_STATEACTION_VERIFY
        public IntPtr StateData = IntPtr.Zero;
        [MarshalAs(UnmanagedType.LPTStr)] public string? UrlReference = null;
        public uint ProvFlags = 0x00000040;     // WTD_SAFER_FLAG
        public uint UiContext = 0;
        public IntPtr SignatureSettings = IntPtr.Zero;
    }
}
