using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PcInspector;

/// <summary>
/// Mises à jour Windows qui échouent en boucle, lues dans l'historique de l'agent Windows Update (lecture seule),
/// avec le code d'erreur traduit en clair. Une mise à jour réinstallée avec succès ensuite n'est pas signalée.
/// </summary>
public static class WindowsUpdateFailures
{
    public sealed record Failure(string Title, int Attempts, DateTime Last, string Code, string Meaning);

    private const int MaxHistory = 300;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>Échecs des 60 derniers jours, null si l'historique est illisible.</summary>
    public static List<Failure>? Read()
    {
        var task = Task.Run(ReadHistory);
        return task.Wait(Timeout) ? task.Result : null;
    }

    private static List<Failure>? ReadHistory()
    {
        try
        {
            var type = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (type is null) return null;
            var session = Activator.CreateInstance(type);
            var searcher = Call(session, "CreateUpdateSearcher");
            var total = Convert.ToInt32(Call(searcher, "GetTotalHistoryCount"));
            if (total == 0) return new List<Failure>();
            var history = Call(searcher, "QueryHistory", 0, Math.Min(total, MaxHistory));
            var count = Convert.ToInt32(Get(history, "Count"));

            var since = DateTime.Now.AddDays(-60);
            var entries = new List<(string Title, DateTime Date, int Result, int HResult)>();
            for (var i = 0; i < count; i++)
            {
                var entry = Get(history, "Item", i);
                // Operation 1 = installation ; ResultCode 2 = réussi, 3 = réussi avec erreurs, 4 = échec, 5 = annulé.
                if (Convert.ToInt32(Get(entry, "Operation")) != 1) continue;
                var title = Get(entry, "Title")?.ToString();
                if (string.IsNullOrWhiteSpace(title)) continue;
                var date = Get(entry, "Date") is DateTime d ? d.ToLocalTime() : DateTime.MinValue;
                entries.Add((title!.Trim(), date, Convert.ToInt32(Get(entry, "ResultCode")), Convert.ToInt32(Get(entry, "HResult"))));
            }

            return entries
                .GroupBy(e => Key(e.Title))
                .Select(g =>
                {
                    var lastSuccess = g.Where(e => e.Result is 2 or 3).Select(e => e.Date).DefaultIfEmpty(DateTime.MinValue).Max();
                    var failures = g.Where(e => e.Result is 4 or 5 && e.Date > lastSuccess && e.Date >= since).OrderByDescending(e => e.Date).ToList();
                    if (failures.Count == 0) return null;
                    var code = $"0x{failures[0].HResult:X8}";
                    return new Failure(failures[0].Title, failures.Count, failures[0].Date, code, Explain(failures[0].HResult));
                })
                .Where(f => f is not null).Select(f => f!)
                .OrderByDescending(f => f.Last).ToList();
        }
        catch (Exception ex) when (ex is TargetInvocationException or COMException or InvalidCastException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Même mise à jour d'un mois à l'autre : on regroupe par numéro KB quand il y en a un.</summary>
    private static string Key(string title)
    {
        var kb = System.Text.RegularExpressions.Regex.Match(title, @"KB\d{6,8}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return kb.Success ? kb.Value.ToUpperInvariant() : title;
    }

    /// <summary>Les codes les plus fréquents en atelier, avec la piste de réparation.</summary>
    public static string Explain(int hresult) => unchecked((uint)hresult) switch
    {
        0x800F081F => "fichiers source introuvables : PC Maintenance > Réparer Windows (DISM), sinon réparation par l'ISO",
        0x800F0922 => "partition système réservée trop petite ou VPN actif : couper le VPN, vérifier la partition réservée",
        0x80073712 => "composants Windows endommagés : PC Maintenance > Réparer Windows (DISM + SFC)",
        0x800F0988 => "magasin de composants à nettoyer : DISM /StartComponentCleanup puis réessayer",
        0x80070643 => "échec de l'installateur (souvent la partition de récupération WinRE trop petite pour KB5034441)",
        0x8024402C or 0x80072EE2 or 0x80072EFD or 0x80072F8F => "connexion aux serveurs Microsoft impossible (proxy, DNS, heure du PC ou pare-feu)",
        0x80070002 or 0x80070003 => "fichiers de mise à jour manquants : vider le cache Windows Update (PC Maintenance > Nettoyage)",
        0x8007000D or 0x80240034 => "téléchargement corrompu : vider le cache Windows Update et relancer",
        0x80070005 => "accès refusé : antivirus tiers ou droits du dossier Windows\\SoftwareDistribution",
        0x80070070 => "espace disque insuffisant sur C:",
        0x800705B4 => "délai dépassé : relancer, PC branché sur secteur et sans mise en veille",
        0x80240017 => "mise à jour non applicable à cette version de Windows",
        0x8024200B or 0x80242006 => "base de Windows Update endommagée : vider le cache Windows Update et relancer",
        0x80070422 => "service Windows Update désactivé",
        _ => "code non répertorié : chercher le code sur support.microsoft.com",
    };

    private static object? Call(object? target, string method, params object[] args) =>
        target?.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);

    private static object? Get(object? target, string property, params object[] args) =>
        target?.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, args);
}
