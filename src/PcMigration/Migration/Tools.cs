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

/// <summary>Résultat d'une copie robocopy, lu dans le résumé que robocopy affiche à la fin (sans recompter les dossiers).</summary>
internal sealed class CopyResult
{
    /// <summary>Fichiers de la source (total du résumé robocopy).</summary>
    public FolderSize Source { get; set; }
    /// <summary>Fichiers présents à l'arrivée : copiés maintenant, ou déjà identiques et donc sautés.</summary>
    public FolderSize Copied { get; set; }
    /// <summary>Fichiers en échec selon robocopy (ouverts, refusés, chemin invalide…).</summary>
    public long Failed { get; set; }
    public int ExitCode { get; set; }
    /// <summary>Chemins des fichiers en erreur relevés dans la sortie de robocopy (5000 au plus ; tout est dans <see cref="FullLog"/>).</summary>
    public List<string> FailedFiles { get; } = new();
    /// <summary>Robocopy : 8 et plus = au moins un fichier non copié (ouvert, refusé, chemin invalide…).</summary>
    public bool HasFailures => ExitCode >= 8 || ExitCode < 0;
    /// <summary>16 = erreur grave (source introuvable, destination inaccessible) ; négatif = robocopy absent ou arrêté.</summary>
    public bool Fatal => ExitCode >= 16 || ExitCode < 0;
    public long Missing => Math.Max(Failed, Math.Max(0, Source.Files - Copied.Files));
    /// <summary>Tout est arrivé : aucun échec signalé et autant de fichiers à l'arrivée qu'au départ.</summary>
    public bool IsComplete => !HasFailures && Missing == 0 && !Unverified && Mismatched.Count == 0;
    /// <summary>Contrôle après copie (audit I05) : fichiers vérifiés présents avec la même taille.</summary>
    public long CheckedFiles { get; set; }
    /// <summary>Fichiers dont l'empreinte SHA-256 a été comparée (échantillon).</summary>
    public int HashedFiles { get; set; }
    /// <summary>Sortie complète de robocopy enregistrée dans ce fichier (audit I17), null si impossible.</summary>
    public string? FullLog { get; set; }
    /// <summary>Fichiers absents, de taille différente ou d'empreinte différente à l'arrivée.</summary>
    public List<string> Mismatched { get; } = new();
    /// <summary>Résumé de robocopy absent et recomptage impossible à interpréter (restauration dans un dossier déjà rempli).</summary>
    public bool Unverified { get; set; }

    public string Summary(string verb = "copiés")
    {
        if (Fatal) return $"ÉCHEC : {Tools.RobocopyMeaning(ExitCode)}";
        var text = $"{Copied.Files} fichiers ({Tools.Size(Copied.Bytes)}) {verb}";
        if (Missing > 0) text += $", INCOMPLET : {Missing} non copiés (ouverts ou refusés, liste dans le rapport)";
        else if (HasFailures) text += ", INCOMPLET : des erreurs (voir le journal)";
        else if (Unverified) text += ", NON VÉRIFIÉ : robocopy s'est arrêté avant son résumé (relancer pour contrôler)";
        if (Mismatched.Count > 0) text += $", CONTRÔLE ÉCHOUÉ : {Mismatched.Count} fichier(s) absent(s) ou différent(s) à l'arrivée (liste dans le rapport)";
        else if (CheckedFiles > 0 && Missing == 0 && !HasFailures) text += $" ; VÉRIFIÉS : présence et taille des {CheckedFiles} fichiers, empreinte SHA-256 de {HashedFiles} fichier(s) tiré(s) au sort";
        return text;
    }
}

