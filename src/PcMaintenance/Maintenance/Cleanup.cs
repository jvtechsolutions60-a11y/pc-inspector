using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;
using PcInspector;

namespace PcMaintenance.Maintenance;

/// <summary>Ce que le nettoyage a fait, pour le journal et le rapport.</summary>
internal sealed class CleanupResult
{
    public ulong Freed { get; set; }
    public List<string> Done { get; } = new();
    public List<string> MovedShortcuts { get; } = new();
    public List<string> Startup { get; } = new();
}

/// <summary>
/// Nettoyage intégré qui remplace la maintenance 1 clic de Glary. Il allège le PC sans toucher à l'environnement du
/// client : favoris, mots de passe, historique, cookies, corbeille et Téléchargements ne sont jamais touchés, pas de
/// nettoyage du registre. Les raccourcis cassés sont mis de côté, pas supprimés.
/// </summary>
internal static class Cleanup
{
    // Catégories du nettoyage de disque de Windows (cleanmgr). Volontairement absentes : Corbeille, Téléchargements,
    // anciennes installations de Windows (Windows.old, qui sert à revenir en arrière).
    private static readonly string[] DiskCleanupCategories =
    {
        "Temporary Files", "Temporary Setup Files", "Thumbnail Cache", "Delivery Optimization Files",
        // « Update Cleanup » (anciennes versions des mises à jour, WinSxS) est retiré : à lui seul il prenait 10 à 30 minutes.
        // La préparation revente le fait avec DISM, au moment où le temps compte moins.
        "Windows Error Reporting Files", "Windows Upgrade Log Files", "Setup Log Files",
        "D3D Shader Cache", "Old ChkDsk Files", "System error memory dump files", "System error minidump files",
        "Downloaded Program Files", "Internet Cache Files",
    };
    private const string SageSet = "0777";
    private const string VolumeCaches = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";

    private static readonly (string Name, string Process, string UserData)[] Chromium =
    {
        ("Chrome", "chrome", @"Google\Chrome\User Data"),
        ("Edge", "msedge", @"Microsoft\Edge\User Data"),
        ("Brave", "brave", @"BraveSoftware\Brave-Browser\User Data"),
        ("Vivaldi", "vivaldi", @"Vivaldi\User Data"),
    };

    public static CleanupResult Run(IProgress<string> log)
    {
        var result = new CleanupResult();
        Step(result, log, "Fichiers temporaires", TempFiles);
        Step(result, log, "Cache de Windows Update", l => WindowsUpdateCache(l));
        Step(result, log, "Nettoyage de disque Windows", DiskCleanup);
        Step(result, log, "Caches des navigateurs", BrowserCaches);
        Guard(result, log, "Raccourcis cassés", () => BrokenShortcuts(result, log));
        Guard(result, log, "Analyse Microsoft Defender", () => DefenderQuickScan(result, log));
        Guard(result, log, "Programmes lancés au démarrage", () =>
        {
            result.Startup.AddRange(StartupPrograms());
            log.Report($"Programmes lancés au démarrage : {result.Startup.Count} (listés dans le rapport, rien n'est désactivé).");
        });
        return result;
    }

    private static void Step(CleanupResult result, IProgress<string> log, string title, Func<IProgress<string>, ulong> run)
    {
        log.Report(title + "…");
        try
        {
            var freed = run(log);
            result.Freed += freed;
            var line = freed > 0 ? $"{title} : {Size(freed)} libérés" : $"{title} : fait";
            result.Done.Add(line);
            log.Report("    " + line);
        }
        catch (Exception ex)
        {
            NotDone(result, log, title, ex);
        }
    }

    /// <summary>Une étape qui plante est notée « non faite » et le nettoyage continue : rien ne doit fermer le logiciel.</summary>
    private static void Guard(CleanupResult result, IProgress<string> log, string title, Action run)
    {
        try { run(); }
        catch (Exception ex) { NotDone(result, log, title, ex); }
    }

    private static void NotDone(CleanupResult result, IProgress<string> log, string title, Exception ex)
    {
        if (ex is TargetInvocationException { InnerException: { } inner }) ex = inner;
        result.Done.Add($"{title} : non fait ({ex.Message})");
        log.Report($"    {title} : non fait ({ex.GetType().Name} : {ex.Message})");
    }

    // ---------- Fichiers ----------

