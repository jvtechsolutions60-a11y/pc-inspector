using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PcInspector;

namespace PcMaintenance.Maintenance;

/// <summary>
/// Réparer Windows : DISM /RestoreHealth répare d'abord le magasin de composants (la « réserve » de fichiers de
/// Windows), puis SFC /scannow remplace les fichiers système abîmés à partir de cette réserve. C'est l'ordre conseillé
/// par Microsoft. Les résultats sont traduits en clair pour le rapport. Aucun fichier personnel n'est touché.
/// </summary>
internal static class Repair
{
    /// <param name="Ok">Réparation confirmée (code de DISM et résultat de SFC), jamais déduit de la seule fin du processus.</param>
    public sealed record Result(string Summary, bool Ok, List<string> Details, bool RebootRequired = false);

    /// <summary>Verdict de SFC, lu dans sa sortie puis, si la langue n'est pas reconnue, dans CBS.log (marqueurs [SR] fixes).</summary>
    internal enum SfcVerdict { Clean, Repaired, Unrepairable, PendingReboot, CouldNotRun, Unknown }

    public static Result Run(IProgress<string> log)
    {
        var details = new List<string>();

        log.Report("DISM : vérification et réparation du magasin de composants (10 à 30 minutes)…");
        var dism = ProcessRunner.Run("dism.exe", "/Online /Cleanup-Image /RestoreHealth /NoRestart", l =>
        {
            var line = l.Trim();
            if (line.Length > 0 && !line.Contains("%") && !line.StartsWith("[")) log.Report("    " + line);
        }, TimeSpan.FromMinutes(60));
        var (dismText, dismOk, dismReboot) = DismMeaning(dism.Started, dism.TimedOut, dism.ExitCode);
        details.Add("DISM : " + dismText);
        log.Report("DISM : " + dismText);

        log.Report("SFC : vérification des fichiers système (5 à 15 minutes)…");
        var started = DateTime.Now;
        var output = new StringBuilder();
        // SFC écrit en UTF-16 quand sa sortie est redirigée.
        var sfc = ProcessRunner.Run("sfc.exe", "/scannow", l =>
        {
            var line = l.Replace("\0", "").Trim();
            if (line.Length == 0 || line.Contains("%")) return;
            output.AppendLine(line);
        }, TimeSpan.FromMinutes(45), Encoding.Unicode);

        var verdict = sfc.TimedOut ? SfcVerdict.Unknown : !sfc.Started ? SfcVerdict.CouldNotRun : SfcFromOutput(output.ToString());
        if (verdict == SfcVerdict.Unknown && sfc.Started && !sfc.TimedOut)
        {
            // Windows dans une autre langue que le français ou l'anglais : le journal CBS, lui, ne change pas.
            verdict = SfcFromCbs(ReadCbsSince(started));
            if (verdict != SfcVerdict.Unknown) details.Add("SFC : résultat lu dans C:\\Windows\\Logs\\CBS\\CBS.log");
        }
        var (sfcText, sfcOk) = SfcMeaning(verdict, sfc.TimedOut, output.ToString());
        details.Add("SFC : " + sfcText);
        log.Report("SFC : " + sfcText);

        var reboot = dismReboot || verdict == SfcVerdict.PendingReboot;
        var summary = dismOk && sfcOk ? "Windows vérifié : " + sfcText : $"DISM : {dismText} · SFC : {sfcText}";
        return new Result(summary, dismOk && sfcOk, details, reboot);
    }

    /// <summary>Code de sortie de DISM traduit. 0 = sain ou réparé ; 3010 = réparé, redémarrage nécessaire (réussite).</summary>
    internal static (string Text, bool Ok, bool Reboot) DismMeaning(bool started, bool timedOut, int? exitCode)
    {
        if (timedOut) return ("arrêté au bout d'une heure (PC très lent ou disque en mauvais état)", false, false);
        if (!started || exitCode is not { } code) return ("n'a pas pu démarrer", false, false);
        return unchecked((uint)code) switch
        {
            0 => ("magasin de composants sain ou réparé", true, false),
            3010 => ("réparé, redémarrage nécessaire", true, true),
            0x800F081F => ("fichiers source introuvables : réparer avec l'ISO de la même version de Windows (DISM /Source)", false, false),
            0x800F0906 or 0x800F0907 => ("téléchargement des fichiers de réparation impossible (internet, proxy ou WSUS)", false, false),
            0x800F0950 => ("fichiers source introuvables (.NET 3.5) : utiliser l'ISO", false, false),
            0x800F082F => ("redémarrage en attente : redémarrer puis relancer", false, true),
            0x80070005 => ("accès refusé : lancer en administrateur, antivirus tiers à vérifier", false, false),
            0x80070070 => ("espace disque insuffisant sur C:", false, false),
            0x800706BE or 0x800706BA => ("service Windows Modules Installer en erreur : redémarrer puis relancer", false, false),
            _ => ($"échec (code 0x{code:X8}) : {PcInspector.WindowsUpdateFailures.Explain(code)}", false, false),
        };
    }