internal static class Tools
{
    /// <summary>
    /// Code de sortie de robocopy (bits) : 1 = fichiers copiés, 2 = fichiers en plus à l'arrivée, 4 = différences,
    /// 8 = des fichiers n'ont pas pu être copiés, 16 = erreur grave, rien de fiable n'a été copié.
    /// </summary>
    internal static string RobocopyMeaning(int code)
    {
        if (code < 0) return "robocopy n'a pas pu s'exécuter jusqu'au bout (absent, arrêté ou délai dépassé)";
        if ((code & 16) != 0) return $"erreur grave de robocopy (code {code}) : source ou destination inaccessible";
        if ((code & 8) != 0) return $"des fichiers n'ont pas pu être copiés (code {code})";
        if (code == 0) return "rien à copier : tout était déjà identique";
        var parts = new List<string>();
        if ((code & 1) != 0) parts.Add("fichiers copiés");
        if ((code & 2) != 0) parts.Add("fichiers en plus à l'arrivée (gardés)");
        if ((code & 4) != 0) parts.Add("différences à l'arrivée (fichier plus récent gardé)");
        return string.Join(", ", parts) + $" (code {code})";
    }

    private static readonly System.Text.RegularExpressions.Regex ErrorLine = new(
        @"\(0x[0-9A-Fa-f]{8}\).*?(?<path>[A-Za-z]:\\.+|\\\\.+)$");

    /// <summary>
    /// Lignes d'erreur de robocopy (« ERREUR 32 (0x00000020) Copie du fichier C:\… », quelle que soit la langue :
    /// le code hexadécimal entre parenthèses et le chemin en fin de ligne ne changent pas).
    /// </summary>
    internal static List<string> FailedPaths(IEnumerable<string> output) =>
        output.Select(l => ErrorLine.Match(l.Trim())).Where(m => m.Success).Select(m => m.Groups["path"].Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(5000).ToList();

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
    /// destination ; en restauration (<paramref name="neverOverwrite"/>), aucun fichier déjà présent n'est remplacé.
    /// </summary>
    public static CopyResult Robocopy(string source, string destination, IProgress<string> log, bool neverOverwrite = false,
        IReadOnlyCollection<string>? excludeDirs = null, IReadOnlyCollection<string>? excludeFiles = null)
    {
        var result = new CopyResult();
        Directory.CreateDirectory(destination);
        var output = new List<string>();
        result.ExitCode = Run("robocopy.exe", RobocopyArguments(source, destination, neverOverwrite, excludeDirs, excludeFiles), output, TimeSpan.FromHours(12)) ?? -1;
        // Seulement le résumé final et les erreurs : pas la liste des fichiers.
        foreach (var line in output.Where(l => l.Trim().Length > 0 && (l.Contains("ERR") || l.Contains("ERREUR") || l.Contains(":"))).Take(30))
            log.Report("      " + line.Trim());

        result.FailedFiles.AddRange(FailedPaths(output));
        // Sortie complète gardée à part : le rapport n'en reprend qu'un extrait.
        if (FullLogFolder is { } logs)
        {
            try
            {
                Directory.CreateDirectory(logs);
                var file = Path.Combine(logs, $"{DateTime.Now:yyyy-MM-dd HH-mm-ss} {SafeName(Path.GetFileName(source.TrimEnd('\\')))} {Guid.NewGuid().ToString("N").Substring(0, 4)}.log");
                File.WriteAllLines(file, new[] { $"robocopy {RobocopyArguments(source, destination, neverOverwrite, excludeDirs, excludeFiles)}", $"Code de sortie : {result.ExitCode}", "" }.Concat(output));
                result.FullLog = file;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        if (!ReadSummary(output, result))
        {
            // Résumé introuvable (robocopy arrêté avant la fin) : on recompte, plus lent mais exact.
            result.Source = Measure(source, excludeDirs?.ToList());
            result.Copied = Measure(destination, excludeDirs?.ToList());
            // En restauration, la destination contenait déjà des fichiers : le recomptage ne prouve rien.
            if (neverOverwrite) result.Unverified = true;
        }
        return result;
    }

    /// <summary>
    /// Après une restauration sans écrasement : les fichiers de la sauvegarde qui existaient déjà à la destination avec
    /// un contenu différent (taille ou date) sont copiés à part dans <c>destination\{folderName}</c>, à la même place
    /// relative. Les deux versions sont donc gardées. Rien n'est remplacé ni supprimé. Renvoie les chemins relatifs.
    /// </summary>
    public static List<string> SaveConflicts(string source, string destination, string folderName, IProgress<string> log)
    {
        var saved = new List<string>();
        var conflictRoot = Path.Combine(destination, folderName);
        foreach (var relative in FindConflicts(source, destination))
        {
            var target = Path.Combine(conflictRoot, relative);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target)) File.Copy(Path.Combine(source, relative), target, overwrite: false);
                saved.Add(relative);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Report($"      En double non copié : {relative} ({ex.Message})");
            }
        }
        return saved;
    }

