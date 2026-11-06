using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace PcMigration.Migration;

/// <summary>Taille et nombre de fichiers d'un dossier.</summary>
internal readonly struct FolderSize
{
    public FolderSize(long files, ulong bytes) { Files = files; Bytes = bytes; }
    public long Files { get; }
    public ulong Bytes { get; }
    public static FolderSize operator +(FolderSize a, FolderSize b) => new(a.Files + b.Files, a.Bytes + b.Bytes);
}

/// <summary>Résultat d'une copie robocopy, vérifiée en recomptant la destination.</summary>
internal sealed class CopyResult
{
    public FolderSize Source { get; set; }
    public FolderSize Copied { get; set; }
    public int ExitCode { get; set; }
    /// <summary>Robocopy : 8 et plus = au moins un fichier non copié (ouvert, refusé, chemin invalide…).</summary>
    public bool HasFailures => ExitCode >= 8 || ExitCode < 0;
    public long Missing => Math.Max(0, Source.Files - Copied.Files);

    public string Summary(string verb = "copiés")
    {
        var text = $"{Copied.Files} fichiers ({Tools.Size(Copied.Bytes)}) {verb}";
        if (Missing > 0) text += $", {Missing} non copiés (ouverts ou refusés, voir le journal)";
        else if (HasFailures) text += ", des erreurs (voir le journal)";
        return text;
    }
}

internal static class Tools
{
    public static string Size(ulong bytes) => bytes >= 1UL << 30
        ? $"{bytes / (double)(1UL << 30):0.0} Go"
        : bytes >= 1UL << 20 ? $"{bytes / (double)(1UL << 20):0} Mo" : $"{Math.Max(1, bytes / 1024)} Ko";

    /// <summary>Compte les fichiers (en ignorant les dossiers refusés et les jonctions, comme robocopy /XJ).</summary>
    public static FolderSize Measure(string folder, ICollection<string>? excludedDirs = null)
    {
        long files = 0;
        ulong bytes = 0;
        var stack = new Stack<string>();
        if (Directory.Exists(folder)) stack.Push(folder);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            try
            {
                foreach (var file in new DirectoryInfo(dir).EnumerateFiles())
                {
                    files++;
                    bytes += (ulong)file.Length;
                }
                foreach (var sub in new DirectoryInfo(dir).EnumerateDirectories())
                {
                    if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (excludedDirs is not null && excludedDirs.Contains(sub.Name)) continue;
                    stack.Push(sub.FullName);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return new FolderSize(files, bytes);
    }

    /// <summary>
    /// Copie un dossier avec robocopy (chemins longs, reprises, plusieurs fichiers à la fois). Rien n'est supprimé à la
    /// destination ; en restauration, un fichier plus récent déjà présent est gardé.
    /// </summary>
    public static CopyResult Robocopy(string source, string destination, IProgress<string> log, bool keepNewer = false,
        IReadOnlyCollection<string>? excludeDirs = null, IReadOnlyCollection<string>? excludeFiles = null)
    {
        var result = new CopyResult { Source = Measure(source, excludeDirs?.ToList()) };
        Directory.CreateDirectory(destination);
        var args = new StringBuilder($"{Quote(source)} {Quote(destination)} /E /COPY:DAT /DCOPY:T /R:1 /W:1 /XJ /MT:8 /NP /NFL /NDL /NJH");
        if (keepNewer) args.Append(" /XO");
        if (excludeDirs is { Count: > 0 }) args.Append(" /XD ").Append(string.Join(" ", excludeDirs.Select(Quote)));
        if (excludeFiles is { Count: > 0 }) args.Append(" /XF ").Append(string.Join(" ", excludeFiles.Select(Quote)));
        var output = new List<string>();
        result.ExitCode = Run("robocopy.exe", args.ToString(), output, TimeSpan.FromHours(12)) ?? -1;
        // Seulement le résumé final et les erreurs : pas la liste des fichiers.
        foreach (var line in output.Where(l => l.Trim().Length > 0 && (l.Contains("ERR") || l.Contains("ERREUR") || l.Contains(":"))).Take(30))
            log.Report("      " + line.Trim());
        result.Copied = Measure(destination, excludeDirs?.ToList());
        return result;
    }

    public static string Quote(string path) => "\"" + path.TrimEnd('\\') + "\"";

    /// <summary>Lance un programme sans fenêtre et récupère ce qu'il affiche. Null : introuvable ou trop long.</summary>
    public static int? Run(string file, string arguments, List<string>? output, TimeSpan timeout, Encoding? encoding = null)
    {
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(PcInspector.AppPaths.SystemTool(file), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = encoding ?? Encoding.GetEncoding(850),
                StandardErrorEncoding = encoding ?? Encoding.GetEncoding(850),
            });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            output?.Add($"{file} introuvable : {ex.Message}");
            return null;
        }
        if (process is null) return null;
        using (process)
        {
            var lines = output ?? new List<string>();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds)))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                return null;
            }
            process.WaitForExit();
            return process.ExitCode;
        }
    }

    public static bool IsRunning(string process)
    {
        try { return Process.GetProcessesByName(process).Length > 0; }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { return false; }
    }

    /// <summary>
    /// Arrête les processus d'un navigateur resté en arrière-plan sans fenêtre (Edge « démarrage rapide », Chrome
    /// « applis en arrière-plan »). Rien n'est arrêté si une fenêtre est encore ouverte : le technicien la ferme lui-même.
    /// </summary>
    public static bool StopBackground(string name)
    {
        try
        {
            var processes = Process.GetProcessesByName(name);
            if (processes.Length == 0) return true;
            if (processes.Any(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch (InvalidOperationException) { return false; } }))
                return false;
            foreach (var process in processes)
            {
                try { process.Kill(); process.WaitForExit(5000); }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            }
            return !IsRunning(name);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { return false; }
    }

    /// <summary>Efface un fichier sensible (mots de passe, clés Wi-Fi) en écrasant son contenu avant de le supprimer.</summary>
    public static void Wipe(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var info = new FileInfo(path);
            if (info.IsReadOnly) info.IsReadOnly = false;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                var zeros = new byte[64 * 1024];
                for (long done = 0; done < info.Length; done += zeros.Length)
                    stream.Write(zeros, 0, (int)Math.Min(zeros.Length, info.Length - done));
                stream.Flush(true);
            }
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(path); } catch (Exception inner) when (inner is IOException or UnauthorizedAccessException) { }
        }
    }

    public static string SafeName(string text)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) text = text.Replace(c, '-');
        return text.Trim();
    }
}
