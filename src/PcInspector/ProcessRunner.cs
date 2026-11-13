using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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
        using (var job = JobObject.Create())
        {
            // Le programme et tout ce qu'il lance (PowerShell → DISM, installeur…) sont rangés dans un « job » Windows :
            // en cas de délai dépassé, tout l'arbre est arrêté, pas seulement le premier programme (audit I12).
            job?.Assign(process);

            // Les lignes passent par une file lue par un seul fil : un appelant lent ne bloque ni la lecture des sorties
            // ni l'arrêt du programme (audit I13). File bornée : si l'appelant ne suit plus, le programme attend.
            // Pas de « using » : le fil de lecture peut encore la vider après le retour (délai dépassé).
            var queue = new BlockingCollection<string>(10000);
            Thread? reader = null;
            if (redirect)
            {
                reader = new Thread(() =>
                {
                    foreach (var text in queue.GetConsumingEnumerable())
                    {
                        try { line!(text); }
                        catch (Exception ex) when (ex is not OutOfMemoryException) { }
                    }
                }) { IsBackground = true, Name = "Sortie " + Path.GetFileName(file) };
                reader.Start();
                process.OutputDataReceived += (_, e) => { if (e.Data is not null) TryAdd(queue, e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data is not null && includeErrors) TryAdd(queue, e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            if (!process.WaitForExit((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds)))
            {
                if (job is null || !job.Terminate())
                    try { process.Kill(); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                queue.CompleteAdding();
                reader?.Join(TimeSpan.FromSeconds(5));
                return new ProcessResult(null, true, true);
            }
            // Attend la fin de la lecture des sorties, puis que la file soit vidée (30 s au plus si l'appelant bloque).
            process.WaitForExit();
            queue.CompleteAdding();
            reader?.Join(TimeSpan.FromSeconds(30));
            return new ProcessResult(process.ExitCode, true, false);
        }
    }

    private static void TryAdd(BlockingCollection<string> queue, string text)
    {
        try { queue.Add(text); }
        catch (InvalidOperationException) { } // file fermée ou libérée après l'arrêt : ligne ignorée
    }

    /// <summary>Job Windows minimal : regroupe un programme et ses enfants pour pouvoir tous les arrêter.</summary>
    private sealed class JobObject : IDisposable
    {
        private IntPtr _handle;
        private JobObject(IntPtr handle) => _handle = handle;

        public static JobObject? Create()
        {
            var handle = CreateJobObject(IntPtr.Zero, null);
            return handle == IntPtr.Zero ? null : new JobObject(handle);
        }

        public void Assign(Process process)
        {
            try { AssignProcessToJobObject(_handle, process.Handle); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        }

        public bool Terminate() => TerminateJobObject(_handle, 1);

        // Pas de « tuer à la fermeture » : un programme qui lance volontairement un autre programme durable (installeur)
        // ne doit pas le voir arrêté quand il se termine normalement. Seul le délai dépassé arrête l'arbre.
        public void Dispose()
        {
            if (_handle == IntPtr.Zero) return;
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateJobObject(IntPtr job, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
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
