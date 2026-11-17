using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PcInspector.Collectors;
using PcInspector.Models;

namespace PcInspector.Report;

/// <summary>
/// Rapport complet de PC Inspector, fait pour ne jamais échouer en entier : utilisé par PC Inspector (bouton
/// « Rapport ») et par PC Maintenance (automatique au lancement depuis la clé et à la fin de chaque maintenance).
///
/// Pourquoi l'export échouait sur certains PC :
///   - une partie qui ne répond pas (WMI bloqué, winget…) faisait attendre le rapport sans fin ;
///   - le dossier Documents refusé (protection anti-rançongiciel de Windows, Documents redirigé vers OneDrive ou un
///     serveur absent) : aucun autre emplacement n'était essayé ;
///   - le PDF passe par Edge, qui peut échouer ou être bloqué : une erreur autre qu'« IOException » empêchait
///     d'enregistrer la page web à la place.
/// Ici : chaque partie a un délai et ses erreurs sont écrites dans le rapport ; la page web (HTML, sans navigateur)
/// est toujours écrite en premier, puis le PDF si on le demande ; plusieurs emplacements sont essayés ; tout est noté
/// dans rapport.log.
/// </summary>
public static class InspectorReport
{
    /// <summary>Délai global pour lire toutes les parties ; au-delà, les parties pas finies sont notées « non lues ».</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromMinutes(4);

    /// <summary>
    /// Les parties du rapport de PC Inspector, sans le test de débit ni les tests de performance (lancés seulement
    /// à la demande dans PC Inspector).
    /// </summary>
    public static List<ICollector> Collectors() => new()
    {
        new HealthCollector(),
        new SummaryCollector(),
        new CpuCollector(),
        new MotherboardCollector(),
        new MemoryCollector(),
        new GpuCollector(),
        new StorageCollector(),
        new DiskUsageCollector(),
        new NetworkCollector(),
        new TemperatureCollector(),
        new DiagnosticCollector(),
        new MissingDriversCollector(),
        new BootPerformanceCollector(),
        new BatteryCollector(),
        new Windows11Collector(),
        new LicenseCollector(),
        new UpgradeQuoteCollector(),
        new SystemCollector(),
        new SoftwareCollector(),
        new WingetCollector(),
        new DriversCollector(),
        new ServicesCollector(),
        new StartupCollector(),
    };

    /// <summary>Une partie qui plante donne une section avec l'erreur, jamais une exception.</summary>
    public static InfoSection CollectSafely(ICollector collector)
    {
        try
        {
            return collector.Collect() ?? new InfoSection(collector.Title) { Error = "Aucune information lue." };
        }
        catch (Exception ex)
        {
            return new InfoSection(collector.Title) { Error = $"Impossible de lire ces informations : {ex.Message}" };
        }
    }

