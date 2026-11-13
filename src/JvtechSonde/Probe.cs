using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PcInspector;
using PcInspector.Collectors;
using PcSecurite.Checks;

namespace JvtechSonde;

/// <summary>
/// Les mesures, dans l'ordre du rapport. Chacune est isolée : une mesure qui échoue donne « non mesurable »
/// et n'empêche jamais les autres. Rien n'est modifié sur le PC.
/// </summary>
internal static class Probe
{
    public static List<Point> Run(Settings settings, Action<string>? progress = null)
    {
        var points = new List<Point>();

        // En premier, pendant que le PC est au calme : la charge moyenne du processeur.
        progress?.Invoke($"Mesure de la charge du processeur ({settings.CpuSampleSeconds} s)…");
        Add(points, "Processeur", "utilisation", () => CpuUsage(settings));
        Add(points, "Processeur", "température", () => CpuTemperature(settings));

        progress?.Invoke("Mémoire, carte graphique et disques…");
        Add(points, "Mémoire", "", () => Memory(settings));
        AddMany(points, "Carte graphique", () => Gpus(settings));
        AddMany(points, "Disque", () => Volumes(settings));
        AddMany(points, "Santé disque", DisksHealth);
        Add(points, "Batterie", "usure", () => Battery(settings));

        progress?.Invoke("Windows, sécurité et plantages…");
        Add(points, "Windows", "version", () => Security("Version de Windows", "Windows", "version"));
        Add(points, "Antivirus", "", () => Security("Antivirus", "Antivirus", ""));
        Add(points, "Microsoft Defender", "", () => Security("Microsoft Defender", "Microsoft Defender", ""));
        Add(points, "Pare-feu", "", () => Security("Pare-feu", "Pare-feu", ""));
        Add(points, "Plantages", "30 jours", () => Crashes(settings));
        Add(points, "Redémarrage", "en attente", RebootPending);
        Add(points, "Windows Update", "dernière recherche", () => LastSearch(settings));
        Add(points, "Windows Update", "dernière installation", () => Security("Dernière mise à jour", "Windows Update", "dernière installation"));

        // En dernier : la recherche Windows Update peut prendre jusqu'à 90 secondes.
        progress?.Invoke("Recherche des mises à jour Windows (sans rien installer)…");
        Add(points, "Windows Update", "en attente", () => Security("Mises à jour en attente", "Windows Update", "en attente"));

        return points;
    }

    private static void Add(List<Point> points, string area, string name, Func<Point> measure)
    {
        try { points.Add(measure()); }
        catch (Exception ex) { points.Add(new Point(area, name, Level.Unknown, "mesure impossible : " + Short(ex))); }
    }

    private static void AddMany(List<Point> points, string area, Func<IEnumerable<Point>> measure)
    {
        try { points.AddRange(measure()); }
        catch (Exception ex) { points.Add(new Point(area, "", Level.Unknown, "mesure impossible : " + Short(ex))); }
    }

    private static string Short(Exception ex)
    {
        while (ex is TargetInvocationException or AggregateException && ex.InnerException is { } inner) ex = inner;
        return ex.Message.Split('\n')[0].Trim();
    }

    // ---------- Processeur ----------

    private static Point CpuUsage(Settings settings)
    {
        if (!GetSystemTimes(out var idle0, out var kernel0, out var user0)) return new Point("Processeur", "utilisation", Level.Unknown, "compteurs illisibles");
        Thread.Sleep(TimeSpan.FromSeconds(settings.CpuSampleSeconds));
        if (!GetSystemTimes(out var idle1, out var kernel1, out var user1)) return new Point("Processeur", "utilisation", Level.Unknown, "compteurs illisibles");
        var percent = Rules.CpuPercent(idle0, kernel0, user0, idle1, kernel1, user1);
        return new Point("Processeur", "utilisation", Rules.CpuUsage(percent, settings.CpuUsageWarning),
            percent is { } p ? $"{p:0} % en moyenne sur {settings.CpuSampleSeconds} s" : "relevés incohérents");
    }

    private static Point CpuTemperature(Settings settings)
    {
        var (celsius, source) = CpuTemperatureReader.Read();
        if (celsius is null)
            return new Point("Processeur", "température", Level.Unknown, "pas de sonde lisible sur ce PC");
        return new Point("Processeur", "température", Rules.Above(celsius, settings.CpuTempWarning, settings.CpuTempCritical),
            $"{celsius:0} °C ({source})");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    // ---------- Mémoire ----------

    private static Point Memory(Settings settings)
    {
        var os = Wmi.Query("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem").FirstOrDefault();
        var total = os?.ULong("TotalVisibleMemorySize") * 1024;
        var free = os?.ULong("FreePhysicalMemory") * 1024;
        if (total is not { } t || t == 0 || free is not { } f)
            return new Point("Mémoire", "", Level.Unknown, "quantité illisible");
        var used = t - Math.Min(f, t);
        var percent = used * 100.0 / t;
        return new Point("Mémoire", "", Rules.Above(percent, settings.RamWarning, settings.RamCritical),
            $"{percent:0} % utilisée ({Wmi.FormatBytes(used)} sur {Wmi.FormatBytes(t)}, {Wmi.FormatBytes(f)} libre)");
    }

    // ---------- Carte graphique ----------

    private static IEnumerable<Point> Gpus(Settings settings)
    {
        var readings = GpuSensors.Read();
        if (readings.Count == 0)
            return new[] { new Point("Carte graphique", "température", Level.Unknown, "pas de sonde lisible (carte intégrée ou pilote de base)") };
        return readings.Select(r => new Point("Carte graphique", r.Name,
            Rules.Above(r.TemperatureC, settings.GpuTempWarning, settings.GpuTempCritical), $"{r.TemperatureC:0} °C"));
    }

    // ---------- Disques ----------

    private static IEnumerable<Point> Volumes(Settings settings)
    {
        var list = new List<Point>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady || drive.TotalSize < 1L << 30) continue;
                var used = drive.TotalSize - drive.TotalFreeSpace;
                var percent = used * 100.0 / drive.TotalSize;
                list.Add(new Point("Disque", drive.Name.TrimEnd('\\'), Rules.Above(percent, settings.DiskWarning, settings.DiskCritical),
                    $"{percent:0} % rempli ({Wmi.FormatBytes((ulong)drive.TotalFreeSpace)} libres sur {Wmi.FormatBytes((ulong)drive.TotalSize)})"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                list.Add(new Point("Disque", drive.Name.TrimEnd('\\'), Level.Unknown, "illisible : " + ex.Message));
            }
        }
        if (list.Count == 0) list.Add(new Point("Disque", "", Level.Unknown, "aucun volume lisible"));
        return list;
    }

