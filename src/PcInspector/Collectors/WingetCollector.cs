using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PcInspector.Models;

namespace PcInspector.Collectors;

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
        try
        {
            // Seule la source « winget » est interrogée : celle du Store demande d'accepter des conditions,
            // ce que le logiciel ne fait pas à la place du client.
            using var process = WingetShell.Start("upgrade --source winget");
            if (process is null) return new Result(null, "winget n'est pas installé sur ce PC (ou pas pour ce compte utilisateur).", Missing: true);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                return new Result(null, $"winget n'a pas répondu en {Timeout.TotalSeconds:0} secondes (connexion Internet ?).");
            }

            var lines = WingetShell.Lines(output.Result);
            if (WingetShell.ParseUpgradeTable(lines) is { } updates) return new Result(updates, null);
            if (process.ExitCode is 0 or WingetShell.NoApplicationsFound) return new Result(Array.Empty<WingetUpdate>(), null);

            var message = lines.LastOrDefault(l => l.Trim().Length > 0)?.Trim();
            return new Result(null, $"winget a échoué (code 0x{process.ExitCode:X8})" + (message is null ? "." : $" : {message}"));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return new Result(null, "Impossible de lancer winget : " + ex.Message);
        }
    }
}
