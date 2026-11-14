using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using PcInspector;
using PcInspector.Report;
using PcMaintenance.Maintenance;

namespace PcMaintenance;

/// <summary>
/// Mode ligne de commande, sans fenêtre, pour un script PowerShell ou un outil de gestion à distance (RMM).
///
///   PcMaintenance.exe --silent --action=clean,winget --report="C:\Logs\maintenance.html"
///   PcMaintenance.exe --dry-run --action=all
///
/// Codes de sortie : 0 = tout s'est bien passé, 1 = terminé avec des avertissements (étape en échec, redémarrage
/// nécessaire), 2 = erreur bloquante (argument inconnu, pas de droits administrateur, plantage).
/// PowerShell : <c>$p = Start-Process .\PcMaintenance.exe -ArgumentList '--silent','--action=clean' -Wait -PassThru; $p.ExitCode</c>
/// </summary>
internal static class CommandLine
{
    public const int Success = 0, Warnings = 1, Critical = 2;

    private static readonly string[] Known = { "restore", "adwcleaner", "clean", "repair", "windowsupdate", "winget", "pack" };
    private static readonly string[] All = { "restore", "adwcleaner", "clean", "windowsupdate", "winget" };

    private const int WingetNothingToUpgrade = unchecked((int)0x8A150014);
    private const int WingetAlreadyInstalled = unchecked((int)0x8A15002B);

    /// <summary>Vrai si les arguments demandent le mode sans fenêtre.</summary>
    public static bool Wanted(string[] args) => args.Any(a => Is(a, "silent") || Is(a, "dry-run") || Is(a, "help") || a == "/?");

    private sealed class Options
    {
        public bool DryRun;
        public bool Help;
        public string? Report;
        public string? Client;
        public List<string> Actions = new();
        public string? Error;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        CrashGuard.Silent = true;
        AttachConsole(AttachParentProcess);
        var options = Parse(args);
        if (options.Help)
        {
            Write(Usage);
            return Success;
        }
        if (options.Error is not null)
        {
            Write("ERREUR : " + options.Error);
            Write(Usage);
            return Critical;
        }
        if (!IsAdministrator())
        {
            Write("ERREUR : PC Maintenance doit être lancé en administrateur (ou en tant que SYSTEM depuis un RMM).");
            return Critical;
        }

        var report = new MaintenanceReport();
        var warnings = 0;
        var reboot = false;
        void Log(string line)
        {
            Write(line);
            CrashGuard.Trace(line);
        }
        var log = new Progress<string>(Log);

