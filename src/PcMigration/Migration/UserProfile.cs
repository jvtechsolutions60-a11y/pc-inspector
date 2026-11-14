using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using Microsoft.Win32;

namespace PcMigration.Migration;

/// <summary>Un dossier du client à transférer (Bureau, Documents…).</summary>
internal sealed class UserFolder
{
    public UserFolder(string key, string label, string path) { Key = key; Label = label; Path = path; }
    /// <summary>Nom du sous-dossier dans la sauvegarde, identique quelle que soit la langue de Windows.</summary>
    public string Key { get; }
    public string Label { get; }
    public string Path { get; }
}

/// <summary>
/// Un compte Windows et ses dossiers. Le logiciel tourne en administrateur : si le client a un compte standard, le
/// compte « administrateur » n'est pas le sien. On vise donc le compte ouvert à l'écran, pas celui du processus.
/// </summary>
internal sealed class UserProfile
{
    // Valeurs de « User Shell Folders » : elles suivent les dossiers déplacés (OneDrive, autre disque).
    private static readonly (string Key, string Label, string Value, string Default)[] Known =
    {
        ("Desktop", "Bureau", "Desktop", "Desktop"),
        ("Documents", "Documents", "Personal", "Documents"),
        ("Pictures", "Images", "My Pictures", "Pictures"),
        ("Videos", "Vidéos", "My Video", "Videos"),
        ("Music", "Musique", "My Music", "Music"),
        ("Downloads", "Téléchargements", "{374DE290-123F-4565-9164-39C4925E467B}", "Downloads"),
        ("Favorites", "Favoris Internet Explorer", "Favorites", "Favorites"),
    };

    private UserProfile(string name, string sid, string path) { Name = name; Sid = sid; Path = path; }

    public string Name { get; }
    public string Sid { get; }
    public string Path { get; }
    public string Roaming => Folder("AppData", System.IO.Path.Combine(Path, @"AppData\Roaming"));
    public string Local => Folder("Local AppData", System.IO.Path.Combine(Path, @"AppData\Local"));
    public bool IsProcessUser => string.Equals(Sid, WindowsIdentity.GetCurrent().User?.Value, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Name}  ({Path})";

    public List<UserFolder> Folders() =>
        Known.Select(k => new UserFolder(k.Key, k.Label, Folder(k.Value, System.IO.Path.Combine(Path, k.Default))))
             .Where(f => f.Key != "Favorites" || Directory.Exists(f.Path))
             .ToList();

    public UserFolder? FolderByKey(string key) => Folders().FirstOrDefault(f => f.Key == key);

    /// <summary>Chemin d'un dossier du compte, lu dans sa partie du registre quand elle est chargée (compte ouvert).</summary>
    private string Folder(string value, string fallback)
    {
        try
        {
            using var key = Registry.Users.OpenSubKey(Sid + @"\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            if (key?.GetValue(value, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string raw && raw.Length > 0)
            {
                var path = raw.Replace("%USERPROFILE%", Path).Replace("%userprofile%", Path);
                path = Environment.ExpandEnvironmentVariables(path);
                if (System.IO.Path.IsPathRooted(path)) return path;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return fallback;
    }

    /// <summary>Les comptes de ce PC qui ont un dossier dans C:\Users (sans les comptes système).</summary>
    public static List<UserProfile> All()
    {
        var list = new List<UserProfile>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            foreach (var sid in root?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) || sid.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                using var key = root!.OpenSubKey(sid);
                if (key?.GetValue("ProfileImagePath") is not string raw) continue;
                var path = Environment.ExpandEnvironmentVariables(raw);
                if (!Directory.Exists(path)) continue;
                list.Add(new UserProfile(AccountName(sid) ?? System.IO.Path.GetFileName(path), sid, path));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Le compte ouvert à l'écran (celui du client), sinon celui qui a lancé le logiciel.</summary>
    public static UserProfile? Interactive(List<UserProfile> all)
    {
        var sid = ConsoleUserSid() ?? WindowsIdentity.GetCurrent().User?.Value;
        return all.FirstOrDefault(p => string.Equals(p.Sid, sid, StringComparison.OrdinalIgnoreCase)) ?? all.FirstOrDefault();
    }

    private static string? ConsoleUserSid()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT UserName FROM Win32_ComputerSystem");
            foreach (ManagementObject item in searcher.Get())
                if (item["UserName"] is string name && name.Length > 0)
                    return ((SecurityIdentifier)new NTAccount(name).Translate(typeof(SecurityIdentifier))).Value;
        }
        catch (Exception ex) when (ex is ManagementException or IdentityNotMappedException or System.Runtime.InteropServices.COMException
                                       or UnauthorizedAccessException or SystemException) { }
        return null;
    }

    private static string? AccountName(string sid)
    {
        try
        {
            var name = ((NTAccount)new SecurityIdentifier(sid).Translate(typeof(NTAccount))).Value;
            return name.Contains("\\") ? name.Substring(name.IndexOf('\\') + 1) : name;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException) { return null; }
    }
}