    /// <summary>Fichiers présents des deux côtés avec une taille ou une date différente (tolérance 2 s, comme robocopy).</summary>
    internal static IEnumerable<string> FindConflicts(string source, string destination)
    {
        var stack = new Stack<string>();
        stack.Push(source);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            FileInfo[] files;
            DirectoryInfo[] subs;
            try
            {
                var info = new DirectoryInfo(dir);
                files = info.GetFiles();
                subs = info.GetDirectories();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { continue; }
            foreach (var file in files)
            {
                var relative = file.FullName.Substring(source.TrimEnd('\\').Length + 1);
                var other = new FileInfo(Path.Combine(destination, relative));
                if (!other.Exists) continue;
                if (other.Length != file.Length || Math.Abs((other.LastWriteTimeUtc - file.LastWriteTimeUtc).TotalSeconds) > 2)
                    yield return relative;
            }
            foreach (var sub in subs)
                if ((sub.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(sub.FullName);
        }
    }

    /// <summary>
    /// Contrôle après la sauvegarde (audit I05) : chaque fichier de la source doit être à l'arrivée avec la même taille,
    /// et l'empreinte SHA-256 d'un échantillon (1 fichier sur 20, 100 au plus, 50 Mo au plus chacun) doit être identique.
    /// Un fichier en échec chez robocopy n'est pas compté deux fois. Remplit <see cref="CopyResult.Mismatched"/>.
    /// </summary>
    public static void Verify(string source, string destination, CopyResult result, IReadOnlyCollection<string>? excludeDirs = null)
    {
        var failed = new HashSet<string>(result.FailedFiles, StringComparer.OrdinalIgnoreCase);
        var excluded = excludeDirs is null ? null : new HashSet<string>(excludeDirs, StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(source);
        long index = 0;
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            FileInfo[] files;
            DirectoryInfo[] subs;
            try
            {
                var info = new DirectoryInfo(dir);
                files = info.GetFiles();
                subs = info.GetDirectories();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { continue; }
            foreach (var file in files)
            {
                if (failed.Contains(file.FullName)) continue;
                var relative = file.FullName.Substring(source.TrimEnd('\\').Length + 1);
                var copy = new FileInfo(Path.Combine(destination, relative));
                result.CheckedFiles++;
                if (!copy.Exists || copy.Length != file.Length) { if (result.Mismatched.Count < 500) result.Mismatched.Add(relative); continue; }
                if (index++ % 20 != 0 || result.HashedFiles >= 100 || file.Length > 50L * 1024 * 1024) continue;
                var a = Sha256(file.FullName);
                var b = Sha256(copy.FullName);
                if (a is null || b is null) continue;
                result.HashedFiles++;
                if (a != b && result.Mismatched.Count < 500) result.Mismatched.Add(relative + " (contenu différent)");
            }
            foreach (var sub in subs)
                if ((sub.Attributes & FileAttributes.ReparsePoint) == 0 && (excluded is null || !excluded.Contains(sub.Name))) stack.Push(sub.FullName);
        }
    }

    internal static string? Sha256(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sha = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Dossier où chaque copie enregistre la sortie complète de robocopy (choisi par la sauvegarde ou la restauration).</summary>
    public static string? FullLogFolder { get; set; }

    private static string SafeName(string name)
    {
        var clean = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return clean.Length == 0 ? "copie" : clean.Length > 40 ? clean.Substring(0, 40) : clean;
    }

    /// <summary>Options qui suppriment ou déplacent des fichiers : jamais utilisées par PC Migration.</summary>
    internal static readonly string[] ForbiddenSwitches = { "/MIR", "/PURGE", "/MOV", "/MOVE" };

    /// <summary>
    /// Arguments de robocopy. /E copie sans rien supprimer ; en restauration, /XC /XN /XO : un fichier déjà présent
    /// n'est JAMAIS remplacé, qu'il soit plus ancien, plus récent ou de même date (voir <see cref="SaveConflicts"/>) ; /BYTES donne des tailles exactes dans le résumé (sinon « 1.2 g », arrondi et différent selon la langue).
    /// </summary>
    internal static string RobocopyArguments(string source, string destination, bool neverOverwrite,
        IReadOnlyCollection<string>? excludeDirs = null, IReadOnlyCollection<string>? excludeFiles = null)
    {
        var args = new StringBuilder($"{Quote(source)} {Quote(destination)} /E /COPY:DAT /DCOPY:T /R:1 /W:1 /XJ /MT:8 /NP /NFL /NDL /NJH /BYTES");
        if (neverOverwrite) args.Append(" /XC /XN /XO");
        if (excludeDirs is { Count: > 0 }) args.Append(" /XD ").Append(string.Join(" ", excludeDirs.Select(Quote)));
        if (excludeFiles is { Count: > 0 }) args.Append(" /XF ").Append(string.Join(" ", excludeFiles.Select(Quote)));
        return args.ToString();
    }

    // « Fichiers :   120   100   20   0   0   0 » : Total, Copié, Ignoré, Discordance, Échec, Extras.
    // Le libellé change avec la langue de Windows (Dirs/Rép., Files/Fichiers, Bytes/Octets) mais l'ordre des lignes
    // ne change pas : répertoires, fichiers, octets, puis la durée (qui contient des « : » et ne correspond pas).
    private static readonly System.Text.RegularExpressions.Regex SummaryLine = new(
        @"^\s*[^\d:]+:\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*$");

    /// <summary>Remplit le résultat avec le résumé de robocopy. Faux si le résumé n'est pas là.</summary>
    internal static bool ReadSummary(IReadOnlyList<string> output, CopyResult result)
    {
        var rows = output.Select(l => SummaryLine.Match(l)).Where(m => m.Success)
            .Select(m => Enumerable.Range(1, 6).Select(i => ulong.Parse(m.Groups[i].Value)).ToArray())
            .ToList();
        if (rows.Count < 3) return false;
        // Les 3 dernières lignes de chiffres : un « /MT » peut répéter des lignes avant le résumé final.
        var files = rows[rows.Count - 2];
        var bytes = rows[rows.Count - 1];
        const int Total = 0, Copied = 1, Skipped = 2, Failed = 4;
        result.Source = new FolderSize((long)files[Total], bytes[Total]);
        result.Copied = new FolderSize((long)(files[Copied] + files[Skipped]), bytes[Copied] + bytes[Skipped]);
        result.Failed = (long)files[Failed];
        return true;
    }

    public static string Quote(string path) => "\"" + path.TrimEnd('\\') + "\"";

    /// <summary>Lance un programme sans fenêtre et récupère ce qu'il affiche. Null : introuvable ou trop long.</summary>
    public static int? Run(string file, string arguments, List<string>? output, TimeSpan timeout, Encoding? encoding = null)
    {
        var lines = output ?? new List<string>();
        return PcInspector.ProcessRunner.Run(file, arguments, lines.Add, timeout, encoding).ExitCode;
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
