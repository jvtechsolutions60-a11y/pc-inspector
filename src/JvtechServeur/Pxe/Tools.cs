using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace JvtechServeur.Pxe;

/// <summary>Lancement des outils de Windows (DISM, bcdedit, robocopy, PowerShell) avec leur sortie dans le journal.</summary>
internal static class Tools
{
    /// <summary>Lance un programme et renvoie son code de sortie et ses lignes (null s'il n'a pas pu démarrer).</summary>
    public static (int? Code, List<string> Lines) Run(string exe, string arguments, Action<string>? log, TimeSpan timeout, Encoding? encoding = null)
    {
        var lines = new List<string>();
        var result = PcInspector.ProcessRunner.Run(exe, arguments, text => Line(text, lines, log), timeout, encoding);
        if (result.TimedOut) log?.Invoke($"{Path.GetFileName(exe)} ne répond plus : arrêté.");
        return (result.ExitCode, lines);
    }

    /// <summary>Script PowerShell encodé (pas de souci de guillemets), sortie en UTF-8.</summary>
    public static (int? Code, List<string> Lines) PowerShell(string script, Action<string>? log, TimeSpan timeout)
    {
        var lines = new List<string>();
        var result = PcInspector.ProcessRunner.PowerShell(script, text => Line(text, lines, log), timeout);
        if (result.TimedOut) log?.Invoke("PowerShell ne répond plus : arrêté.");
        return (result.ExitCode, lines);
    }

    private static void Line(string text, List<string> lines, Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var clean = text.Trim();
        // DISM dessine sa barre de progression ligne par ligne : inutile dans le journal.
        if (clean.StartsWith("[") && clean.Contains("%")) return;
        lines.Add(clean);
        log?.Invoke(clean);
    }

    public static string Quote(string text) => "'" + text.Replace("'", "''") + "'";

    public static string System32(string exe) => Path.Combine(Environment.SystemDirectory, exe);
}