    /// <summary>Lit toutes les parties (4 à la fois) dans le délai donné.</summary>
    public static async Task<List<InfoSection>> CollectAsync(IReadOnlyList<ICollector> collectors, TimeSpan? budget = null)
    {
        var gate = new SemaphoreSlim(4);
        var tasks = collectors.Select(c => Task.Run(async () =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try { return CollectSafely(c); }
            finally { gate.Release(); }
        })).ToList();
        return await Finish(collectors, tasks, budget ?? DefaultBudget).ConfigureAwait(false);
    }

    /// <summary>Attend les lectures déjà lancées ; celles qui dépassent le délai sont notées sans bloquer le rapport.</summary>
    public static async Task<List<InfoSection>> Finish(IReadOnlyList<ICollector> collectors, IReadOnlyList<Task<InfoSection>> tasks, TimeSpan budget)
    {
        await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(budget)).ConfigureAwait(false);
        return collectors.Select((c, i) => Result(c.Title, tasks[i], budget)).ToList();
    }

    internal static InfoSection Result(string title, Task<InfoSection> task, TimeSpan budget)
    {
        if (task.Status == TaskStatus.RanToCompletion && task.Result is { } section) return section;
        if (task.IsFaulted)
            return new InfoSection(title) { Error = "Impossible de lire ces informations : " + task.Exception?.GetBaseException().Message };
        return new InfoSection(title) { Error = $"Non lu : pas de réponse en {budget.TotalMinutes:0} minutes (le reste du rapport est complet)." };
    }

    /// <summary>Résultat de l'enregistrement : chemin du fichier (null si rien n'a pu être écrit) et ce qui a échoué.</summary>
    public sealed record Saved(string? Path, string? HtmlPath, IReadOnlyList<string> Problems);

    /// <summary>
    /// Enregistre le rapport : la page web d'abord, dans le premier emplacement qui accepte l'écriture, puis le PDF
    /// à côté si demandé. Ne lève jamais d'exception.
    /// </summary>
    public static Saved Save(ReportInfo info, IReadOnlyList<InfoSection> sections, string baseName, string preferredFolder, bool pdf)
    {
        var problems = new List<string>();
        string html;
        try { html = ReportWriter.Html(info, sections); }
        catch (Exception ex)
        {
            problems.Add("Mise en page : " + ex.Message + " (rapport en texte)");
            html = "<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>Rapport</title></head><body><pre>"
                   + System.Net.WebUtility.HtmlEncode(SafeText(info, sections)) + "</pre></body></html>";
        }

        var name = FileName(baseName);
        string? htmlPath = null;
        foreach (var folder in Folders(preferredFolder))
        {
            var error = TryWrite(folder, name + ".html", html, out var path);
            if (error is null) { htmlPath = path; break; }
            problems.Add($"{folder} : {error}");
        }

        string? result = htmlPath;
        if (htmlPath is not null && pdf)
        {
            var pdfPath = Path.ChangeExtension(htmlPath, ".pdf");
            try
            {
                PdfPrinter.FromHtml(html, pdfPath);
                result = pdfPath;
            }
            catch (Exception ex) { problems.Add("PDF non créé (la page web est enregistrée) : " + FirstLine(ex.Message)); }
        }

        Log(result is null
            ? "ÉCHEC : rapport non enregistré\r\n    " + string.Join("\r\n    ", problems)
            : "Rapport enregistré : " + result + (problems.Count > 0 ? "\r\n    " + string.Join("\r\n    ", problems) : ""));
        return new Saved(result, htmlPath, problems);
    }

    /// <summary>Nom de fichier sans caractère interdit ni chemin trop long.</summary>
    internal static string FileName(string baseName)
    {
        var name = string.IsNullOrWhiteSpace(baseName) ? "Rapport" : baseName.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        if (name.Length > 120) name = name.Substring(0, 120).TrimEnd();
        return name;
    }

    /// <summary>Emplacements essayés dans l'ordre : le dossier client, puis des dossiers que Windows laisse presque toujours écrire.</summary>
    internal static IEnumerable<string> Folders(string preferred)
    {
        var list = new List<string?> { preferred };
        try { list.Add(AppPaths.ReportsRoot); } catch (Exception) { }
        if (AppPaths.IsPortable) list.Add(Path.Combine(AppPaths.ExeFolder, "Rapports"));
        list.Add(Special(Environment.SpecialFolder.DesktopDirectory, "Rapports JVTECH"));
        list.Add(Special(Environment.SpecialFolder.CommonDocuments, @"JVTECH\Rapports"));
        list.Add(Special(Environment.SpecialFolder.CommonApplicationData, @"JVTECH\Rapports"));
        try { list.Add(Path.Combine(Path.GetTempPath(), "JVTECH", "Rapports")); } catch (Exception) { }
        return list.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string? Special(Environment.SpecialFolder folder, string sub)
    {
        try
        {
            var root = Environment.GetFolderPath(folder);
            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, sub);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Écrit le fichier (nom libre si un fichier du même nom est ouvert) et vérifie qu'il est bien là. Renvoie l'erreur, ou null.</summary>
    private static string? TryWrite(string folder, string fileName, string content, out string? path)
    {
        path = null;
        try
        {
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, fileName);
            for (var i = 2; File.Exists(target) && i < 100; i++)
                target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(fileName)} ({i}){Path.GetExtension(fileName)}");
            File.WriteAllText(target, content, new UTF8Encoding(true));
            if (!File.Exists(target) || new FileInfo(target).Length == 0) return "fichier vide après écriture";
            path = target;
            return null;
        }
        catch (Exception ex) { return FirstLine(ex.Message); }
    }

    private static string SafeText(ReportInfo info, IReadOnlyList<InfoSection> sections)
    {
        try { return ReportWriter.Text(info, sections); }
        catch (Exception ex) { return "Rapport illisible : " + ex.Message; }
    }

    private static string FirstLine(string message) => message.Split('\n')[0].Trim();

    /// <summary>Journal des rapports : rapport.log avec les réglages (sur la clé en mode clé USB).</summary>
    public static string LogPath
    {
        get
        {
            try { return Path.Combine(AppPaths.SettingsFolder, "rapport.log"); }
            catch (Exception) { return Path.Combine(Path.GetTempPath(), "JVTECH-rapport.log"); }
        }
    }

    public static void Log(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {AppInfo.Product} {AppInfo.Version} {Environment.MachineName} : {message}\r\n";
        foreach (var path in new[] { LogPath, Path.Combine(Path.GetTempPath(), "JVTECH-rapport.log") })
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Delete(path);
                File.AppendAllText(path, line, new UTF8Encoding(false));
                return;
            }
            catch (Exception) { }
        }
    }
}