        try
        {
            Log($"===== PC Maintenance {AppInfo.Version} en ligne de commande{(options.DryRun ? " (SIMULATION, rien n'est modifié)" : "")} : {string.Join(", ", options.Actions)} =====");
            report.Begin();
            await Snapshot(report, before: true, log: Log);

            foreach (var action in options.Actions)
            {
                Log($"--- {Title(action)} ---");
                if (options.DryRun)
                {
                    report.Step(Title(action), "Simulation : " + Plan(action));
                    Log("Simulation : " + Plan(action));
                    continue;
                }
                try
                {
                    var (result, ok, needsReboot) = await Do(action, report, log);
                    report.Step(Title(action), result);
                    Log($"{Title(action)} : {result}");
                    if (!ok) warnings++;
                    reboot |= needsReboot;
                }
                catch (Exception ex)
                {
                    warnings++;
                    CrashGuard.Write(ex, fatal: false);
                    report.Step(Title(action), "Non fait : erreur imprévue (" + ex.Message + ")");
                    Log($"{Title(action)} : erreur imprévue ({ex.Message}), étape suivante.");
                }
            }

            report.Finish();
            await Snapshot(report, before: false, log: Log);
            reboot |= WindowsUpdate.IsRebootPending();
            if (reboot)
            {
                Log("Redémarrage nécessaire pour finir les mises à jour (non lancé automatiquement).");
                report.Step("Redémarrage", "Nécessaire pour finir les mises à jour");
            }

            if (options.Report is not null)
            {
                var info = ReportInfo.LoadDefaults();
                if (options.Client is not null) info.Client = options.Client;
                info.Date = DateTime.Now;
                var path = Path.GetFullPath(options.Report);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                ReportWriter.Write(path, info, report.ToSections(options.DryRun ? "Maintenance du PC (simulation)" : "Maintenance du PC"));
                Log("Rapport : " + path);
            }
        }
        catch (Exception ex)
        {
            CrashGuard.Write(ex, fatal: true);
            Log("ERREUR BLOQUANTE : " + ex.Message);
            return Critical;
        }

        var code = warnings > 0 || reboot ? Warnings : Success;
        Log($"===== Fin, code de sortie {code} ({(code == Success ? "succès" : "succès avec avertissements")}) =====");
        return code;
    }

    // ---------- Étapes ----------

    private static async Task<(string Result, bool Ok, bool Reboot)> Do(string action, MaintenanceReport report, IProgress<string> log)
    {
        switch (action)
        {
            case "restore":
            {
                var result = await Task.Run(() => RestorePoint.Create(log));
                return (result, result.StartsWith("Point de restauration créé") || result.StartsWith("Windows n'en crée pas"), false);
            }
            case "adwcleaner":
            {
                var result = await AdwCleaner.RunAsync(log);
                return (result.Summary, !result.Summary.StartsWith("Non", StringComparison.OrdinalIgnoreCase), false);
            }
            case "clean":
            {
                var result = await Task.Run(() => Cleanup.Run(log));
                report.Detail("Nettoyage", result.Done);
                report.Detail("Raccourcis cassés mis de côté", result.MovedShortcuts);
                report.Detail("Programmes lancés au démarrage (à désactiver s'ils sont inutiles)", result.Startup);
                return ($"{Cleanup.Size(result.Freed)} libérés", true, false);
            }
            case "repair":
            {
                var result = await Task.Run(() => Repair.Run(log));
                report.Detail("Réparation de Windows", result.Details);
                return (result.Summary, result.Ok, result.RebootRequired);
            }
            case "windowsupdate":
                return await Task.Run(() => InstallUpdates(report, log));
            case "winget":
            {
                var before = await Winget.ListAsync();
                var code = await Winget.UpgradeAllAsync(log);
                if (code is 0 && before.Updates is { } list)
                    report.Detail("Logiciels mis à jour", list.Select(u => $"{u.Name} {u.Installed} → {u.Available}"));
                return code switch
                {
                    null => ("winget n'est pas installé sur ce PC.", false, false),
                    0 => ("Terminé.", true, false),
                    WingetNothingToUpgrade => ("Rien à mettre à jour.", true, false),
                    var other => ($"Terminé avec des erreurs (code 0x{other:X8}).", false, false),
                };
            }
            case "pack":
            {
                var done = new List<string>();
                var failed = 0;
                foreach (var item in SoftwarePack.Load())
                {
                    var code = await Winget.InstallAsync(item.Id, log);
                    if (code is null) return ("winget n'est pas installé sur ce PC.", false, false);
                    if (code == 0) done.Add(item.Name + " : installé");
                    else if (code == WingetAlreadyInstalled) done.Add(item.Name + " : déjà installé");
                    else { failed++; done.Add($"{item.Name} : échec (code 0x{code:X8})"); }
                }
                report.Detail("Logiciels du pack", done);
                return ($"{done.Count - failed} sur {done.Count} installé(s) ou déjà présent(s)", failed == 0, false);
            }
            default:
                return ("Action inconnue", false, false);
        }
    }

    private static (string, bool, bool) InstallUpdates(MaintenanceReport report, IProgress<string> log)
    {
        try
        {
            var agent = WindowsUpdate.Create();
            var updates = agent.Search();
            if (updates.Count == 0) return ("Windows est à jour.", true, false);
            int installed = 0, failed = 0;
            var reboot = false;
            var titles = new List<string>();
            foreach (var update in updates)
            {
                log.Report(update.Title);
                try
                {
                    if (agent.Install(update, out var needsReboot)) { installed++; titles.Add(update.Title); reboot |= needsReboot; }
                    else { failed++; log.Report("    échec de l'installation"); }
                }
                catch (Exception ex) when (WindowsUpdate.IsExpected(ex))
                {
                    failed++;
                    log.Report("    échec : " + WindowsUpdate.Describe(ex));
                }
            }
            report.Detail("Mises à jour Windows installées", titles);
            return ($"{installed} installée(s)" + (failed > 0 ? $", {failed} en échec" : ""), failed == 0, reboot);
        }
        catch (Exception ex) when (WindowsUpdate.IsExpected(ex))
        {
            return ("Impossible d'utiliser Windows Update : " + WindowsUpdate.Describe(ex), false, false);
        }
    }

    /// <summary>État du PC avant et après, pour le tableau « Avant / Après » du rapport.</summary>
    private static async Task Snapshot(MaintenanceReport report, bool before, Action<string> log)
    {
        var wu = await Task.Run(() =>
        {
            try { return WindowsUpdate.Create().Search().Count.ToString(); }
            catch (Exception ex) when (WindowsUpdate.IsExpected(ex)) { return "?"; }
        });
        var winget = await Winget.ListAsync();
        var apps = winget.Updates is { } list ? list.Count.ToString() : "?";
        var reboot = WindowsUpdate.IsRebootPending() ? "oui" : "non";
        report.Compare("Mises à jour Windows en attente", wu, before);
        report.Compare("Logiciels à mettre à jour (winget)", apps, before);
        report.Compare("Redémarrage en attente", reboot, before);
        log($"{(before ? "Avant" : "Après")} : {wu} mise(s) à jour Windows, {apps} logiciel(s) à mettre à jour, redémarrage en attente : {reboot}.");
    }

    private static string Title(string action) => action switch
    {
        "restore" => "Point de restauration",
        "adwcleaner" => "Suppression des adwares (AdwCleaner)",
        "clean" => "Nettoyage du PC",
        "repair" => "Réparer Windows (DISM + SFC)",
        "windowsupdate" => "Mises à jour Windows",
        "winget" => "Mise à jour des logiciels (winget)",
        "pack" => "Pack logiciels nouveau PC",
        _ => action,
    };

    private static string Plan(string action) => action switch
    {
        "restore" => "créerait un point de restauration",
        "adwcleaner" => "téléchargerait AdwCleaner et supprimerait les adwares trouvés",
        "clean" => "viderait temporaires, cache Windows Update et caches des navigateurs (favoris, mots de passe, historique et cookies gardés)",
        "repair" => "lancerait DISM /RestoreHealth puis SFC /scannow (20 à 45 minutes)",
        "windowsupdate" => "installerait les mises à jour Windows en attente",
        "winget" => "mettrait à jour les logiciels connus de winget",
        "pack" => "installerait les logiciels du pack (" + string.Join(", ", SoftwarePack.Load().Select(i => i.Name)) + ")",
        _ => "",
    };

    // ---------- Arguments ----------

    private static Options Parse(string[] args)
    {
        var options = new Options();
        foreach (var raw in args)
        {
            var (name, value) = Split(raw);
            switch (name)
            {
                case "silent": break;
                case "dry-run": options.DryRun = true; break;
                case "help" or "?": options.Help = true; break;
                case "report": options.Report = value; break;
                case "client": options.Client = value; break;
                case "action":
                    foreach (var a in (value ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(a => a.Trim().ToLowerInvariant()))
                    {
                        if (a == "all") options.Actions.AddRange(All);
                        else if (a == "cleanall") options.Actions.Add("clean");
                        else if (Known.Contains(a)) options.Actions.Add(a);
                        else options.Error ??= $"action inconnue « {a} »";
                    }
                    break;
                default: options.Error ??= $"argument inconnu « {raw} »"; break;
            }
        }
        if (options.Report is { Length: 0 }) options.Error ??= "--report demande un chemin de fichier (.html, .txt ou .pdf)";
        if (options.Actions.Count == 0) options.Actions.Add("clean");
        // Toujours dans l'ordre de la fenêtre : le point de restauration d'abord, une seule fois chacune.
        options.Actions = Known.Where(options.Actions.Contains).ToList();
        return options;
    }

    private static (string Name, string? Value) Split(string raw)
    {
        var text = raw.TrimStart('-', '/');
        var cut = text.IndexOfAny(new[] { '=', ':' });
        return cut < 0 ? (text.ToLowerInvariant(), null) : (text.Substring(0, cut).ToLowerInvariant(), text.Substring(cut + 1).Trim('"'));
    }

    private static bool Is(string raw, string name) => Split(raw).Name == name;

    private const string Usage = @"
PC Maintenance en ligne de commande
  --silent                 sans fenêtre (obligatoire pour un script ou un RMM)
  --action=a,b,...         restore, adwcleaner, clean (ou cleanall), repair, windowsupdate, winget, pack, all
                           (par défaut : clean ; all = tout sauf repair et pack)
  --dry-run                simulation : montre ce qui serait fait, ne modifie rien
  --report=chemin          rapport avant / après en .html, .txt ou .pdf
  --client=""Nom""           nom du client dans le rapport
Codes de sortie : 0 succès, 1 succès avec avertissements, 2 erreur bloquante.
Exemple PowerShell :
  $p = Start-Process .\PcMaintenance.exe -ArgumentList '--silent','--action=clean,winget','--report=C:\Logs\m.html' -Wait -PassThru
  $p.ExitCode";

    // ---------- Console et droits ----------

    private static void Write(string line)
    {
        try { Console.WriteLine(line); }
        catch (IOException) { /* pas de console : le journal suffit */ }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) || identity.IsSystem;
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
