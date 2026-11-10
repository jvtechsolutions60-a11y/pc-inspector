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
    public sealed record Result(string Summary, bool Ok, List<string> Details);

    public static Result Run(IProgress<string> log)
    {
        var details = new List<string>();

        log.Report("DISM : vérification et réparation du magasin de composants (10 à 30 minutes)…");
        var dism = ProcessRunner.Run("dism.exe", "/Online /Cleanup-Image /RestoreHealth /NoRestart", l =>
        {
            var line = l.Trim();
            if (line.Length > 0 && !line.Contains("%") && !line.StartsWith("[")) log.Report("    " + line);
        }, TimeSpan.FromMinutes(60));
        var dismText = DismMeaning(dism);
        details.Add("DISM : " + dismText);
        log.Report("DISM : " + dismText);
        var dismOk = dism.ExitCode == 0;

        log.Report("SFC : vérification des fichiers système (5 à 15 minutes)…");
        var output = new StringBuilder();
        // SFC écrit en UTF-16 quand sa sortie est redirigée.
        var sfc = ProcessRunner.Run("sfc.exe", "/scannow", l =>
        {
            var line = l.Replace("\0", "").Trim();
            if (line.Length == 0 || line.Contains("%")) return;
            output.AppendLine(line);
        }, TimeSpan.FromMinutes(45), Encoding.Unicode);
        var (sfcText, sfcOk) = SfcMeaning(sfc, output.ToString());
        details.Add("SFC : " + sfcText);
        log.Report("SFC : " + sfcText);

        var summary = dismOk && sfcOk ? "Windows vérifié : " + sfcText : $"DISM : {dismText} · SFC : {sfcText}";
        return new Result(summary, dismOk && sfcOk, details);
    }

    private static string DismMeaning(ProcessResult result)
    {
        if (result.TimedOut) return "arrêté au bout d'une heure (PC très lent ou disque en mauvais état)";
        if (result.ExitCode is not { } code) return "n'a pas pu démarrer";
        return unchecked((uint)code) switch
        {
            0 => "magasin de composants sain ou réparé",
            3010 => "réparé, redémarrage nécessaire",
            0x800F081F => "fichiers source introuvables : réparer avec l'ISO de la même version de Windows (DISM /Source)",
            0x800F0906 or 0x800F0907 => "téléchargement des fichiers de réparation impossible (internet, proxy ou WSUS)",
            0x800F0950 => "fichiers source introuvables (.NET 3.5) : utiliser l'ISO",
            0x800F082F => "redémarrage en attente : redémarrer puis relancer",
            0x80070005 => "accès refusé : lancer en administrateur, antivirus tiers à vérifier",
            0x80070070 => "espace disque insuffisant sur C:",
            0x800706BE or 0x800706BA => "service Windows Modules Installer en erreur : redémarrer puis relancer",
            _ => $"échec (code 0x{code:X8}) : {PcInspector.WindowsUpdateFailures.Explain(code)}",
        };
    }

    /// <summary>SFC ne donne pas de code de sortie fiable : on lit sa dernière phrase (français ou anglais).</summary>
    private static (string Text, bool Ok) SfcMeaning(ProcessResult result, string output)
    {
        if (result.TimedOut) return ("arrêté au bout de 45 minutes", false);
        if (!result.Started) return ("n'a pas pu démarrer", false);
        var text = output.ToLowerInvariant();
        if (text.Contains("aucune violation") || text.Contains("did not find any integrity"))
            return ("aucun fichier système endommagé", true);
        if (text.Contains("pas pu en réparer") || text.Contains("pas pu réparer") || text.Contains("unable to fix"))
            return ("fichiers endommagés non réparables : réparation par l'ISO (mise à niveau sur place) conseillée", false);
        if (text.Contains("les a réparés") || text.Contains("successfully repaired"))
            return ("fichiers système endommagés trouvés et réparés", true);
        if (text.Contains("en attente") || text.Contains("pending") || text.Contains("redémarr") || text.Contains("restart"))
            return ("une réparation attend un redémarrage : redémarrer puis relancer", false);
        if (text.Contains("n'a pas pu effectuer") || text.Contains("could not perform"))
            return ("SFC n'a pas pu faire la vérification : relancer en mode sans échec", false);
        var last = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
        return (last is null ? "résultat illisible" : "résultat : " + last, result.ExitCode == 0);
    }
}
