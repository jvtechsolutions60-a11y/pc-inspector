using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PcSecurite.Checks;

/// <summary>
/// Mots de passe enregistrés dans les navigateurs. On compte seulement les entrées : rien n'est déchiffré ni affiché.
/// Les voleurs de mots de passe (infostealers) vident ces coffres en quelques secondes, d'où le conseil d'un vrai
/// gestionnaire de mots de passe.
/// </summary>
public static partial class SecurityChecks
{
    private static readonly string[] PasswordManagers = { "Bitwarden", "KeePass", "1Password", "Dashlane", "NordPass", "Proton Pass", "Keeper", "LastPass" };

    private static SecurityCheck SavedPasswords()
    {
        const string name = "Mots de passe des navigateurs";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var counts = new List<(string Browser, int Count)>();
        foreach (var (browser, folder) in new[]
        {
            ("Chrome", Path.Combine(local, @"Google\Chrome\User Data")),
            ("Edge", Path.Combine(local, @"Microsoft\Edge\User Data")),
            ("Brave", Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data")),
            ("Vivaldi", Path.Combine(local, @"Vivaldi\User Data")),
            ("Opera", Path.Combine(roaming, @"Opera Software")),
        })
        {
            var total = ChromiumLoginFiles(folder).Sum(CountChromiumLogins);
            if (total > 0) counts.Add((browser, total));
        }
        var firefox = Path.Combine(roaming, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(firefox))
        {
            var total = Directory.GetDirectories(firefox).Select(p => Path.Combine(p, "logins.json")).Where(File.Exists).Sum(CountFirefoxLogins);
            if (total > 0) counts.Add(("Firefox", total));
        }

        var manager = InstalledPrograms().Select(p => p.Name)
            .FirstOrDefault(n => PasswordManagers.Any(m => n.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0));
        var managerText = manager is null ? "" : $" Gestionnaire installé : {manager}.";

        if (counts.Count == 0)
            return SecurityCheck.Ok(name, "Aucun mot de passe enregistré dans les navigateurs." + managerText);

        var detail = string.Join(", ", counts.Select(c => $"{c.Browser} : {c.Count}")) + " mot(s) de passe enregistré(s)." + managerText;
        var advice = manager is null
            ? "Installer un gestionnaire de mots de passe (Bitwarden gratuit, ou KeePass), y importer les mots de passe du navigateur, puis les effacer du navigateur et désactiver « Proposer d'enregistrer les mots de passe »."
            : $"Importer ces mots de passe dans {manager}, puis les effacer du navigateur et désactiver « Proposer d'enregistrer les mots de passe ».";
        return SecurityCheck.Warning(name, detail, advice);
    }

    /// <summary>Fichiers « Login Data » de chaque profil (Default, Profile 1…) d'un navigateur basé sur Chromium.</summary>
    private static IEnumerable<string> ChromiumLoginFiles(string userData)
    {
        if (!Directory.Exists(userData)) return Enumerable.Empty<string>();
        try
        {
            return Directory.GetFiles(userData, "Login Data", SearchOption.AllDirectories)
                .Where(f => Path.GetDirectoryName(f)!.Length - userData.Length < 60); // profils, pas les sauvegardes profondes
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Enumerable.Empty<string>(); }
    }

    /// <summary>
    /// Compte les identifiants d'une base « Login Data » avec le moteur SQLite fourni par Windows (winsqlite3.dll).
    /// La base est copiée d'abord : le navigateur ouvert la verrouille.
    /// </summary>
    private static int CountChromiumLogins(string path)
    {
        var copy = Path.Combine(Path.GetTempPath(), $"jvtech-logins-{Guid.NewGuid():N}.db");
        try
        {
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var target = File.Create(copy))
                source.CopyTo(target);

            if (Sqlite.sqlite3_open_v2(copy, out var db, 1 /* SQLITE_OPEN_READONLY */, IntPtr.Zero) != 0) return 0;
            try
            {
                if (Sqlite.sqlite3_prepare_v2(db, "SELECT COUNT(*) FROM logins WHERE blacklisted_by_user = 0", -1, out var statement, IntPtr.Zero) != 0) return 0;
                try { return Sqlite.sqlite3_step(statement) == 100 /* SQLITE_ROW */ ? Sqlite.sqlite3_column_int(statement, 0) : 0; }
                finally { Sqlite.sqlite3_finalize(statement); }
            }
            finally { Sqlite.sqlite3_close(db); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException) { return 0; }
        finally
        {
            try { File.Delete(copy); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static int CountFirefoxLogins(string path)
    {
        try { return Regex.Matches(File.ReadAllText(path), "\"hostname\"\\s*:").Count; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static class Sqlite
    {
        private const string Dll = "winsqlite3.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false)]
        public static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_close(IntPtr db);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_step(IntPtr statement);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_column_int(IntPtr statement, int column);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_finalize(IntPtr statement);
    }
}
