using System;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Ce qu'un technicien regarde en premier : périphériques en erreur, plantages récents, démarrage.</summary>
public sealed class DiagnosticCollector : ICollector
{
    private const int MaxCrashes = 15;

    public string Title => "Diagnostic";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);

        var os = Wmi.Query("SELECT LastBootUpTime FROM Win32_OperatingSystem").FirstOrDefault();
        var boot = os?.Str("LastBootUpTime") is { Length: > 0 } raw ? System.Management.ManagementDateTimeConverter.ToDateTime(raw) : (DateTime?)null;
        section.Groups.Add(new InfoGroup("Démarrage")
            .Add("Dernier démarrage", boot?.ToString("dd/MM/yyyy HH:mm"))
            .Add("Allumé depuis", boot is { } b ? Duration(DateTime.Now - b) : null)
            .Add("Redémarrage en attente", Wmi.YesNo(WindowsState.IsRebootPending())));

        var devices = new InfoGroup("Périphériques en erreur");
        foreach (var device in Wmi.Query("SELECT Name, PNPClass, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
        {
            var code = device.ULong("ConfigManagerErrorCode");
            devices.Add(device.Str("Name") ?? device.Str("PNPClass") ?? "Périphérique inconnu", DeviceError(code));
        }
        if (devices.Items.Count == 0) devices.Add("Résultat", "Aucun périphérique en erreur");
        section.Groups.Add(devices);

        section.Groups.Add(Crashes());
        section.Groups.Add(RecurringErrors());

        return section;
    }

    /// <summary>Écrans bleus (BugCheck) et arrêts brutaux (Kernel-Power 41) dans le journal Système.</summary>
    private static InfoGroup Crashes()
    {
        var group = new InfoGroup("Plantages récents");

        try
        {
            var query = new EventLogQuery("System", PathType.LogName, CrashXPath()) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            int count = 0;
            for (var record = reader.ReadEvent(); record is not null && count < MaxCrashes; record = reader.ReadEvent(), count++)
            {
                using (record)
                {
                    var when = record.TimeCreated?.ToString("dd/MM/yyyy HH:mm") ?? "Date inconnue";
                    var what = record.Id == 1001 ? "Écran bleu" + BugCheckCode(record) : "Arrêt brutal (coupure, blocage ou bouton power)";
                    group.Add(when, what);
                }
            }
        }
        catch (EventLogException ex)
        {
            group.Add("Erreur", "Journal illisible : " + ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            group.Add("Erreur", "Accès au journal refusé (lancer en administrateur)");
        }

        if (group.Items.Count == 0) group.Add("Résultat", "Aucun plantage enregistré");

        var dumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
        try
        {
            if (Directory.Exists(dumps))
                group.Add("Fichiers minidump", $"{Directory.GetFiles(dumps, "*.dmp").Length} dans {dumps}");
        }
        catch (UnauthorizedAccessException) { }

        return group;
    }

    // ---------- Erreurs récurrentes ----------

    private sealed record ErrorKind(string Log, string XPath, Func<EventRecord, string> Describe);

    private static string Arg(EventRecord r, int i)
    {
        try { return r.Properties.Count > i ? r.Properties[i].Value?.ToString()?.Trim() ?? "" : ""; }
        catch (Exception) { return ""; }
    }

    /// <summary>
    /// Ce qui se répète sur 30 jours dans les journaux, regroupé et compté : un technicien voit tout de suite un disque qui
    /// lâche, une erreur matérielle, un pilote graphique qui plante ou une application qui se ferme toute seule.
    /// </summary>
    private static readonly ErrorKind[] Kinds =
    {
        new("System", "(Provider[@Name='disk'] and (EventID=7 or EventID=11 or EventID=51 or EventID=153)) or ((Provider[@Name='stornvme'] or Provider[@Name='storahci'] or Provider[@Name='iaStorAC'] or Provider[@Name='iaStorA']) and (EventID=11 or EventID=129))",
            r => r.Id == 153 || r.Id == 129 ? "Disque : délais de réponse dépassés (disque ou câble fatigué)" : "Disque : erreurs de lecture ou d'écriture (disque à vérifier ou remplacer)"),
        new("System", "(Provider[@Name='Ntfs'] or Provider[@Name='Microsoft-Windows-Ntfs']) and (EventID=55 or EventID=98 or EventID=140)",
            _ => "Système de fichiers endommagé (lancer chkdsk)"),
        new("System", "Provider[@Name='Microsoft-Windows-WHEA-Logger']",
            r => r.Id == 47 ? "Erreur mémoire corrigée (RAM à tester)" : "Erreur matérielle signalée par le processeur (WHEA : CPU, RAM ou carte mère)"),
        new("System", "Provider[@Name='Display'] and EventID=4101",
            _ => "Pilote graphique qui a cessé de répondre puis a redémarré"),
        new("System", "Provider[@Name='Service Control Manager'] and (EventID=7000 or EventID=7009 or EventID=7023 or EventID=7031 or EventID=7034)",
            r => "Service en échec : " + (Arg(r, 0) is { Length: > 0 } s ? s : "?")),
        new("Application", "Provider[@Name='Application Error'] and EventID=1000",
            r => "Application qui plante : " + (Arg(r, 0) is { Length: > 0 } a ? a : "?")),
        new("Application", "Provider[@Name='Application Hang'] and EventID=1002",
            r => "Application qui ne répond plus : " + (Arg(r, 0) is { Length: > 0 } a ? a : "?")),
    };

    private const int RecurringDays = 30;
    private const int MaxEventsPerKind = 2000;

    private static InfoGroup RecurringErrors()
    {
        var group = new InfoGroup($"Erreurs récurrentes ({RecurringDays} derniers jours)");
        var counts = new System.Collections.Generic.Dictionary<string, (int Count, DateTime? Last)>();
        var unreadable = false;
        var window = (long)TimeSpan.FromDays(RecurringDays).TotalMilliseconds;
        foreach (var kind in Kinds)
        {
            try
            {
                var query = new EventLogQuery(kind.Log, PathType.LogName, $"*[System[({kind.XPath}) and TimeCreated[timediff(@SystemTime) <= {window}]]]") { ReverseDirection = true };
                using var reader = new EventLogReader(query);
                var read = 0;
                for (var record = reader.ReadEvent(); record is not null && read < MaxEventsPerKind; record = reader.ReadEvent(), read++)
                {
                    using (record)
                    {
                        var key = kind.Describe(record);
                        counts.TryGetValue(key, out var entry);
                        counts[key] = (entry.Count + 1, entry.Last ?? record.TimeCreated);
                    }
                }
            }
            catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException) { unreadable = true; }
        }

        // Les plantages d'application isolés sont du bruit : on garde ce qui revient au moins 3 fois.
        foreach (var pair in counts.Where(p => p.Value.Count >= 3 || !p.Key.StartsWith("Application", StringComparison.Ordinal))
                     .OrderByDescending(p => p.Value.Count).Take(12))
        {
            var level = pair.Key.StartsWith("Disque") || pair.Key.StartsWith("Erreur matérielle") || pair.Key.StartsWith("Système de fichiers") ? "⚠ " : "";
            group.Add(level + pair.Key, $"{pair.Value.Count} fois, dernière le {pair.Value.Last:dd/MM/yyyy HH:mm}");
        }
        if (group.Items.Count == 0) group.Add("Résultat", unreadable ? "Journaux illisibles" : "Aucune erreur qui se répète");
        return group;
    }

    private static string CrashXPath(TimeSpan? within = null)
    {
        var crashes =
            "(Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001)" +
            " or (Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41)";
        return within is { } span
            ? $"*[System[({crashes}) and TimeCreated[timediff(@SystemTime) <= {(long)span.TotalMilliseconds}]]]"
            : $"*[System[{crashes}]]";
    }

    /// <summary>Nombre d'écrans bleus et d'arrêts brutaux sur la période, null si le journal est illisible.</summary>
    internal static int? RecentCrashCount(TimeSpan within)
    {
        try
        {
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, CrashXPath(within)));
            int count = 0;
            for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                record.Dispose();
                count++;
            }
            return count;
        }
        catch (EventLogException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string BugCheckCode(EventRecord record)
    {
        // Le premier paramètre de l'événement 1001 contient le code d'arrêt, ex. 0x0000009f (...).
        var text = record.Properties.FirstOrDefault()?.Value?.ToString();
        if (string.IsNullOrEmpty(text)) return "";
        var code = text!.Split(' ', '(').FirstOrDefault();
        return string.IsNullOrEmpty(code) ? "" : $" (code {code})";
    }

    private static string Duration(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{(int)span.TotalDays} j {span.Hours} h" : $"{span.Hours} h {span.Minutes} min";

    internal static string DeviceError(ulong? code) => code switch
    {
        1 => "Mal configuré (code 1)",
        3 => "Pilote corrompu ou mémoire insuffisante (code 3)",
        10 => "Ne peut pas démarrer (code 10)",
        12 => "Ressources insuffisantes (code 12)",
        18 => "Pilote à réinstaller (code 18)",
        19 => "Registre corrompu (code 19)",
        22 => "Désactivé (code 22)",
        24 => "Absent ou mal installé (code 24)",
        28 => "Pilote non installé (code 28)",
        31 => "Ne fonctionne pas correctement (code 31)",
        39 => "Pilote manquant ou corrompu (code 39)",
        43 => "Arrêté après un problème signalé (code 43)",
        45 => "Débranché (code 45)",
        52 => "Pilote non signé (code 52)",
        _ => $"Erreur (code {code})",
    };
}
