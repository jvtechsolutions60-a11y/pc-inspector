using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Ce qui remplit le disque système, dossier par dossier, et ce qui peut être libéré (comme FoldersReport).</summary>
public sealed class DiskUsageCollector : ICollector
{
    private const int MaxBigFiles = 15;
    private const int MaxProfileFolders = 8;
    private const long BigFileBytes = 500L * 1024 * 1024;

    // Fichiers OneDrive « en ligne uniquement » : leur taille n'occupe pas le disque.
    private const FileAttributes CloudOnly = FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000;

    public string Title => "Espace disque";
    public string Icon => "\uEDA2";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        var root = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var bigFiles = new List<FileInfo>();

        var drive = new DriveInfo(root);
        section.Groups.Add(new InfoGroup($"Disque {root.TrimEnd('\\')}")
            .Add("Taille", Wmi.FormatBytes((ulong)drive.TotalSize))
            .Add("Utilisé", Wmi.FormatBytes((ulong)(drive.TotalSize - drive.TotalFreeSpace)))
            .Add("Libre", $"{Wmi.FormatBytes((ulong)drive.TotalFreeSpace)} ({drive.TotalFreeSpace * 100 / Math.Max(1, drive.TotalSize)} %)"));

        // Le dossier Windows n'est pas parcouru : ses liens internes gonflent artificiellement sa taille.
        var usersDir = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var profiles = new List<InfoGroup>();
        long usersTotal = 0;
        if (usersDir is not null && Directory.Exists(usersDir))
        {
            foreach (var profile in SafeDirectories(new DirectoryInfo(usersDir)))
            {
                var parts = SubFolders(profile.FullName, bigFiles).ToList();
                var total = parts.Sum(p => p.Size) + FilesSize(profile, bigFiles);
                usersTotal += total;
                if (total < 100L * 1024 * 1024) continue;
                var group = new InfoGroup($"Utilisateur {profile.Name} · {Wmi.FormatBytes((ulong)total)}");
                foreach (var (dir, size) in parts.Take(MaxProfileFolders))
                    group.Add(dir.Name, Wmi.FormatBytes((ulong)size));
                profiles.Add(group);
            }
        }

        var folders = new InfoGroup("Dossiers principaux");
        var top = SubFolders(root, bigFiles, skip: new[] { windows, usersDir })
            .Select(p => (p.Dir.Name, p.Size)).ToList();
        if (usersTotal > 0) top.Add(($"{Path.GetFileName(usersDir)} (détail par utilisateur plus bas)", usersTotal));
        foreach (var (name, size) in top.OrderByDescending(p => p.Size))
            folders.Add(name, Wmi.FormatBytes((ulong)size));
        if (folders.Items.Count == 0) folders.Add("Résultat", "Aucun dossier lisible");
        section.Groups.Add(folders);
        section.Groups.AddRange(profiles);

        section.Groups.Add(SystemFiles(root, windows));
        section.Groups.Add(Freeable(root, windows, usersDir));

        var big = new InfoGroup("Plus gros fichiers (hors Windows)");
        foreach (var file in bigFiles.OrderByDescending(f => f.Length).Take(MaxBigFiles))
            big.Add(file.FullName, Wmi.FormatBytes((ulong)file.Length));
        if (big.Items.Count == 0) big.Add("Résultat", "Aucun fichier de plus de 500 Mo");
        section.Groups.Add(big);

