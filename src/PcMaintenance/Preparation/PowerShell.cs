using System;
using System.Collections.Generic;

namespace PcMaintenance.Preparation;

/// <summary>Lance un petit script PowerShell (encodé, pour éviter les problèmes de guillemets) et renvoie ses lignes.</summary>
internal static class PowerShell
{
    public static List<string> Run(string script, TimeSpan timeout)
    {
        var lines = new List<string>();
        // Les erreurs de PowerShell sont ignorées : les scripts écrivent eux-mêmes « ERREUR|… » quand il le faut.
        var result = PcInspector.ProcessRunner.PowerShell(script, line =>
        {
            if (line.Trim().Length > 0) lines.Add(line.Trim());
        }, timeout, includeErrors: false);
        if (!result.Started) lines.Add("ERREUR|PowerShell|introuvable");
        else if (result.TimedOut) lines.Add("ERREUR|PowerShell|pas de réponse");
        return lines;
    }

    /// <summary>Met une chaîne entre apostrophes pour PowerShell.</summary>
    public static string Quote(string text) => PcInspector.ProcessRunner.PowerShellQuote(text);
}
