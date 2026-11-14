using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PcMigration.Migration;

/// <summary>
/// Dossier de passage des secrets (CSV de mots de passe exporté par le navigateur, profil Wi-Fi quand l'API Windows
/// n'est pas disponible). Règles :
/// - jamais sur le disque externe ni dans un dossier synchronisé (Bureau, Documents peuvent être dans OneDrive) :
///   AppData\Local du compte du client, qui n'est pas synchronisé ;
/// - droits réservés au compte du client, aux administrateurs et à SYSTEM ;
/// - vidé (fichiers écrasés puis supprimés) après chaque élément, à la fin, en cas d'erreur, et au lancement suivant
///   si le logiciel a été coupé net.
/// Sur un SSD, l'écrasement ne garantit pas l'effacement physique : c'est pourquoi le Wi-Fi passe d'abord par la
/// mémoire (<see cref="WlanApi"/>) et que les fichiers en clair ne restent que quelques secondes.
/// </summary>
internal static class Secrets
{
    private const string FolderName = "JVTECH-Migration-secrets";

    public static string FolderFor(UserProfile profile) => Path.Combine(profile.Local, FolderName);

    /// <summary>
    /// Crée le dossier (vide) avec des droits restreints, puis RELIT les droits obtenus. Si les droits ne peuvent pas
    /// être posés ou ne sont pas ceux attendus, le dossier est supprimé et une erreur est levée : aucun secret en clair
    /// n'est alors écrit (l'étape est notée « non faite »).
    /// </summary>
    public static string Create(UserProfile profile)
    {
        var folder = FolderFor(profile);
        Cleanup(profile);
        Directory.CreateDirectory(folder);
        var allowed = new[]
            {
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                TryParse(profile.Sid),
            }.Where(s => s is not null).Select(s => s!).ToList();
        string? problem;
        try
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            foreach (var sid in allowed)
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(folder, security);

            var actual = Directory.GetAccessControl(folder);
            var sids = actual.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
                .Where(r => r.AccessControlType == AccessControlType.Allow)
                .Select(r => (SecurityIdentifier)r.IdentityReference).ToList();
            problem = !actual.AreAccessRulesProtected ? "héritage des droits non coupé"
                : HasOnlyAllowed(sids, allowed) ? null : "droits inattendus sur le dossier";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or IOException or ArgumentException or PrivilegeNotHeldException or IdentityNotMappedException)
        {
            problem = ex.Message;
        }
        if (problem is null) return folder;
        Cleanup(profile);
        throw new InvalidOperationException($"dossier protégé pour les secrets impossible à créer ({problem}) : rien n'a été écrit en clair");
    }

    /// <summary>Vrai si chaque autorisation vise un compte permis (SYSTEM, Administrateurs, le client).</summary>
    internal static bool HasOnlyAllowed(IEnumerable<SecurityIdentifier> granted, IReadOnlyCollection<SecurityIdentifier> allowed) =>
        granted.All(allowed.Contains);

    /// <summary>Écrase et supprime tout ce qui reste dans le dossier de passage, puis le dossier. Ne lève jamais d'erreur.</summary>
    public static void Cleanup(UserProfile profile)
    {
        try
        {
            var folder = FolderFor(profile);
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) Tools.Wipe(file);
            Directory.Delete(folder, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static SecurityIdentifier? TryParse(string sid)
    {
        try { return new SecurityIdentifier(sid); }
        catch (ArgumentException) { return null; }
    }
}

/// <summary>
/// Profils Wi-Fi lus et ajoutés par l'API Windows (wlanapi.dll), entièrement en mémoire : la clé en clair n'est
/// jamais écrite sur un disque. Lecture de la clé : réservée aux administrateurs (le logiciel l'est).
/// </summary>
internal static class WlanApi
{
    private const uint ClientVersion = 2;
    private const uint GetPlaintextKey = 4;      // WLAN_PROFILE_GET_PLAINTEXT_KEY
    private const uint AllUserProfile = 0;       // dwFlags de WlanSetProfile : profil pour tous les utilisateurs
    private const int InterfaceInfoSize = 16 + 512 + 4;  // GUID + WCHAR[256] + état
    private const int ProfileInfoSize = 512 + 4;         // WCHAR[256] + flags

    /// <summary>Profils (nom, XML avec la clé en clair). Null si l'API Wi-Fi n'est pas disponible sur ce PC.</summary>
    public static List<(string Name, string Xml)>? ReadProfiles()
    {
        if (WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out var handle) != 0) return null;
        try
        {
            var result = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var iface in Interfaces(handle))
            {
                var guid = iface;
                if (WlanGetProfileList(handle, ref guid, IntPtr.Zero, out var list) != 0) continue;
                try
                {
                    var count = Marshal.ReadInt32(list);
                    for (var i = 0; i < count; i++)
                    {
                        var name = Marshal.PtrToStringUni(list + 8 + i * ProfileInfoSize) ?? "";
                        if (name.Length == 0 || !seen.Add(name)) continue;
                        var flags = GetPlaintextKey;
                        if (WlanGetProfile(handle, ref guid, name, IntPtr.Zero, out var xmlPtr, ref flags, out _) != 0) continue;
                        try { result.Add((name, Marshal.PtrToStringUni(xmlPtr) ?? "")); }
                        finally { WlanFreeMemory(xmlPtr); }
                    }
                }
                finally { WlanFreeMemory(list); }
            }
            return result;
        }
        finally { WlanCloseHandle(handle, IntPtr.Zero); }
    }

    /// <summary>Ajoute un profil pour tous les utilisateurs sur la première carte Wi-Fi. Null si réussi, sinon la raison.</summary>
    public static string? AddProfile(string xml)
    {
        if (WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out var handle) != 0) return "API Wi-Fi indisponible";
        try
        {
            var first = Interfaces(handle).Cast<Guid?>().FirstOrDefault();
            if (first is not { } guid) return "aucune carte Wi-Fi";
            var code = WlanSetProfile(handle, ref guid, AllUserProfile, xml, null, true, IntPtr.Zero, out var reason);
            return code == 0 ? null : $"erreur {code} (raison {reason})";
        }
        finally { WlanCloseHandle(handle, IntPtr.Zero); }
    }

    private static List<Guid> Interfaces(IntPtr handle)
    {
        var guids = new List<Guid>();
        if (WlanEnumInterfaces(handle, IntPtr.Zero, out var list) != 0) return guids;
        try
        {
            var count = Marshal.ReadInt32(list);
            for (var i = 0; i < count; i++)
                guids.Add(Marshal.PtrToStructure<Guid>(list + 8 + i * InterfaceInfoSize));
        }
        finally { WlanFreeMemory(list); }
        return guids;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr handle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetProfileList(IntPtr handle, ref Guid interfaceGuid, IntPtr reserved, out IntPtr profileList);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanGetProfile(IntPtr handle, ref Guid interfaceGuid, string profileName, IntPtr reserved,
        out IntPtr profileXml, ref uint flags, out uint grantedAccess);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanSetProfile(IntPtr handle, ref Guid interfaceGuid, uint flags, string profileXml, string? allUserProfileSecurity,
        bool overwrite, IntPtr reserved, out uint reasonCode);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