        return section;
    }

    private static IEnumerable<(DirectoryInfo Dir, long Size)> SubFolders(string path, List<FileInfo> bigFiles, string?[]? skip = null) =>
        SafeDirectories(new DirectoryInfo(path))
            .Where(d => skip is null || !skip.Any(s => string.Equals(s, d.FullName, StringComparison.OrdinalIgnoreCase)))
            .Select(d => (Dir: d, Size: Measure(d, bigFiles)))
            .Where(p => p.Size > 0)
            .OrderByDescending(p => p.Size)
            .ToList();

    private static InfoGroup SystemFiles(string root, string windows)
    {
        var group = new InfoGroup("Fichiers système");
        AddFile(group, "Fichier de veille prolongée (hiberfil.sys)", Path.Combine(root, "hiberfil.sys"));
        AddFile(group, "Mémoire virtuelle (pagefile.sys)", Path.Combine(root, "pagefile.sys"));
        AddFile(group, "Fichier d'échange (swapfile.sys)", Path.Combine(root, "swapfile.sys"));
        AddFile(group, "Vidage mémoire d'écran bleu (MEMORY.DMP)", Path.Combine(windows, "MEMORY.DMP"));
        if (group.Items.Count == 0) group.Add("Résultat", "Aucun fichier système volumineux");
        return group;
    }

    /// <summary>Ce qui peut être vidé sans risque, avec l'outil Windows à utiliser. Le logiciel ne supprime rien lui-même.</summary>
    private static InfoGroup Freeable(string root, string windows, string? usersDir)
    {
        var group = new InfoGroup("Ce qui peut être libéré");
        long total = 0;

        void Add(string label, long size, string how)
        {
            if (size < 50L * 1024 * 1024) return;
            total += size;
            group.Add(label, $"{Wmi.FormatBytes((ulong)size)} · {how}");
        }

        var cleanup = "Nettoyage de disque > Nettoyer les fichiers système";
        Add("Ancienne installation (Windows.old)", Measure(Path.Combine(root, "Windows.old")), cleanup);
        Add("Téléchargements de Windows Update", Measure(Path.Combine(windows, @"SoftwareDistribution\Download")), cleanup);
        Add("Fichiers temporaires de Windows", Measure(Path.Combine(windows, "Temp")), cleanup);
        Add("Corbeille", Measure(Path.Combine(root, "$Recycle.Bin")), "Vider la corbeille après accord du client");
        Add("Vidages d'écran bleu", Measure(Path.Combine(windows, "Minidump")) + FileSize(Path.Combine(windows, "MEMORY.DMP")),
            "Nettoyage de disque, après avoir noté la cause des écrans bleus");

        if (usersDir is not null && Directory.Exists(usersDir))
        {
            long temp = 0, downloads = 0;
            foreach (var profile in SafeDirectories(new DirectoryInfo(usersDir)))
            {
                temp += Measure(Path.Combine(profile.FullName, @"AppData\Local\Temp"));
                downloads += Measure(Path.Combine(profile.FullName, "Downloads"));
            }
            Add("Fichiers temporaires des utilisateurs", temp, "Paramètres > Système > Stockage > Fichiers temporaires");
            Add("Dossiers Téléchargements", downloads, "À trier avec le client : souvent des installateurs inutiles");
        }

        if (group.Items.Count == 0) group.Add("Résultat", "Rien de significatif à libérer");
        else group.Add("Total possible", Wmi.FormatBytes((ulong)total));
        return group;
    }

    private static void AddFile(InfoGroup group, string label, string path)
    {
        var size = FileSize(path);
        if (size > 0) group.Add(label, Wmi.FormatBytes((ulong)size));
    }

    private static long FileSize(string path)
    {
        try
        {
            // Lire via le dossier parent fonctionne aussi pour les fichiers verrouillés comme pagefile.sys.
            var dir = new DirectoryInfo(Path.GetDirectoryName(path)!);
            return dir.EnumerateFiles(Path.GetFileName(path)).FirstOrDefault()?.Length ?? 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return 0;
        }
    }

    private static long Measure(string path) => Directory.Exists(path) ? Measure(new DirectoryInfo(path), null) : 0;

    /// <summary>Taille d'un dossier sans suivre les jonctions, en ignorant les dossiers interdits et les fichiers OneDrive en ligne.</summary>
    private static long Measure(DirectoryInfo start, List<FileInfo>? bigFiles)
    {
        long total = 0;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = dir.EnumerateFileSystemInfos().ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (entry is DirectoryInfo sub) pending.Push(sub);
                else if (entry is FileInfo file && (file.Attributes & CloudOnly) == 0)
                {
                    total += file.Length;
                    if (bigFiles is not null && file.Length >= BigFileBytes) bigFiles.Add(file);
                }
            }
        }
        return total;
    }

    private static long FilesSize(DirectoryInfo dir, List<FileInfo> bigFiles)
    {
        try
        {
            long total = 0;
            foreach (var file in dir.EnumerateFiles())
            {
                if ((file.Attributes & (CloudOnly | FileAttributes.ReparsePoint)) != 0) continue;
                total += file.Length;
                if (file.Length >= BigFileBytes) bigFiles.Add(file);
            }
            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return 0;
        }
    }

    private static List<DirectoryInfo> SafeDirectories(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateDirectories().Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new List<DirectoryInfo>();
        }
    }
}
