using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using PcInspector;

namespace PcMaintenance.Maintenance;

/// <summary>
/// Télécharge la dernière version d'AdwCleaner (Malwarebytes, gratuit, sans installation) et la lance en mode nettoyage automatique.
/// </summary>
internal static class AdwCleaner
{
    private const string DownloadUrl = "https://downloads.malwarebytes.com/file/adwcleaner";
    private static readonly string LogFolder = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", @"AdwCleaner\Logs");

    public sealed record Result(string Summary, int Removed, string? LogPath);

    public static async Task<Result> RunAsync(IProgress<string> log)
    {
        var exe = Path.Combine(AppPaths.EnsureFolder(AppPaths.WorkFolder), "adwcleaner.exe");
        try
        {
            log.Report("Téléchargement de la dernière version d'AdwCleaner…");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            // Téléchargement dans un fichier à part : une coupure ne laisse jamais un adwcleaner.exe à moitié écrit,
            // et la copie précédente (déjà vérifiée) reste utilisable.
            var part = exe + ".part";
            TryDelete(part);
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "PC Maintenance";
                await client.DownloadFileTaskAsync(DownloadUrl, part);
            }
            if (!SignedByMalwarebytes(part))
            {
                TryDelete(part);
                throw new WebException("le fichier reçu est incomplet ou n'est pas signé par Malwarebytes");
            }
            TryDelete(exe);
            File.Move(part, exe);
        }
        catch (Exception ex) when (ex is WebException or IOException or UnauthorizedAccessException)
        {
            TryDelete(exe + ".part");
            if (!File.Exists(exe)) return new Result("Téléchargement impossible : " + ex.Message, 0, null);
            log.Report("Téléchargement impossible, utilisation de la copie déjà présente.");
        }

        // Garde-fou : ne lancer le fichier que s'il porte bien la signature de Malwarebytes.
        if (!SignedByMalwarebytes(exe))
        {
            TryDelete(exe);
            return new Result("Fichier téléchargé non signé par Malwarebytes : AdwCleaner n'a pas été lancé.", 0, null);
        }

        var started = DateTime.Now;
        log.Report("Analyse et nettoyage par AdwCleaner (quelques minutes)…");
        var code = await Task.Run(() =>
        {
            using var process = Process.Start(new ProcessStartInfo(exe, "/eula /clean /noreboot") { UseShellExecute = true });
            if (process is null) return -1;
            process.WaitForExit();
            return process.ExitCode;
        });

        var logPath = LatestLog(started);
        var removed = logPath is null ? 0 : CountRemoved(logPath);
        var summary = removed == 0 ? "Rien de suspect trouvé." : $"{removed} élément(s) indésirable(s) supprimé(s) : redémarrer le PC pour finir.";
        if (logPath is null && code != 0) summary = $"AdwCleaner s'est arrêté avec le code {code}.";
        return new Result(summary, removed, logPath);
    }

    /// <summary>Signature valide (fichier intact, certificat reconnu par Windows) et signataire Malwarebytes.</summary>
    private static bool SignedByMalwarebytes(string path) => PcInspector.Authenticode.IsSignedBy(path, "Malwarebytes");

    private static string? LatestLog(DateTime since)
    {
        try
        {
            return Directory.Exists(LogFolder)
                ? new DirectoryInfo(LogFolder).GetFiles("*.txt").Where(f => f.LastWriteTime >= since.AddMinutes(-1))
                    .OrderByDescending(f => f.LastWriteTime).Select(f => f.FullName).FirstOrDefault()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Le journal d'AdwCleaner liste chaque élément supprimé sur une ligne commençant par « Deleted ».</summary>
    private static int CountRemoved(string logPath)
    {
        try { return File.ReadLines(logPath).Count(l => l.TrimStart().StartsWith("Deleted", StringComparison.OrdinalIgnoreCase)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
