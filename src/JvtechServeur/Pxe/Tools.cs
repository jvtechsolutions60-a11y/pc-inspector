using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace JvtechServeur.Pxe;

/// <summary>Lancement des outils de Windows (DISM, bcdedit, robocopy, PowerShell) avec leur sortie dans le journal.</summary>
internal static class Tools
{
    private static Encoding Console => Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

    /// <summary>Lance un programme et renvoie son code de sortie et ses lignes (null s'il n'a pas pu démarrer).</summary>
    public static (int? Code, List<string> Lines) Run(string exe, string arguments, Action<string>? log, TimeSpan timeout, Encoding? encoding = null)
    {
        var lines = new List<string>();
        var start = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding ?? Console,
            StandardErrorEncoding = encoding ?? Console,
        };
        try
        {
            using var process = new Process { StartInfo = start };
            void Line(string? text)
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                var clean = text!.Trim();
                // DISM dessine sa barre de progression ligne par ligne : inutile dans le journal.
                if (clean.StartsWith("[") && clean.Contains("%")) return;
                lock (lines) lines.Add(clean);
                log?.Invoke(clean);
            }
            process.OutputDataReceived += (_, e) => Line(e.Data);
            process.ErrorDataReceived += (_, e) => Line(e.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds)))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                log?.Invoke($"{Path.GetFileName(exe)} ne répond plus : arrêté.");
                return (null, lines);
            }
            process.WaitForExit();
            return (process.ExitCode, lines);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            log?.Invoke($"{exe} : {ex.Message}");
            return (null, lines);
        }
    }

    /// <summary>Script PowerShell encodé (pas de souci de guillemets), sortie en UTF-8.</summary>
    public static (int? Code, List<string> Lines) PowerShell(string script, Action<string>? log, TimeSpan timeout)
    {
        var full = "$ProgressPreference='SilentlyContinue'\n[Console]::OutputEncoding=[Text.Encoding]::UTF8\n" + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded, log, timeout, Encoding.UTF8);
    }

    public static string Quote(string text) => "'" + text.Replace("'", "''") + "'";

    public static string System32(string exe) => Path.Combine(Environment.SystemDirectory, exe);
}
