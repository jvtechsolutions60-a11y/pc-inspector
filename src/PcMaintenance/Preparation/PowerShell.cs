using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PcMaintenance.Preparation;

/// <summary>Lance un petit script PowerShell (encodé, pour éviter les problèmes de guillemets) et renvoie ses lignes.</summary>
internal static class PowerShell
{
    public static List<string> Run(string script, TimeSpan timeout)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ProgressPreference='SilentlyContinue'\n" + script));
        var start = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        var lines = new List<string>();
        try
        {
            using var process = Process.Start(start)!;
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                lines.Add("ERREUR|PowerShell|pas de réponse");
                return lines;
            }
            foreach (var line in output.Result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                lines.Add(line.Trim());
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            lines.Add("ERREUR|PowerShell|" + ex.Message);
        }
        return lines;
    }

    /// <summary>Met une chaîne entre apostrophes pour PowerShell.</summary>
    public static string Quote(string text) => "'" + text.Replace("'", "''") + "'";
}
