using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PcInspector;

namespace PcMaintenance.Maintenance;

/// <summary>Lecture des mises à jour (même analyse que l'onglet de PC Inspector) et « winget upgrade --all ».</summary>
internal static class Winget
{
    public sealed record ListResult(IReadOnlyList<WingetUpdate>? Updates, string? Error);

    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(90);

    private const string Missing = WingetShell.MissingMessage;

    /// <summary>Liste des mises à jour, sans rien installer.</summary>
    public static Task<ListResult> ListAsync() => Task.Run(() =>
    {
        Process? process = null;
        try
        {
            process = WingetShell.Start("upgrade --source winget --accept-source-agreements");
            if (process is null) return new ListResult(null, Missing);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit((int)ListTimeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                return new ListResult(null, $"winget n'a pas répondu en {ListTimeout.TotalSeconds:0} secondes (connexion Internet ?).");
            }

            var lines = WingetShell.Lines(output.Result);
            if (WingetShell.ParseUpgradeTable(lines) is { } updates) return new ListResult(updates, null);
            if (process.ExitCode is 0 or WingetShell.NoApplicationsFound) return new ListResult(Array.Empty<WingetUpdate>(), null);
            return new ListResult(null, $"winget a échoué (code 0x{process.ExitCode:X8}).");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return new ListResult(null, "Impossible de lancer winget : " + ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    });

    /// <summary>
    /// Met à jour tous les logiciels connus de winget, en silencieux, et recopie la sortie dans le journal.
    /// Renvoie le code de sortie de winget, ou null s'il est introuvable.
    /// </summary>
    // Source « winget » seulement : celle du Store demande un compte et ses propres conditions.
    public static Task<int?> UpgradeAllAsync(IProgress<string> log) =>
        RunAsync("upgrade --all --source winget --silent --accept-source-agreements --accept-package-agreements --disable-interactivity", log);

    /// <summary>Installe un logiciel par son identifiant winget exact (ex. VideoLAN.VLC).</summary>
    public static Task<int?> InstallAsync(string id, IProgress<string> log) =>
        RunAsync($"install --exact --id {id} --source winget --silent --accept-source-agreements --accept-package-agreements --disable-interactivity", log);

    private static Task<int?> RunAsync(string arguments, IProgress<string> log) => Task.Run<int?>(() =>
    {
        using var process = WingetShell.Start(arguments);
        if (process is null)
        {
            log.Report(Missing);
            return null;
        }
        process.StandardInput.Close();

        string? line;
        while ((line = process.StandardOutput.ReadLine()) is not null)
        {
            var text = WingetShell.Visible(line).Trim();
            if (WingetShell.IsProgress(text)) continue;
            log.Report(text);
        }
        process.WaitForExit();
        return process.ExitCode;
    });
}
