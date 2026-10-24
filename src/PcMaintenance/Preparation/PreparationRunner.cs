using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using PcInspector;
using PcInspector.Report;
using PcMaintenance.Maintenance;

namespace PcMaintenance.Preparation;

/// <summary>
/// Préparation d'un PC d'achat-revente après une installation propre de Windows, en mode audit :
/// applications inutiles retirées, outil du constructeur (pilotes, BIOS), Windows Update en boucle avec les pilotes,
/// contrôle final et rapport, puis remise au premier démarrage pour l'acheteur. Chaque étape est notée dans etat.ini
/// et la préparation reprend toute seule après chaque redémarrage.
/// </summary>
internal sealed class PreparationRunner
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "JVTECH-Preparation";
    private const string StorePolicyKey = @"SOFTWARE\Policies\Microsoft\WindowsStore";
    private const string DeliveryPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";

    private readonly IProgress<string> _log;
    private readonly IProgress<PreparationStep> _step;
    private readonly IProgress<string> _status;
    private readonly PreparationOptions _options = PreparationOptions.Load();
    private readonly PreparationState _state = PreparationState.Load();
    private readonly ServerLink _server = ServerLink.Load();

    public volatile bool StopRequested;

    public PreparationRunner(IProgress<string> log, IProgress<PreparationStep> step, IProgress<string> status)
    {
        _log = new Progress<string>(text =>
        {
            log.Report(text);
            try { File.AppendAllText(PreparationState.LogPath, $"[{DateTime.Now:dd/MM HH:mm:ss}] {text}{Environment.NewLine}"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        });
        _step = step;
        _status = new Progress<string>(text =>
        {
            status.Report(text);
            _server.Status(Model(), Serial(), _state.Started, StepName(_state.Step), text);
        });
    }

    private static string StepName(PreparationStep step) => step switch
    {
        PreparationStep.Start => "Mise en route",
        PreparationStep.Debloat => "Applications inutiles",
        PreparationStep.Manufacturer => "Pilotes et BIOS du constructeur",
        PreparationStep.WindowsUpdate => "Windows Update et pilotes",
        PreparationStep.Finish => "Contrôle final et rapport",
        _ => "Terminé",
    };

    public PreparationStep Current => _state.Step;
    public DateTime Started => _state.Started;

    /// <summary>Ce qu'il reste à faire après la fin : rien, redémarrer, ou remettre le PC au premier démarrage.</summary>
    public enum Outcome { Stopped, Reboot, Reseal, Finished }

    public async Task<Outcome> RunAsync()
    {
        while (!StopRequested)
        {
            _step.Report(_state.Step);
            switch (_state.Step)
            {
                case PreparationStep.Start:
                    await Task.Run(Begin);
                    Next(PreparationStep.Debloat);
                    break;

                case PreparationStep.Debloat:
                    if (_options.Debloat) await Task.Run(RunDebloat);
                    else _state.Result("Applications inutiles", "Étape désactivée dans options.ini.");
                    if (_options.Tweaks) await Task.Run(RunTweaks);
                    Next(PreparationStep.Manufacturer);
                    break;

                case PreparationStep.Manufacturer:
                    var reboot = false;
                    if (_options.Manufacturer)
                    {
                        _status.Report("Pilotes et BIOS du constructeur…");
                        var result = await Manufacturer.RunAsync(_options.Bios, _log);
                        _state.Result("Outil du constructeur", result.Summary);
                        reboot = result.RebootRequired;
                    }
                    else _state.Result("Outil du constructeur", "Étape désactivée dans options.ini.");
                    Next(PreparationStep.WindowsUpdate);
                    if (reboot) return Reboot("Redémarrage pour finir les pilotes et le BIOS du constructeur.");
                    break;

                case PreparationStep.WindowsUpdate:
                    if (await Task.Run(UpdateRound) is { } why) return Reboot(why);
                    Next(PreparationStep.Finish);
                    break;

                case PreparationStep.Finish:
                    await FinishAsync();
                    Next(PreparationStep.Done);
                    return _options.Reseal ? Outcome.Reseal : Outcome.Finished;

                default:
                    return Outcome.Finished;
            }
        }
        _log.Report("Préparation arrêtée à la demande. Elle reprendra à cette étape au prochain lancement de « PcMaintenance.exe /preparation ».");
        RemoveAutoStart();
        return Outcome.Stopped;
    }

    private void Next(PreparationStep step)
    {
        _state.Step = step;
        _state.Save();
    }

    // ---------- Étapes ----------

    private void Begin()
    {
        _status.Report("Mise en route…");
        _state.Started = DateTime.Now;
        _log.Report($"Préparation de {Model()} (n° de série {Serial()}).");

        // La fenêtre de Sysprep s'ouvre d'elle-même en mode audit : elle servira à la fin, pas maintenant.
        foreach (var process in Process.GetProcessesByName("sysprep"))
        {
            try { process.Kill(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }

        SetAutoStart();
        // Pas de veille pendant la préparation ; les réglages d'origine sont remis à la fin.
        foreach (var setting in new[] { "standby-timeout-ac 0", "monitor-timeout-ac 0", "hibernate-timeout-ac 0" })
            Run("powercfg.exe", "/change " + setting, TimeSpan.FromSeconds(30));
        // Le Store met à jour ses applications pour le compte en cours, ce qui empêcherait Sysprep de finir.
        using (var key = Registry.LocalMachine.CreateSubKey(StorePolicyKey)) key?.SetValue("AutoDownload", 2, RegistryValueKind.DWord);
        // Les PC préparés en même temps s'échangent les mises à jour sur le réseau de l'atelier : téléchargées une seule fois.
        using (var key = Registry.LocalMachine.CreateSubKey(DeliveryPolicyKey)) key?.SetValue("DODownloadMode", 1, RegistryValueKind.DWord);
        // winget fait partie de « Programme d'installation d'application », pas encore enregistré pour le compte du mode audit.
        PowerShell.Run("Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe -ErrorAction SilentlyContinue", TimeSpan.FromMinutes(3));
        WaitForNetwork();
        _state.Result("Ordinateur", $"{Model()}, n° de série {Serial()}");
    }

    private void RunDebloat()
    {
        _status.Report("Retrait des applications inutiles…");
        var (removed, errors) = Debloat.Run(_log);
        _state.Result("Applications inutiles", removed.Count == 0 ? "Aucune trouvée." : $"{removed.Count} retirée(s)" + (errors.Count > 0 ? $", {errors.Count} en échec." : "."));
        _state.Detail("Applications retirées", removed);
        _state.Detail("Applications non retirées", errors);
    }

    private void RunTweaks()
    {
        _status.Report("Publicités et suggestions de Windows…");
        var done = Tweaks.Run(_log);
        _state.Result("Publicités et suggestions", done.Count == 2 ? "Coupées pour l'acheteur (menu Démarrer, recherche, widgets)." : "Coupées en partie : voir le journal.");
        _state.Detail("Réglages pour l'acheteur", done);
    }

    /// <summary>Un passage de Windows Update. Renvoie la raison du redémarrage, ou null quand il n'y a plus rien à faire.</summary>
    private string? UpdateRound()
    {
        const string StepName = "Windows Update et pilotes";
        if (_state.Round >= _options.MaxRounds)
        {
            _state.Result(StepName, $"Arrêté après {_state.Round} passages : vérifier Windows Update à la main.");
            return null;
        }
        _state.Round++;
        _state.Save();
        _status.Report($"Windows Update, passage {_state.Round}…");
        _log.Report($"--- Windows Update, passage {_state.Round} ---");
        try
        {
            var agent = WindowsUpdate.Create();
            var updates = agent.Search(withDrivers: true);
            _log.Report(updates.Count == 0 ? "Plus aucune mise à jour proposée." : $"{updates.Count} mise(s) à jour trouvée(s).");
            if (updates.Count == 0)
            {
                if (WindowsUpdate.IsRebootPending()) return "Redémarrage pour finir les dernières mises à jour.";
                _state.Result(StepName, $"À jour après {_state.Round} passage(s).");
                return null;
            }

            int installed = 0, failed = 0;
            for (var i = 0; i < updates.Count && !StopRequested; i++)
            {
                _status.Report($"Windows Update, passage {_state.Round} : {i + 1}/{updates.Count}");
                _log.Report($"({i + 1}/{updates.Count}) {updates[i].Title}");
                try
                {
                    if (agent.Install(updates[i], out _)) { installed++; _state.Detail("Mises à jour et pilotes installés", new[] { updates[i].Title }); }
                    else { failed++; _log.Report("    échec de l'installation"); }
                }
                catch (Exception ex) when (WindowsUpdate.IsExpected(ex))
                {
                    failed++;
                    _log.Report("    échec : " + WindowsUpdate.Describe(ex));
                }
            }
            _state.Result(StepName, $"Passage {_state.Round} : {installed} installée(s)" + (failed > 0 ? $", {failed} en échec." : "."));
            _state.Save();
            if (StopRequested) return null;
            // Toujours redémarrer après un passage : les pilotes et la mise à jour cumulative en ont besoin, et d'autres arrivent après.
            return $"Redémarrage après le passage {_state.Round} de Windows Update.";
        }
        catch (Exception ex) when (WindowsUpdate.IsExpected(ex))
        {
            _log.Report("Windows Update indisponible : " + WindowsUpdate.Describe(ex));
            _state.Result(StepName, "Impossible d'utiliser Windows Update : " + WindowsUpdate.Describe(ex));
            return null;
        }
    }

    private async Task FinishAsync()
    {
        _status.Report("Nettoyage des fichiers d'installation…");
        _log.Report("Nettoyage des anciennes versions des composants Windows (plusieurs minutes)…");
        await Task.Run(() => Run("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup", TimeSpan.FromMinutes(45)));
        _state.Result("Nettoyage", "Anciennes versions des composants Windows supprimées.");

        _status.Report("Contrôle final…");
        var missing = await Task.Run(MissingDrivers);
        _state.Result("Pilotes", missing.Count == 0 ? "Aucun périphérique en erreur ou sans pilote." : $"{missing.Count} périphérique(s) sans pilote ou en erreur : à vérifier.");
        _state.Detail("Périphériques à vérifier", missing);
        foreach (var device in missing) _log.Report("À vérifier : " + device);

        var activation = await Activation.ReadAsync();
        _state.Result("Activation de Windows", activation.Text.Replace("\n", " "));
        _log.Report("Activation : " + activation.Text.Replace("\n", " "));

        Restore();
        WriteReport();
        RemoveAutoStart();
        _server.Status(Model(), Serial(), _state.Started, "Prêt pour la revente",
            _options.Reseal ? "Remise au premier démarrage : le PC s'éteint." : "Préparation terminée.", finished: true, toCheck: missing.Count > 0);
    }

    /// <summary>Remet ce qui avait été changé pour la préparation.</summary>
    private void Restore()
    {
        Run("powercfg.exe", "/restoredefaultschemes", TimeSpan.FromSeconds(30));
        RemovePolicy(StorePolicyKey, "AutoDownload");
        RemovePolicy(DeliveryPolicyKey, "DODownloadMode");
    }

    private static void RemovePolicy(string path, string value)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path, writable: true);
            key?.DeleteValue(value, throwOnMissingValue: false);
            if (key is not null && key.ValueCount == 0 && key.SubKeyCount == 0) Registry.LocalMachine.DeleteSubKey(path, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
    }

    // ---------- Rapport ----------

    private void WriteReport()
    {
        var report = new MaintenanceReport();
        report.Begin(_state.Started);
        foreach (var (step, result) in _state.Results) report.Step(step, result);
        foreach (var group in _state.Details.GroupBy(d => d.Title)) report.Detail(group.Key, group.Select(d => d.Line));
        report.Finish();

        var info = ReportInfo.LoadDefaults();
        info.Client = "Préparation revente";
        info.JobNumber = Serial();
        info.Notes = $"{Model()} préparé automatiquement : Windows réinstallé, mis à jour, pilotes et BIOS du constructeur.";
        info.Date = DateTime.Now;

        var name = Safe($"{DateTime.Now:yyyy-MM-dd HH-mm} {Model()} {Serial()}") + ".html";
        foreach (var folder in ReportFolders())
        {
            try
            {
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, name);
                ReportWriter.Write(path, info, report.ToSections("Préparation du PC"));
                _log.Report("Rapport enregistré : " + path);
                // Reçu par le serveur de l'atelier : inutile d'en laisser une copie sur le PC vendu.
                if (_server.Report(Serial(), path) && path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), StringComparison.OrdinalIgnoreCase))
                {
                    _log.Report("Rapport envoyé au serveur de l'atelier.");
                    try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Report($"Impossible d'enregistrer le rapport dans {folder} : {ex.Message}");
            }
        }
    }

    /// <summary>
    /// D'abord la clé d'installation (celle qui porte autounattend.xml ou un dossier JVTECH), pour ne rien laisser sur
    /// le PC vendu ; sinon dans ProgramData.
    /// </summary>
    private static IEnumerable<string> ReportFolders()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            bool usable;
            try
            {
                usable = drive.IsReady && drive.DriveType == DriveType.Removable
                         && (File.Exists(Path.Combine(drive.RootDirectory.FullName, "autounattend.xml")) || Directory.Exists(Path.Combine(drive.RootDirectory.FullName, "JVTECH")));
            }
            catch (IOException) { usable = false; }
            if (usable) yield return Path.Combine(drive.RootDirectory.FullName, @"JVTECH\Rapports\Preparation");
        }
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"JVTECH\Preparation");
    }

    private static string Safe(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(text.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
    }

    // ---------- Fin : redémarrage ou premier démarrage ----------

    private Outcome Reboot(string why)
    {
        _state.Save();
        _log.Report(why);
        _status.Report(why);
        Run("shutdown.exe", "/r /t 15 /c \"Préparation JVTECH : " + why.Replace("\"", "'") + "\"", TimeSpan.FromSeconds(10));
        return Outcome.Reboot;
    }

    /// <summary>
    /// Supprime le dossier de préparation et lance Sysprep : le PC s'éteint et l'acheteur découvrira l'écran
    /// de premier démarrage de Windows (langue, réseau, son propre compte). Les fichiers de réponse gardés par
    /// Windows sont retirés d'abord, sinon il retournerait en mode audit.
    /// </summary>
    public void Reseal()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var answer in new[] { @"Panther\unattend.xml", @"Panther\Unattend\unattend.xml", @"System32\Sysprep\unattend.xml" })
        {
            try { File.Delete(Path.Combine(windows, answer)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        // Le dossier de préparation n'est supprimé que s'il est sur le disque du PC, jamais sur une clé.
        var folder = AppPaths.ExeFolder;
        var onSystemDrive = string.Equals(Path.GetPathRoot(folder), Path.GetPathRoot(windows), StringComparison.OrdinalIgnoreCase);
        var script = Path.Combine(windows, @"Temp\jvtech-fin.cmd");
        File.WriteAllText(script, string.Join("\r\n",
            "@echo off",
            "ping -n 6 127.0.0.1 >nul",
            onSystemDrive ? $"rd /s /q \"{folder}\"" : "rem dossier de preparation hors du disque systeme : conserve",
            "\"%WINDIR%\\System32\\Sysprep\\sysprep.exe\" /oobe /shutdown /quiet",
            "del \"%~f0\""), Encoding.ASCII);
        _log.Report("Remise au premier démarrage (Sysprep) : le PC va s'éteindre.");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { UseShellExecute = false, CreateNoWindow = true });
    }

    // ---------- Outils ----------

    private void SetAutoStart()
    {
        using var key = Registry.LocalMachine.CreateSubKey(RunKey);
        key?.SetValue(RunValue, $"\"{AppInfo.Entry.Location}\" /preparation");
    }

    private static void RemoveAutoStart()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    private void WaitForNetwork()
    {
        for (var i = 0; i < 60; i++)
        {
            if (NetworkInterface.GetIsNetworkAvailable() && CanReachInternet()) return;
            if (i == 0) _log.Report("En attente du réseau (brancher le câble)…");
            _status.Report("En attente du réseau…");
            Thread.Sleep(TimeSpan.FromSeconds(5));
        }
        _log.Report("Pas d'accès à Internet après 5 minutes : la suite risque d'échouer.");
    }

    private static bool CanReachInternet()
    {
        try
        {
            using var ping = new Ping();
            return ping.Send("www.msftconnecttest.com", 3000)?.Status == IPStatus.Success
                   || System.Net.Dns.GetHostAddresses("www.microsoft.com").Length > 0;
        }
        catch (Exception ex) when (ex is PingException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            return false;
        }
    }

    private void Run(string exe, string arguments, TimeSpan timeout)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true })!;
            if (!process.WaitForExit((int)timeout.TotalMilliseconds)) _log.Report($"{exe} ne répond plus : on continue.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Report($"{exe} : {ex.Message}");
        }
    }

    private static List<string> MissingDrivers()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
            return searcher.Get().Cast<ManagementObject>()
                .Select(d => $"{d["Name"] ?? "Périphérique inconnu"} ({d["DeviceID"]}, code {d["ConfigManagerErrorCode"]})")
                .ToList();
        }
        catch (ManagementException)
        {
            return new List<string>();
        }
    }

    private static string? _model, _serial;

    private static string Model() => _model ??= Wmi("SELECT Manufacturer, Model FROM Win32_ComputerSystem", o => $"{o["Manufacturer"]} {o["Model"]}") ?? "PC";
    private static string Serial() => _serial ??= Wmi("SELECT SerialNumber FROM Win32_BIOS", o => o["SerialNumber"]?.ToString()) ?? "sans n° de série";

    private static string? Wmi(string query, Func<ManagementObject, string?> read)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            var value = searcher.Get().Cast<ManagementObject>().Select(read).FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (ManagementException)
        {
            return null;
        }
    }
}
