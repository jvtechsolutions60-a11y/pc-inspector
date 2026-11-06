using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PcMigration.Migration;

/// <summary>Un navigateur : où est son profil, comment l'ouvrir sur sa page des mots de passe.</summary>
internal sealed class Browser
{
    public Browser(string key, string name, string process, bool roaming, string dataFolder, string exe, string passwordsPage, bool chromium)
    {
        Key = key; Name = name; Process = process; Roaming = roaming; DataFolder = dataFolder; Exe = exe;
        PasswordsPage = passwordsPage; Chromium = chromium;
    }

    public string Key { get; }
    public string Name { get; }
    public string Process { get; }
    /// <summary>Profil dans AppData\Roaming (Firefox, Opera) plutôt que AppData\Local.</summary>
    public bool Roaming { get; }
    public string DataFolder { get; }
    public string Exe { get; }
    public string PasswordsPage { get; }
    /// <summary>Chrome, Edge, Brave… : mots de passe chiffrés avec le compte Windows, illisibles sur un autre PC.</summary>
    public bool Chromium { get; }

    public string PathIn(UserProfile profile) => System.IO.Path.Combine(Roaming ? profile.Roaming : profile.Local, DataFolder);

    /// <summary>Caches inutiles à transférer : retéléchargés tout seuls.</summary>
    public static readonly string[] CacheFolders =
    {
        "Cache", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache", "GraphiteDawnCache", "DawnCache", "DawnGraphiteCache",
        "DawnWebGPUCache", "CacheStorage", "ScriptCache", "Crashpad", "component_crx_cache", "Safe Browsing", "cache2",
        "startupCache", "thumbnails", "Crash Reports", "minidumps", "optimization_guide_model_store", "OnDeviceHeadSuggestModel",
    };

    public static readonly Browser[] All =
    {
        new("Chrome", "Google Chrome", "chrome", false, @"Google\Chrome\User Data", "chrome.exe", "chrome://password-manager/settings", true),
        new("Edge", "Microsoft Edge", "msedge", false, @"Microsoft\Edge\User Data", "msedge.exe", "edge://settings/passwords", true),
        new("Brave", "Brave", "brave", false, @"BraveSoftware\Brave-Browser\User Data", "brave.exe", "brave://password-manager/settings", true),
        new("Vivaldi", "Vivaldi", "vivaldi", false, @"Vivaldi\User Data", "vivaldi.exe", "vivaldi://settings/passwords", true),
        new("Opera", "Opera", "opera", true, @"Opera Software\Opera Stable", "opera.exe", "opera://settings/passwords", true),
        // Firefox : profiles.ini + Profiles. Ses mots de passe (logins.json + key4.db) suivent le profil, sans export.
        new("Firefox", "Mozilla Firefox", "firefox", true, @"Mozilla\Firefox", "firefox.exe", "about:logins", false),
    };

    public static Browser? ByKey(string key) => All.FirstOrDefault(b => b.Key == key);

    public bool IsRunning => Tools.IsRunning(Process);

    /// <summary>Profils Chromium (Default, Profile 1…) qui ont au moins un mot de passe enregistré.</summary>
    public bool MayHavePasswords(UserProfile profile)
    {
        var root = PathIn(profile);
        if (!Directory.Exists(root)) return false;
        if (!Chromium) return true;
        try
        {
            var files = Directory.GetFiles(root, "Login Data", SearchOption.AllDirectories).Concat(
                        Directory.GetFiles(root, "Login Data For Account", SearchOption.AllDirectories));
            return files.Any(f => new FileInfo(f).Length > 40 * 1024);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    /// <summary>Ouvre le navigateur sur sa page des mots de passe (export ou import du fichier CSV).</summary>
    public bool OpenPasswordsPage()
    {
        try
        {
            // ShellExecute trouve chrome.exe, msedge.exe… grâce à « App Paths », même hors du PATH.
            System.Diagnostics.Process.Start(new ProcessStartInfo(Exe, PasswordsPage) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException) { return false; }
    }

    public static List<Browser> InstalledFor(UserProfile profile) =>
        All.Where(b => Directory.Exists(b.PathIn(profile))).ToList();
}
