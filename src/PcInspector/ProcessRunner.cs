using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace PcInspector;

/// <summary>Résultat d'un programme lancé : code de sortie, ou pourquoi il n'y en a pas.</summary>
public readonly struct ProcessResult
{
    public ProcessResult(int? exitCode, bool started, bool timedOut) { ExitCode = exitCode; Started = started; TimedOut = timedOut; }
    /// <summary>Null si le programme n'a pas démarré ou a été arrêté parce qu'il ne répondait plus.</summary>
    public int? ExitCode { get; }
    public bool Started { get; }
    public bool TimedOut { get; }
}

/// <summary>
/// Lancement des outils de Windows (robocopy, netsh, DISM, PowerShell…) commun à tous les logiciels JVTECH :
/// sans fenêtre, par leur chemin complet dans System32 (voir <see cref="AppPaths.SystemTool"/>), arrêtés s'ils ne
/// répondent plus, et leurs lignes renvoyées une par une.
/// </summary>
public static class ProcessRunner
{
    /// <summary>Encodage de la console (850 en France) : celui des outils de Windows comme robocopy ou netsh.</summary>
    public static Encoding ConsoleEncoding => Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

    /// <param name="line">Reçoit chaque ligne affichée (et les erreurs si <paramref name="includeErrors"/>). Null : sortie ignorée.</param>
    public static ProcessResult Run(string file, string arguments, Action<string>? line, TimeSpan timeout,
        Encoding? encoding = null, bool includeErrors = true)
    {
        var redirect = line is not null;
        var start = new ProcessStartInfo(AppPaths.SystemTool(file), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
        };
        if (redirect)
        {
            start.StandardOutputEncoding = encoding ?? ConsoleEncoding;
            start.StandardErrorEncoding = encoding ?? ConsoleEncoding;
        }

        Process? process;
        try { process = Process.Start(start); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            line?.Invoke($"{Path.GetFileName(file)} introuvable : {ex.Message}");
            return new ProcessResult(null, false, false);
        }
        if (process is null) return new ProcessResult(null, false, false);

        using (process)
        {
            var gate = new object();
            if (redirect)
            {
                process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (gate) line!(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data is not null && includeErrors) lock (gate) line!(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            if (!process.WaitForExit((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds)))
            {
                try { process.Kill(); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                return new ProcessResult(null, true, true);
            }
            // Attend la fin de la lecture des sorties.
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, true, false);
        }
    }

    /// <summary>Script PowerShell encodé (pas de souci de guillemets), sortie en UTF-8.</summary>
    public static ProcessResult PowerShell(string script, Action<string>? line, TimeSpan timeout, bool includeErrors = true)
    {
        var full = "$ProgressPreference='SilentlyContinue'\n[Console]::OutputEncoding=[Text.Encoding]::UTF8\n" + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
            line, timeout, Encoding.UTF8, includeErrors);
    }

    /// <summary>Met un texte entre apostrophes pour PowerShell.</summary>
    public static string PowerShellQuote(string text) => "'" + text.Replace("'", "''") + "'";
}
