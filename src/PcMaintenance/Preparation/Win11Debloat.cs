using System;
using System.IO;
using System.Linq;
using PcInspector;

namespace PcMaintenance.Preparation;

/// <summary>
/// Win11Debloat (github.com/Raphire/Win11Debloat, licence MIT), version figée fournie avec le kit dans C:\JVTECH\Win11Debloat.
/// Lancé en mode Sysprep : les réglages vont dans le profil par défaut, donc dans le compte que l'acheteur créera.
/// Seules des options sans risque pour un PC vendu : rien sur Windows Update, Defender, le Store ou Edge.
/// </summary>
internal static class Win11Debloat
{
    private const string Arguments =
        "-Silent -Sysprep -RemoveApps -DisableTelemetry -DisableBing -DisableSuggestions -DisableWidgets -DisableCopilot -DisableRecall";

    public static string ScriptPath => Path.Combine(AppPaths.ExeFolder, "Win11Debloat", "Win11Debloat.ps1");

    public static bool Available => File.Exists(ScriptPath);

    /// <summary>Renvoie null si tout s'est bien passé, sinon la raison de l'échec.</summary>
    public static string? Run(IProgress<string> log)
    {
        var logs = Path.Combine(AppPaths.ExeFolder, "Win11Debloat", "Journaux");
        Directory.CreateDirectory(logs);
        log.Report("Win11Debloat " + Arguments);
        var lines = PowerShell.Run(
            $"& {PowerShell.Quote(ScriptPath)} {Arguments} -LogPath {PowerShell.Quote(logs)} *>&1 | ForEach-Object {{ \"$_\" }}\n'FIN|' + $LASTEXITCODE",
            TimeSpan.FromMinutes(30));
        foreach (var line in lines.Where(l => !l.StartsWith("FIN|") && l.Trim('-', '=', ' ').Length > 0))
            log.Report("    " + line);
        var failure = lines.FirstOrDefault(l => l.StartsWith("ERREUR|PowerShell|"));
        if (failure is not null) return failure.Substring("ERREUR|PowerShell|".Length);
        // Un script PowerShell sans « exit » laisse $LASTEXITCODE vide : c'est une réussite.
        return lines.Contains("FIN|") || lines.Contains("FIN|0") ? null : "terminé avec une erreur (voir le journal)";
    }
}
