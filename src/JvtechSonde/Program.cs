using System;
using System.IO;
using System.Linq;
using System.Text;
using PcInspector;

namespace JvtechSonde;

/// <summary>
/// JVTECH Sonde. Sans argument (ou --mesurer) : mesure l'état du PC, l'affiche, l'écrit dans
/// C:\ProgramData\JVTECH\Sonde et l'envoie à Uptime Kuma. --installer / --desinstaller : tâche planifiée.
/// Lecture seule : la sonde n'installe, ne corrige et ne supprime rien sur le PC (à part ses propres fichiers).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch (IOException) { }

        var options = args.Select(a => a.Trim().ToLowerInvariant()).ToList();
        try
        {
            if (options.Contains("--aide") || options.Contains("/?") || options.Contains("-h") || options.Contains("--help"))
            {
                Help();
                return 0;
            }
            if (options.Contains("--installer")) return Installer.Install(Console.WriteLine);
            if (options.Contains("--desinstaller")) return Installer.Uninstall(Console.WriteLine);
            return Measure(send: !options.Contains("--sans-envoi"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erreur : " + ex.Message);
            Results.Log("Erreur : " + ex);
            return 10;
        }
    }

    private static int Measure(bool send)
    {
        var started = DateTime.Now;
        var settings = Settings.Load(Path.Combine(AppPaths.ExeFolder, "sonde.ini"));
        Console.WriteLine($"JVTECH Sonde {AppInfo.Version} · {settings.Machine}" + (settings.Client.Length > 0 ? $" · {settings.Client}" : ""));
        foreach (var line in settings.Ignored) Console.WriteLine($"sonde.ini : ligne ignorée « {line} »");
        if (!Installer.IsAdministrator())
            Console.WriteLine("Attention : sans les droits administrateur, la santé des disques et Defender ne sont pas lisibles.");

        var points = Probe.Run(settings, Console.WriteLine);
        var overall = Rules.Overall(points);

        Console.WriteLine();
        foreach (var point in points) Console.WriteLine($"{point.Mark} {point.Label} : {point.Value}");
        Console.WriteLine();
        Console.WriteLine($"Bilan : {Point.LevelCode(overall)} ({points.Count(p => p.Level == Level.Critical)} rouge, " +
                          $"{points.Count(p => p.Level == Level.Warning)} orange, {points.Count(p => p.Level == Level.Ok)} ok) en {(DateTime.Now - started).TotalSeconds:0} s");

        var folder = Results.Save(Results.ToJson(settings, started, points));
        Console.WriteLine(folder is null ? "Résultats non enregistrés (dossier ProgramData inaccessible)." : $"Résultats : {folder}");

        string sent;
        if (!send) sent = "envoi désactivé (--sans-envoi)";
        else if (string.IsNullOrWhiteSpace(settings.KumaPush)) sent = "pas d'envoi (kuma_push vide dans sonde.ini)";
        else
        {
            var error = Kuma.Send(settings.KumaPush, points, settings.AlertOnWarning);
            sent = error is null
                ? $"envoyé à Uptime Kuma ({Kuma.Status(points, settings.AlertOnWarning)}) : {Kuma.Masked(settings.KumaPush)}"
                : "échec de l'envoi à Uptime Kuma : " + error;
        }
        Console.WriteLine(sent);
        Results.Log($"{Point.LevelCode(overall)} · {points.Count} points · {sent}");
        return 0;
    }

    private static void Help()
    {
        Console.WriteLine($"JVTECH Sonde {AppInfo.Version} : contrôle de l'état du PC, en lecture seule.");
        Console.WriteLine();
        Console.WriteLine("  JvtechSonde.exe                 mesure, affiche, enregistre et envoie à Uptime Kuma");
        Console.WriteLine("  JvtechSonde.exe --sans-envoi    mesure et affiche, sans rien envoyer");
        Console.WriteLine("  JvtechSonde.exe --installer     copie dans Program Files et crée la tâche planifiée quotidienne");
        Console.WriteLine("  JvtechSonde.exe --desinstaller  supprime la tâche, le programme et les résultats");
        Console.WriteLine();
        Console.WriteLine("Réglages (client, adresse Kuma, seuils) : sonde.ini à côté du .exe.");
        Console.WriteLine($"Résultats : {Results.DataFolder}");
    }
}
