using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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