    private static IEnumerable<Point> DisksHealth()
    {
        var names = Wmi.QueryOptional("SELECT DeviceId, FriendlyName FROM MSFT_PhysicalDisk", Wmi.StorageScope)
            .Where(d => d.Str("DeviceId") is not null)
            .GroupBy(d => d.Str("DeviceId")!)
            .ToDictionary(g => g.Key, g => g.First().Str("FriendlyName"));

        var list = new List<Point>();
        foreach (var pair in DiskHealth.ReadAll().OrderBy(p => p.Key))
        {
            var name = names.TryGetValue(pair.Key, out var n) && !string.IsNullOrWhiteSpace(n) ? n! : $"n° {pair.Key}";
            var result = pair.Value;
            var level = Rules.FromDiskHealth(result);
            var value = result.Problems.Count > 0
                ? string.Join(" ; ", result.Problems.OrderBy(p => p.Severity).Select(p => p.Message))
                : result.Unavailable is { } why ? "contrôle impossible : " + why : "aucun signe de panne";
            list.Add(new Point("Santé disque", name, level, value));
        }
        if (list.Count == 0) list.Add(new Point("Santé disque", "", Level.Unknown, "aucun disque lisible (lancer en administrateur)"));
        return list;
    }

    // ---------- Batterie ----------

    private static Point Battery(Settings settings)
    {
        if (Wmi.QueryOptional("SELECT Name FROM Win32_Battery").Count == 0)
            return new Point("Batterie", "usure", Level.NotApplicable, "pas de batterie (PC fixe)");
        var wear = BatteryCollector.WearPercent();
        if (wear is null) return new Point("Batterie", "usure", Level.Unknown, "capacité non fournie par la batterie");
        var w = Math.Max(0, wear.Value);
        return new Point("Batterie", "usure", Rules.Above(w, settings.BatteryWearWarning, settings.BatteryWearCritical),
            $"{w:0} % d'usure (capacité restante {100 - w:0} %)");
    }

    // ---------- Windows et sécurité (mêmes contrôles que PC Sécurité) ----------

    private static Point Security(string checkName, string area, string name)
    {
        var check = SecurityChecks.All.FirstOrDefault(c => c.Name == checkName);
        if (check.Run is null) return new Point(area, name, Level.Unknown, "contrôle introuvable");
        var result = SecurityChecks.RunSafely(check.Name, check.Run);
        return new Point(area, name, Rules.FromSecurity(result.Status), result.Detail);
    }

    private static Point Crashes(Settings settings)
    {
        var count = DiagnosticCollector.RecentCrashCount(TimeSpan.FromDays(30));
        return new Point("Plantages", "30 jours", Rules.Crashes(count, settings.CrashWarning, settings.CrashCritical),
            count switch
            {
                null => "journal Système illisible",
                0 => "aucun écran bleu ni arrêt brutal",
                var c => $"{c} écran(s) bleu(s) ou arrêt(s) brutal(aux)",
            });
    }

    private static Point RebootPending() =>
        WindowsState.IsRebootPending()
            ? new Point("Redémarrage", "en attente", Level.Warning, "Windows attend un redémarrage pour finir une installation")
            : new Point("Redémarrage", "en attente", Level.Ok, "aucun");

    /// <summary>Date de la dernière recherche réussie de Windows Update (agent Windows Update, en lecture).</summary>
    private static Point LastSearch(Settings settings)
    {
        var last = LastSearchSuccess();
        var level = Rules.UpdateSearch(last, DateTime.Now, settings.UpdateSearchDays);
        return new Point("Windows Update", "dernière recherche", level,
            last is { } d ? $"le {d:dd/MM/yyyy HH:mm} (il y a {(int)(DateTime.Now - d).TotalDays} jour(s))" : "date inconnue");
    }

    private static DateTime? LastSearchSuccess()
    {
        var search = Task.Run(() =>
        {
            var type = Type.GetTypeFromProgID("Microsoft.Update.AutoUpdate");
            if (type is null) return (DateTime?)null;
            var auto = Activator.CreateInstance(type);
            var results = auto?.GetType().InvokeMember("Results", BindingFlags.GetProperty, null, auto, null);
            var date = results?.GetType().InvokeMember("LastSearchSuccessDate", BindingFlags.GetProperty, null, results, null);
            // L'agent renvoie une date UTC ; une date très ancienne veut dire « jamais ».
            return date is DateTime d && d.Year > 2000 ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime() : (DateTime?)null;
        });
        return search.Wait(TimeSpan.FromSeconds(20)) ? search.Result : null;
    }
}
