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
            .Add("Redémarrage en attente", Wmi.YesNo(SecurityCollector.IsRebootPending())));

        var devices = new InfoGroup("Périphériques en erreur");
        foreach (var device in Wmi.Query("SELECT Name, PNPClass, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
        {
            var code = device.ULong("ConfigManagerErrorCode");
            devices.Add(device.Str("Name") ?? device.Str("PNPClass") ?? "Périphérique inconnu", DeviceError(code));
        }
        if (devices.Items.Count == 0) devices.Add("Résultat", "Aucun périphérique en erreur");
        section.Groups.Add(devices);

        section.Groups.Add(Crashes());

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

    private static string DeviceError(ulong? code) => code switch
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
