using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Un logiciel pour lequel winget connaît une version plus récente.</summary>
public sealed record WingetUpdate(string Name, string Id, string Installed, string Available);

/// <summary>
/// Logiciels à mettre à jour, lus avec « winget upgrade » sans argument : winget liste alors
/// les mises à jour sans rien installer. Le logiciel ne lance jamais la mise à jour lui-même.
/// </summary>
public sealed class WingetCollector : ICollector
{
    private const string StoreUrl = "https://apps.microsoft.com/detail/9NBLGGH4NNS1";

    public string Title => "Mises à jour logiciels";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        var result = Winget.ReadUpdates();

        if (result.Updates is not { } updates)
        {
            var group = new InfoGroup("Vérification impossible").Add("Raison", result.Error);
            if (result.Missing)
                group.AddLink("Installer winget", "« Programme d'installation d'application » sur le Microsoft Store", StoreUrl);
            section.Groups.Add(group);
            return section;
        }

        if (updates.Count == 0)
        {
            section.Groups.Add(new InfoGroup("Aucune mise à jour disponible")
                .Add("Résultat", "Tous les logiciels connus de winget sont à jour."));
            return section;
        }

        var list = new InfoGroup(updates.Count == 1 ? "1 logiciel à mettre à jour" : $"{updates.Count} logiciels à mettre à jour");
        foreach (var u in updates)
            list.Add(u.Name, $"installé {u.Installed} → disponible {u.Available} · {u.Id}");
        section.Groups.Add(list);

        // Conseil seulement : le technicien lance la commande lui-même s'il le souhaite.
        section.Groups.Add(new InfoGroup("Comment mettre à jour")
            .Add("Tout mettre à jour", "Dans un terminal administrateur : winget upgrade --all")
            .Add("Un seul logiciel", "winget upgrade --id <identifiant> (l'identifiant figure après chaque version)")
            .Add("Avant de commencer", "Fermer les logiciels concernés et prévenir le client : certains redémarrent ou demandent une confirmation."));
        return section;
    }
}

/// <summary>Appel de winget partagé par l'onglet et le bilan, pour ne le lancer qu'une fois par analyse.</summary>
internal static class Winget
{
    public sealed record Result(IReadOnlyList<WingetUpdate>? Updates, string? Error, bool Missing = false);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);

    // « Aucun package installé ne correspond » : winget répond ainsi quand tout est à jour.
    private const int NoApplicationsFound = unchecked((int)0x8A150014);

    private static readonly object Gate = new();
    private static Result? _last;
    private static DateTime _lastTime;

    public static Result ReadUpdates()
    {
        lock (Gate)
        {
            if (_last is not null && DateTime.UtcNow - _lastTime < CacheLifetime) return _last;
            _last = Run();
            _lastTime = DateTime.UtcNow;
            return _last;
        }
    }

    public static Task<Result> ReadUpdatesAsync() => Task.Run(ReadUpdates);

    private static Result Run()
    {
        foreach (var exe in Candidates())
        {
            try
            {
                return Run(exe);
            }
            catch (Win32Exception)
            {
                // winget introuvable à cet emplacement : essayer le suivant.
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                return new Result(null, "Impossible de lancer winget : " + ex.Message);
            }
        }
        return new Result(null, "winget n'est pas installé sur ce PC (ou pas pour ce compte utilisateur).", Missing: true);
    }

    private static IEnumerable<string> Candidates()
    {
        yield return "winget.exe";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
            yield return Path.Combine(local, @"Microsoft\WindowsApps\winget.exe");
    }

    private static Result Run(string exe)
    {
        // Seule la source « winget » est interrogée : celle du Store demande d'accepter des conditions,
        // ce que le logiciel ne fait pas à la place du client.
        var start = new ProcessStartInfo(exe, "upgrade --source winget")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            return new Result(null, $"winget n'a pas répondu en {Timeout.TotalSeconds:0} secondes (connexion Internet ?).");
        }

        var lines = Lines(output.Result);
        var updates = Parse(lines);
        if (updates is not null) return new Result(updates, null);
        if (process.ExitCode is 0 or NoApplicationsFound) return new Result(Array.Empty<WingetUpdate>(), null);

        var message = lines.LastOrDefault(l => l.Trim().Length > 0)?.Trim();
        return new Result(null, $"winget a échoué (code 0x{process.ExitCode:X8})" + (message is null ? "." : $" : {message}"));
    }

    /// <summary>Lignes affichées, sans les animations de progression que winget redessine avec \r.</summary>
    private static List<string> Lines(string output) =>
        output.Replace("\r\n", "\n")
              .Split('\n')
              .Select(l => l.Substring(l.LastIndexOf('\r') + 1).TrimEnd())
              .ToList();

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
        // Nom, Id, Version, Disponible (et Source quand plusieurs sources sont interrogées).
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