    /// <summary>Temp de l'utilisateur et de Windows : seulement ce qui a plus d'un jour (rien d'un logiciel en cours).</summary>
    private static ulong TempFiles(IProgress<string> log)
    {
        var windowsTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        // Un document (brouillon d'un logiciel, pièce jointe ouverte…) n'est jamais supprimé : il est mis de côté.
        var aside = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "JVTECH - mis de côté", "Temp " + DateTime.Now.ToString("yyyy-MM-dd"));
        var freed = CleanFolder(Path.GetTempPath(), DateTime.Now.AddDays(-1), aside, out var movedUser)
                    + CleanFolder(windowsTemp, DateTime.Now.AddDays(-1), aside, out var movedWindows);
        if (movedUser + movedWindows > 0) log.Report($"    {movedUser + movedWindows} document(s) trouvé(s) dans Temp : mis de côté dans {aside}, pas supprimés.");
        return freed;
    }

    /// <summary>Les mises à jour déjà installées que Windows Update garde en téléchargement ; il les reprend si besoin.</summary>
    private static ulong WindowsUpdateCache(IProgress<string> log)
    {
        var download = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SoftwareDistribution\Download");
        if (!Directory.Exists(download)) return 0;
        var wasRunning = new[] { "wuauserv", "bits" }.Where(s => ServiceState(s) == "Running").ToList();
        RunHidden("net.exe", "stop wuauserv /y", TimeSpan.FromMinutes(2));
        RunHidden("net.exe", "stop bits /y", TimeSpan.FromMinutes(2));
        try
        {
            // Service encore en marche : on ne vide pas le cache sous ses pieds.
            if (ServiceState("wuauserv") == "Running")
            {
                log.Report("    Service Windows Update impossible à arrêter : cache non vidé.");
                return 0;
            }
            return CleanFolder(download, DateTime.MaxValue, null, out _);
        }
        finally
        {
            // Remis en route, puis vérifié : un service qui tournait avant l'intervention doit tourner après.
            RunHidden("net.exe", "start bits", TimeSpan.FromMinutes(2));
            RunHidden("net.exe", "start wuauserv", TimeSpan.FromMinutes(2));
            foreach (var service in wasRunning.Where(s => ServiceState(s) != "Running"))
                log.Report($"    ATTENTION : le service {service} n'a pas redémarré. Le relancer (services.msc) ou redémarrer le PC.");
        }
    }

    /// <summary>État d'un service lu par WMI (Running, Stopped…), null si illisible.</summary>
    private static string? ServiceState(string name)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher($"SELECT State FROM Win32_Service WHERE Name = '{name}'");
            foreach (var service in searcher.Get()) return service["State"] as string;
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>Le nettoyage de disque de Windows, en silencieux, sur les catégories sans risque.</summary>
    private static ulong DiskCleanup(IProgress<string> log)
    {
        var before = FreeSpace();
        using var root = Registry.LocalMachine.OpenSubKey(VolumeCaches, writable: true);
        if (root is null) throw new InvalidOperationException("nettoyage de disque absent");
        var set = new List<string>();
        foreach (var category in DiskCleanupCategories)
        {
            using var key = root.OpenSubKey(category, writable: true);
            if (key is null) continue;
            key.SetValue("StateFlags" + SageSet, 2, RegistryValueKind.DWord);
            set.Add(category);
        }
        try
        {
            log.Report("    cleanmgr /sagerun (en général 1 à 3 minutes)…");
            // Au-delà de 10 minutes, on arrête cleanmgr : ce qui est déjà nettoyé reste nettoyé.
            if (RunHidden("cleanmgr.exe", "/sagerun:" + int.Parse(SageSet), TimeSpan.FromMinutes(10)) is null)
                log.Report("    cleanmgr arrêté après 10 minutes.");
        }
        finally
        {
            // Ne pas laisser de réglage derrière soi.
            foreach (var category in set)
            {
                using var key = root.OpenSubKey(category, writable: true);
                key?.DeleteValue("StateFlags" + SageSet, throwOnMissingValue: false);
            }
        }
        var after = FreeSpace();
        return after > before ? after - before : 0;
    }

    /// <summary>
    /// Seulement le cache (copies de pages et d'images, retéléchargées toutes seules). Un navigateur ouvert est laissé
    /// de côté pour ne rien abîmer.
    /// </summary>
    private static ulong BrowserCaches(IProgress<string> log)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        ulong freed = 0;
        foreach (var (name, process, userData) in Chromium)
        {
            var folder = Path.Combine(local, userData);
            if (!Directory.Exists(folder)) continue;
            if (IsRunning(process)) { log.Report($"    {name} est ouvert : son cache n'est pas vidé."); continue; }
            foreach (var profile in SubFolders(folder).Where(p => File.Exists(Path.Combine(p, "Preferences"))))
                foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache" })
                    freed += CleanFolder(Path.Combine(profile, cache), DateTime.MaxValue, null, out _);
        }

        var opera = Path.Combine(local, @"Opera Software");
        if (Directory.Exists(opera) && !IsRunning("opera"))
            foreach (var profile in SubFolders(opera))
                freed += CleanFolder(Path.Combine(profile, "Cache"), DateTime.MaxValue, null, out _);

        var firefox = Path.Combine(local, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(firefox))
        {
            if (IsRunning("firefox")) log.Report("    Firefox est ouvert : son cache n'est pas vidé.");
            else foreach (var profile in SubFolders(firefox)) freed += CleanFolder(Path.Combine(profile, "cache2"), DateTime.MaxValue, null, out _);
        }
        return freed;
    }

    // ---------- Raccourcis cassés ----------

    /// <summary>
    /// Raccourcis du Bureau et du menu Démarrer dont le programme n'existe plus sur un disque interne. Ils sont déplacés
    /// dans Rapports\Raccourcis cassés\date, d'où on peut les remettre.
    /// </summary>
    private static void BrokenShortcuts(CleanupResult result, IProgress<string> log)
    {
        log.Report("Raccourcis cassés…");
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) { result.Done.Add("Raccourcis cassés : non vérifiés"); return; }
        var shell = Activator.CreateInstance(shellType);
        var backup = Path.Combine(AppPaths.ReportsRoot, "Raccourcis cassés", DateTime.Now.ToString("yyyy-MM-dd"));
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        };
        foreach (var folder in folders.Where(f => f.Length > 0 && Directory.Exists(f)))
        {
            string[] links;
            try { links = Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var link in links)
            {
                if (!IsBroken(shell!, link)) continue;
                try
                {
                    Directory.CreateDirectory(backup);
                    var target = Path.Combine(backup, Path.GetFileName(link));
                    if (File.Exists(target)) target = Path.Combine(backup, $"{Path.GetFileNameWithoutExtension(link)} ({Guid.NewGuid().ToString("N").Substring(0, 6)}).lnk");
                    File.Move(link, target);
                    result.MovedShortcuts.Add($"{Path.GetFileNameWithoutExtension(link)} ({folder})");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        var line = result.MovedShortcuts.Count == 0
            ? "Raccourcis cassés : aucun"
            : $"Raccourcis cassés : {result.MovedShortcuts.Count} mis de côté dans {backup}";
        result.Done.Add(line);
        log.Report("    " + line);
    }

    private static bool IsBroken(object shell, string link)
    {
        try
        {
            var shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
            var target = shortcut?.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
            // Sans cible (applis du Store, raccourcis d'installation) ou vers un réseau, une clé : on ne juge pas.
            if (string.IsNullOrWhiteSpace(target) || !Path.IsPathRooted(target) || target!.StartsWith(@"\\")) return false;
            var root = Path.GetPathRoot(target);
            if (string.IsNullOrEmpty(root) || new DriveInfo(root).DriveType != DriveType.Fixed) return false;
            return !File.Exists(target) && !Directory.Exists(target);
        }
        catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or IOException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    // ---------- Antivirus et démarrage ----------

    private static void DefenderQuickScan(CleanupResult result, IProgress<string> log)
    {
        var mpcmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");
        if (!File.Exists(mpcmd)) { result.Done.Add("Analyse Microsoft Defender : Defender absent"); return; }
        log.Report("Analyse rapide Microsoft Defender (quelques minutes)…");
        var code = RunHidden(mpcmd, "-Scan -ScanType 1", TimeSpan.FromMinutes(30));
        var line = code switch
        {
            0 => "Analyse rapide Microsoft Defender : aucune menace",
            2 => "Analyse rapide Microsoft Defender : menaces trouvées et traitées (voir Sécurité Windows > Historique de protection)",
            null => "Analyse rapide Microsoft Defender : trop longue, arrêtée",
            _ => $"Analyse rapide Microsoft Defender : non faite (Defender en retrait derrière un autre antivirus ? code {code})",
        };
        result.Done.Add(line);
        log.Report("    " + line);
    }

    /// <summary>Programmes lancés au démarrage, pour le rapport : rien n'est désactivé.</summary>
    private static IEnumerable<string> StartupPrograms()
    {
        var list = new List<string>();
        foreach (var (hive, view) in new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
        })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var run = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                if (run is null) continue;
                list.AddRange(run.GetValueNames().Where(n => n.Length > 0));
            }
            catch (System.Security.SecurityException) { }
        }
        foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
        {
            var path = Environment.GetFolderPath(folder);
            if (path.Length > 0 && Directory.Exists(path))
                list.AddRange(Directory.GetFiles(path).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    .Select(Path.GetFileNameWithoutExtension)!);
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase);
    }

    // ---------- Outils ----------

    /// <summary>
    /// Supprime les fichiers modifiés avant <paramref name="olderThan"/>, puis les dossiers vidés. Parcours en flux, dossier
    /// par dossier : la liste complète n'est jamais chargée en mémoire, et un dossier illisible, un chemin trop long ou
    /// un fichier ouvert est simplement sauté sans arrêter le reste. Les jonctions et liens symboliques ne sont jamais
    /// suivis : ils peuvent pointer vers les documents de l'utilisateur.
    /// Jamais supprimés (audit I04) : les fichiers en lecture seule (laissés en place) et, si <paramref name="aside"/> est
    /// donné, les documents (<see cref="IsDocument"/>), déplacés dans ce dossier au lieu d'être effacés.
    /// </summary>
    private static ulong CleanFolder(string folder, DateTime olderThan, string? aside, out int moved)
    {
        moved = 0;
        if (!Directory.Exists(folder)) return 0;
        ulong freed = 0;
        var visited = new List<string>();
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            visited.Add(dir);
            foreach (var entry in SafeEntries(dir))
            {
                try
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (entry is DirectoryInfo sub) { pending.Push(sub.FullName); continue; }
                    var file = (FileInfo)entry;
                    if (file.LastWriteTime >= olderThan) continue;
                    if (file.IsReadOnly) continue;
                    if (aside is not null && IsDocument(file.Name))
                    {
                        var relative = file.FullName.Substring(folder.TrimEnd('\\').Length + 1);
                        var target = Path.Combine(aside, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        if (File.Exists(target)) target = Path.Combine(Path.GetDirectoryName(target)!, $"{Path.GetFileNameWithoutExtension(target)} ({Guid.NewGuid().ToString("N").Substring(0, 6)}){Path.GetExtension(target)}");
                        file.MoveTo(target);
                        moved++;
                        continue;
                    }
                    var size = (ulong)file.Length;
                    file.Delete();
                    freed += size;
                }
                // PathTooLongException est une IOException : le fichier est sauté, le parcours continue.
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                               or System.Security.SecurityException) { }
            }
        }
        // Les plus profonds d'abord ; le dossier de départ (Temp, Cache…) est gardé.
        foreach (var dir in visited.Skip(1).OrderByDescending(d => d.Length))
        {
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return freed;
    }

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".docm", ".dot", ".dotx", ".odt", ".ott", ".rtf",
        ".xls", ".xlsx", ".xlsm", ".xlsb", ".ods", ".csv", ".ppt", ".pptx", ".odp", ".pdf",
        ".jpg", ".jpeg", ".png", ".heic", ".gif", ".bmp", ".tif", ".tiff", ".psd", ".svg",
        ".mp3", ".wav", ".m4a", ".mp4", ".mov", ".avi", ".mkv",
        ".zip", ".7z", ".rar", ".pst", ".ost", ".eml", ".msg", ".vcf", ".ics",
        ".dwg", ".dxf", ".skp", ".mdb", ".accdb",
    };

    /// <summary>Fichier qui peut être le travail de quelqu'un (document, photo, archive, base…) : jamais supprimé.</summary>
    internal static bool IsDocument(string fileName) => DocumentExtensions.Contains(Path.GetExtension(fileName));

    /// <summary>Sous-dossiers (profils de navigateur) ; dossier illisible ou chemin trop long : aucun, sans erreur.</summary>
    private static string[] SubFolders(string folder)
    {
        try { return Directory.GetDirectories(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return Array.Empty<string>(); }
    }

    /// <summary>Contenu d'un dossier lu en flux ; une erreur pendant la lecture arrête ce dossier seulement.</summary>
    private static IEnumerable<FileSystemInfo> SafeEntries(string dir)
    {
        IEnumerator<FileSystemInfo> entries;
        try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().GetEnumerator(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { yield break; }
        using (entries)
        {
            while (true)
            {
                FileSystemInfo current;
                try
                {
                    if (!entries.MoveNext()) yield break;
                    current = entries.Current;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { yield break; }
                yield return current;
            }
        }
    }

    private static bool IsRunning(string process)
    {
        try { return Process.GetProcessesByName(process).Length > 0; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return true; }
    }

    /// <summary>Code de sortie ; -1 si le programme n'a pas démarré, null s'il ne répondait plus.</summary>
    private static int? RunHidden(string file, string arguments, TimeSpan timeout)
    {
        var result = PcInspector.ProcessRunner.Run(file, arguments, null, timeout);
        return result.Started ? result.ExitCode : -1;
    }

    private static ulong FreeSpace()
    {
        try { return (ulong)new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!).AvailableFreeSpace; }
        catch (Exception ex) when (ex is IOException or ArgumentException) { return 0; }
    }

    internal static string Size(ulong bytes) =>
        bytes >= 1UL << 30 ? $"{bytes / (double)(1UL << 30):0.0} Go" : $"{Math.Max(1, bytes / (1UL << 20))} Mo";
}