    /// <summary>Dernière phrase de SFC, en français ou en anglais. Unknown si la langue n'est pas reconnue.</summary>
    internal static SfcVerdict SfcFromOutput(string output)
    {
        var text = output.ToLowerInvariant();
        if (text.Contains("aucune violation") || text.Contains("did not find any integrity")) return SfcVerdict.Clean;
        if (text.Contains("pas pu en réparer") || text.Contains("pas pu réparer") || text.Contains("unable to fix")) return SfcVerdict.Unrepairable;
        if (text.Contains("les a réparés") || text.Contains("les a correctement réparés") || text.Contains("successfully repaired")) return SfcVerdict.Repaired;
        if (text.Contains("n'a pas pu effectuer") || text.Contains("could not perform")) return SfcVerdict.CouldNotRun;
        if (text.Contains("en attente") || text.Contains("pending") || text.Contains("redémarr") || text.Contains("restart")) return SfcVerdict.PendingReboot;
        return SfcVerdict.Unknown;
    }

    /// <summary>
    /// Lignes [SR] de CBS.log écrites pendant ce SFC (indépendantes de la langue de Windows) :
    /// « Cannot repair member file » = non réparable ; « Repairing corrupted file » / « Repaired file » = réparé ;
    /// « Verify complete » sans les précédentes = rien d'anormal.
    /// </summary>
    internal static SfcVerdict SfcFromCbs(IReadOnlyCollection<string> lines)
    {
        var sr = lines.Where(l => l.Contains("[SR]")).ToList();
        if (sr.Count == 0) return SfcVerdict.Unknown;
        if (sr.Any(l => l.IndexOf("Cannot repair", StringComparison.OrdinalIgnoreCase) >= 0)) return SfcVerdict.Unrepairable;
        if (sr.Any(l => l.IndexOf("Repairing corrupted file", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("Repaired file", StringComparison.OrdinalIgnoreCase) >= 0))
            return SfcVerdict.Repaired;
        if (sr.Any(l => l.IndexOf("Verify complete", StringComparison.OrdinalIgnoreCase) >= 0)) return SfcVerdict.Clean;
        return SfcVerdict.Unknown;
    }

    /// <summary>Lignes de CBS.log datées après <paramref name="since"/> (« 2026-11-10 15:03:12, Info … »). Lecture seule, partagée.</summary>
    private static List<string> ReadCbsSince(DateTime since)
    {
        var lines = new List<string>();
        try
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Logs\CBS\CBS.log");
            using var stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
            // Seulement la fin du fichier (il peut peser des centaines de Mo).
            if (stream.Length > 64L * 1024 * 1024) stream.Seek(-64L * 1024 * 1024, System.IO.SeekOrigin.End);
            using var reader = new System.IO.StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length >= 19 && DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var when) && when >= since.AddSeconds(-5))
                    lines.Add(line);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
        return lines;
    }

    internal static (string Text, bool Ok) SfcMeaning(SfcVerdict verdict, bool timedOut, string output)
    {
        if (timedOut) return ("arrêté au bout de 45 minutes : résultat inconnu", false);
        return verdict switch
        {
            SfcVerdict.Clean => ("aucun fichier système endommagé", true),
            SfcVerdict.Repaired => ("fichiers système endommagés trouvés et réparés", true),
            SfcVerdict.Unrepairable => ("fichiers endommagés non réparables : réparation par l'ISO (mise à niveau sur place) conseillée", false),
            SfcVerdict.PendingReboot => ("une réparation attend un redémarrage : redémarrer puis relancer", false),
            SfcVerdict.CouldNotRun => ("SFC n'a pas pu faire la vérification : relancer en mode sans échec", false),
            // Pas de verdict reconnu : on ne conclut pas à une réussite parce que SFC s'est terminé.
            _ => ("résultat non confirmé" + (LastLine(output) is { } last ? " (dernière ligne : " + last + ")" : "") + " : lire C:\\Windows\\Logs\\CBS\\CBS.log", false),
        };
    }

    private static string? LastLine(string output) =>
        output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
}
