using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcMaintenance.Maintenance;

/// <summary>Un logiciel pour lequel winget connaît une version plus récente.</summary>
internal sealed record WingetUpdate(string Name, string Id, string Installed, string Available);

/// <summary>Lecture des mises à jour (même analyse que l'onglet de PC Inspector) et « winget upgrade --all ».</summary>
internal static class Winget
{
    public sealed record ListResult(IReadOnlyList<WingetUpdate>? Updates, string? Error);

    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(90);

    // « Aucun package installé ne correspond » : winget répond ainsi quand tout est à jour.
    private const int NoApplicationsFound = unchecked((int)0x8A150014);

    private const string Missing = "winget n'est pas installé sur ce PC (« Programme d'installation d'application » sur le Microsoft Store).";

    /// <summary>Liste des mises à jour, sans rien installer.</summary>
    public static Task<ListResult> ListAsync() => Task.Run(() =>
    {
        Process? process = null;
        try
        {
            process = Start("upgrade --source winget --accept-source-agreements");
            if (process is null) return new ListResult(null, Missing);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit((int)ListTimeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                return new ListResult(null, $"winget n'a pas répondu en {ListTimeout.TotalSeconds:0} secondes (connexion Internet ?).");
            }

            var lines = output.Result.Replace("\r\n", "\n").Split('\n').Select(Visible).ToList();
            if (Parse(lines) is { } updates) return new ListResult(updates, null);
            if (process.ExitCode is 0 or NoApplicationsFound) return new ListResult(Array.Empty<WingetUpdate>(), null);
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

    /// <summary>Installe un logiciel par son identifiant winget exact (ex. Glarysoft.GlaryUtilities).</summary>
    public static Task<int?> InstallAsync(string id, IProgress<string> log) =>
        RunAsync($"install --exact --id {id} --source winget --silent --accept-source-agreements --accept-package-agreements --disable-interactivity", log);

    private static Task<int?> RunAsync(string arguments, IProgress<string> log) => Task.Run<int?>(() =>
    {
        using var process = Start(arguments);
        if (process is null)
        {
            log.Report(Missing);
            return null;
        }
        process.StandardInput.Close();

        string? line;
        while ((line = process.StandardOutput.ReadLine()) is not null)
        {
            var text = Visible(line).Trim();
            if (IsProgress(text)) continue;
            log.Report(text);
        }
        process.WaitForExit();
        return process.ExitCode;
    });

    private static Process? Start(string arguments)
    {
        foreach (var exe in Candidates())
        {
            try
            {
                return Process.Start(new ProcessStartInfo(exe, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                });
            }
            catch (Win32Exception)
            {
                // winget introuvable à cet emplacement : essayer le suivant.
            }
        }
        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        yield return "winget.exe";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
            yield return Path.Combine(local, @"Microsoft\WindowsApps\winget.exe");
    }

    /// <summary>Ce qui reste affiché d'une ligne que winget redessine avec \r (barres de progression).</summary>
    private static string Visible(string line) => line.Substring(line.LastIndexOf('\r') + 1).TrimEnd();

    /// <summary>Barres de progression, pourcentages et petit moulinet « - \ | / » : inutiles dans le journal.</summary>
    private static bool IsProgress(string text) =>
        text.Length <= 1 || text.IndexOfAny(new[] { '█', '▒' }) >= 0;

    /// <summary>
    /// Lit le tableau de winget : une ligne d'en-têtes (traduits selon la langue de Windows), une ligne de tirets,
    /// puis une ligne par logiciel. Les colonnes sont repérées par la position des en-têtes.
    /// Renvoie null s'il n'y a pas de tableau.
    /// </summary>
    internal static List<WingetUpdate>? Parse(IReadOnlyList<string> lines)
    {
        var dashes = -1;
        for (var i = 1; i < lines.Count; i++)
        {
            if (lines[i].Length >= 10 && lines[i].All(c => c == '-')) { dashes = i; break; }
        }
        if (dashes < 0) return null;

        var header = lines[dashes - 1];
        var starts = new List<int>();
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' ')) starts.Add(i);
        }
        // Nom, Id, Version, Disponible (et Source).
        if (starts.Count < 4) return null;

        string Column(string line, int index)
        {
            var from = starts[index];
            if (from >= line.Length) return "";
            var to = index + 1 < starts.Count ? Math.Min(starts[index + 1], line.Length) : line.Length;
            return line.Substring(from, to - from).Trim();
        }

        var updates = new List<WingetUpdate>();
        for (var i = dashes + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            // La ligne « N mises à jour disponibles » qui suit le tableau n'atteint pas la colonne Disponible.
            if (line.Length == 0 || Column(line, 3).Length == 0) break;
            updates.Add(new WingetUpdate(Column(line, 0), Column(line, 1), Column(line, 2), Column(line, 3)));
        }
        return updates;
    }
}
