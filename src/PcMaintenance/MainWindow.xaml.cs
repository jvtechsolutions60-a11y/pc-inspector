using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PcInspector;
using PcInspector.Report;
using PcMaintenance.Maintenance;

namespace PcMaintenance;

public partial class MainWindow : Window
{
    // « Aucun package installé ne correspond » : rien à mettre à jour.
    private const int WingetNothingToUpgrade = unchecked((int)0x8A150014);

    private bool _running;
    private volatile bool _stopRequested;
    private volatile bool _rebootRequired;
    private readonly MaintenanceReport _report = new();
    private readonly List<string> _installedUpdates = new();
    private IReadOnlyList<WingetUpdate> _wingetUpdates = Array.Empty<WingetUpdate>();
    private string _updateUrl = Updater.ReleasesPage;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ComputerText.Text = Environment.MachineName;
        VersionText.Text = AppVersion();
        Title = "PC Maintenance " + VersionText.Text;
        PortableText.Visibility = AppPaths.IsPortable ? Visibility.Visible : Visibility.Collapsed;
        LoadPack();
        _ = AnalyzeAsync();
        _ = CheckUpdateAsync();
    }

    /// <summary>« v1.0.12 · 20/10/2026 » : numéro de compilation GitHub Actions et date du .exe.</summary>
    private static string AppVersion()
    {
        var assembly = typeof(MainWindow).Assembly;
        var version = assembly.GetName().Version;
        var built = System.IO.File.GetLastWriteTime(assembly.Location);
        return $"v{version.Major}.{version.Minor}.{version.Build} · {built:dd/MM/yyyy}";
    }

    private async Task CheckUpdateAsync()
    {
        if (await Updater.CheckAsync() is not { } update) return;
        _updateUrl = update.Url;
        UpdateText.Text = $"Nouvelle version {update.Tag} : télécharger";
    }

    private void UpdateLink_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_updateUrl) { UseShellExecute = true });

    /// <summary>Remplit (ou rafraîchit) les cases du résumé ; le bouton reste grisé tant que tout n'est pas relu.</summary>
    private async Task AnalyzeAsync()
    {
        RefreshButton.IsEnabled = false;
        try
        {
            ShowWindowsVersion();
            ShowFreeSpace();
            await Task.WhenAll(ShowActivationAsync(), RefreshWindowsUpdateAsync(), RefreshWingetAsync());
        }
        finally
        {
            RefreshButton.IsEnabled = !_running;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        Log("Analyse relancée.");
        LoadPack();
        await AnalyzeAsync();
    }

    // ---------- Résumé rapide ----------

    private void ShowWindowsVersion()
    {
        var version = WindowsVersion.Read();
        SetCard(Win11Value, version.IsWindows11 ? "Oui" : "Non", version.IsWindows11 ? "OkBrush" : "WarningBrush");
        Win11Detail.Text = $"{version.Name} {version.DisplayVersion} (build {version.Build})".Replace("  ", " ")
            + (version.IsWindows11 ? "" : "\nCompatibilité : voir PC Inspector, onglet Windows 11.");
    }

    private async Task ShowActivationAsync()
    {
        SetCard(ActivationValue, "…", null);
        ActivationDetail.Text = "Lecture en cours…";
        var activation = await Activation.ReadAsync();
        var (text, brush) = activation.State switch
        {
            ActivationState.Permanent => ("Permanente", "OkBrush"),
            ActivationState.Expires => ("Temporaire", "WarningBrush"),
            ActivationState.NotActivated => ("Non activé", "DangerBrush"),
            _ => ("?", "DangerBrush"),
        };
        SetCard(ActivationValue, text, brush);
        ActivationDetail.Text = activation.Text;
    }

    private void ShowFreeSpace()
    {
        try
        {
            var drive = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory)!);
            var free = (ulong)drive.AvailableFreeSpace;
            var percent = 100.0 * free / drive.TotalSize;
            SetCard(SpaceValue, Cleanup.Size(free), percent < 10 ? "DangerBrush" : percent < 20 ? "WarningBrush" : "OkBrush");
            SpaceDetail.Text = $"sur {Cleanup.Size((ulong)drive.TotalSize)} ({drive.Name.TrimEnd('\\')}, {percent:0} % libre)";
        }
        catch (Exception ex) when (ex is System.IO.IOException or ArgumentException or UnauthorizedAccessException)
        {
            SetCard(SpaceValue, "?", null);
            SpaceDetail.Text = ex.Message;
        }
    }

    private async Task RefreshWindowsUpdateAsync()
    {
        SetCard(WuValue, "…", null);
        WuDetail.Text = "Recherche en cours…";
        try
        {
            var updates = await Task.Run(() => WindowsUpdate.Create().Search());
            var reboot = WindowsUpdate.IsRebootPending();
            SetCard(WuValue, updates.Count == 0 ? "À jour" : $"{updates.Count} en attente",
                updates.Count == 0 && !reboot ? "OkBrush" : "WarningBrush");
            var titles = updates.Take(3).Select(u => "• " + u.Title).ToList();
            if (updates.Count > 3) titles.Add($"… et {updates.Count - 3} autre(s)");
            if (reboot) titles.Insert(0, "Redémarrage en attente pour finir une installation.");
            WuDetail.Text = titles.Count == 0 ? "Aucune mise à jour à installer." : string.Join("\n", titles);
            WuDetail.ToolTip = updates.Count == 0 ? null : string.Join("\n", updates.Select(u => u.Title));
        }
        catch (Exception ex) when (WindowsUpdate.IsExpected(ex))
        {
            SetCard(WuValue, "?", "DangerBrush");
            WuDetail.Text = "Recherche impossible : " + WindowsUpdate.Describe(ex);
        }
    }

    private async Task RefreshWingetAsync()
    {
        SetCard(WingetValue, "…", null);
        WingetDetail.Text = "Recherche en cours…";
        var result = await Winget.ListAsync();
        if (result.Updates is not { } updates)
        {
            SetCard(WingetValue, "?", "DangerBrush");
            WingetDetail.Text = result.Error;
            return;
        }
        _wingetUpdates = updates;
        SetCard(WingetValue, updates.Count == 0 ? "À jour" : $"{updates.Count} à mettre à jour", updates.Count == 0 ? "OkBrush" : "WarningBrush");
        var names = updates.Take(3).Select(u => $"• {u.Name} {u.Installed} → {u.Available}").ToList();
        if (updates.Count > 3) names.Add($"… et {updates.Count - 3} autre(s)");
        WingetDetail.Text = names.Count == 0 ? "Tous les logiciels connus de winget sont à jour." : string.Join("\n", names);
        WingetDetail.ToolTip = updates.Count == 0 ? null : string.Join("\n", updates.Select(u => $"{u.Name} {u.Installed} → {u.Available}"));
    }

    private void SetCard(TextBlock value, string text, string? brush)
    {
        value.Text = text;
        value.Foreground = (Brush)FindResource(brush ?? "TextBrush");
    }

    // ---------- Maintenance ----------

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        if (new[] { RestoreCheck, AdwCheck, CleanCheck, WuCheck, WingetCheck, PackCheck }.All(c => c.IsChecked != true))
        {
            MessageBox.Show(this, "Cocher au moins une étape.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetRunning(true);
        _report.Begin();
        Log("===== Début de la maintenance =====");
        try
        {
            if (RestoreCheck.IsChecked == true && !_stopRequested) await SafeStepAsync("Point de restauration", RunRestorePointAsync);
            if (AdwCheck.IsChecked == true && !_stopRequested) await SafeStepAsync("AdwCleaner", RunAdwCleanerAsync);
            if (CleanCheck.IsChecked == true && !_stopRequested) await SafeStepAsync("Nettoyage du PC", RunCleanupAsync);
            if (WuCheck.IsChecked == true && !_stopRequested) await SafeStepAsync("Mises à jour Windows", RunWindowsUpdateAsync);
            if (WingetCheck.IsChecked == true && !_stopRequested) await SafeStepAsync("Mise à jour des logiciels", RunWingetAsync);
            if (PackCheck.IsChecked == true && !_stopRequested) await SafeStepAsync("Installation des logiciels", RunPackAsync);
            Log(_stopRequested ? "===== Maintenance arrêtée =====" : "===== Maintenance terminée =====");
        }
        finally
        {
            _report.Finish();
            SetRunning(false);
        }

        _ = AnalyzeAsync();

        if (_rebootRequired || WindowsUpdate.IsRebootPending())
        {
            var answer = MessageBox.Show(this, "Un redémarrage est nécessaire pour finir les mises à jour.\n\nRedémarrer le PC maintenant ?",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { UseShellExecute = false, CreateNoWindow = true });
        }
    }

    /// <summary>Une étape qui plante est notée dans le journal et le rapport, puis la maintenance passe à la suivante.</summary>
    private async Task SafeStepAsync(string title, Func<Task> step)
    {
        try { await step(); }
        catch (Exception ex)
        {
            var file = CrashGuard.Write(ex, fatal: false);
            Log($"{title} : erreur imprévue ({ex.Message}), étape suivante.");
            if (file is not null) Log("    Détail enregistré dans " + file);
            _report.Step(title, "Non fait : erreur imprévue (" + ex.Message + ")");
        }
    }

    private async Task RunCleanupAsync()
    {
        CleanStatus.Text = "En cours (voir le journal)…";
        Log("--- Nettoyage du PC ---");
        var result = await Task.Run(() => Cleanup.Run(new Progress<string>(Log)));
        CleanStatus.Text = $"Terminé : {Cleanup.Size(result.Freed)} libérés.";
        Log($"Nettoyage : {Cleanup.Size(result.Freed)} libérés.");
        _report.Step("Nettoyage du PC", $"{Cleanup.Size(result.Freed)} libérés");
        _report.Detail("Nettoyage", result.Done);
        _report.Detail("Raccourcis cassés mis de côté", result.MovedShortcuts);
        _report.Detail("Programmes lancés au démarrage (à désactiver s'ils sont inutiles)", result.Startup);
        ShowFreeSpace();
    }

    private async Task RunWindowsUpdateAsync()
    {
        Log("--- Mises à jour Windows ---");
        var status = new Progress<string>(text => WuStatus.Text = text);
        var log = new Progress<string>(Log);
        var outcome = await Task.Run(() => InstallWindowsUpdates(status, log));
        WuStatus.Text = outcome;
        Log("Windows Update : " + outcome);
        _report.Step("Mises à jour Windows", outcome);
        _report.Detail("Mises à jour Windows installées", _installedUpdates);
    }

    /// <summary>Recherche puis installe chaque mise à jour, en s'arrêtant entre deux si on le demande.</summary>
    private string InstallWindowsUpdates(IProgress<string> status, IProgress<string> log)
    {
        try
        {
            lock (_installedUpdates) _installedUpdates.Clear();
            status.Report("Recherche des mises à jour…");
            var agent = WindowsUpdate.Create();
            var updates = agent.Search();
            log.Report(updates.Count == 0 ? "Aucune mise à jour à installer." : $"{updates.Count} mise(s) à jour trouvée(s).");
            if (updates.Count == 0) return "Windows est à jour.";

            int installed = 0, failed = 0;
            for (var i = 0; i < updates.Count; i++)
            {
                if (_stopRequested) break;
                var update = updates[i];
                status.Report($"Installation {i + 1}/{updates.Count} : {update.Title}");
                log.Report($"({i + 1}/{updates.Count}) {update.Title}");
                try
                {
                    if (agent.Install(update, out var reboot))
                    {
                        installed++;
                        lock (_installedUpdates) _installedUpdates.Add(update.Title);
                        log.Report(reboot ? "    installée, redémarrage nécessaire" : "    installée");
                        if (reboot) _rebootRequired = true;
                    }
                    else
                    {
                        failed++;
                        log.Report("    échec de l'installation");
                    }
                }
                catch (Exception ex) when (WindowsUpdate.IsExpected(ex))
                {
                    failed++;
                    log.Report("    échec : " + WindowsUpdate.Describe(ex));
                }
            }

            var summary = $"{installed} installée(s)" + (failed > 0 ? $", {failed} en échec" : "");
            if (installed + failed < updates.Count) summary += $", {updates.Count - installed - failed} non traitée(s) (arrêt demandé)";
            return summary + (_rebootRequired ? " ; redémarrage nécessaire." : ".");
        }
        catch (Exception ex) when (WindowsUpdate.IsExpected(ex))
        {
            log.Report("Erreur : " + WindowsUpdate.Describe(ex));
            return "Impossible d'utiliser Windows Update : " + WindowsUpdate.Describe(ex) + ". Essayer « Ouvrir Windows Update ».";
        }
    }

    private async Task RunWingetAsync()
    {
        Log("--- Logiciels : winget upgrade --all ---");
        WingetStatus.Text = "Mise à jour des logiciels en cours (voir le journal)…";
        var code = await Winget.UpgradeAllAsync(new Progress<string>(Log));
        WingetStatus.Text = code switch
        {
            null => "winget n'est pas installé sur ce PC.",
            0 => "Terminé.",
            WingetNothingToUpgrade => "Rien à mettre à jour.",
            var other => $"Terminé avec des erreurs (code 0x{other:X8}) : voir le journal.",
        };
        Log("winget : " + WingetStatus.Text);
        _report.Step("Mise à jour des logiciels (winget)", WingetStatus.Text);
        if (code is 0) _report.Detail("Logiciels mis à jour", _wingetUpdates.Select(u => $"{u.Name} {u.Installed} → {u.Available}"));
    }

    private void SetRunning(bool running)
    {
        _running = running;
        if (running)
        {
            _stopRequested = false;
            _rebootRequired = false;
        }
        StartButton.IsEnabled = !running;
        ReportButton.IsEnabled = !running;
        RestoreCheck.IsEnabled = !running;
        AdwCheck.IsEnabled = !running;
        PackCheck.IsEnabled = !running;
        PackList.IsEnabled = !running;
        StopButton.IsEnabled = running;
        StopButton.Content = "Arrêter après l'étape";
        CleanCheck.IsEnabled = !running;
        RefreshButton.IsEnabled = !running;
        WuCheck.IsEnabled = !running;
        WingetCheck.IsEnabled = !running;
    }

    // ---------- Nouvelles étapes ----------

    private async Task RunRestorePointAsync()
    {
        Log("--- Point de restauration ---");
        RestoreStatus.Text = "Création en cours…";
        var result = await Task.Run(() => RestorePoint.Create(new Progress<string>(Log)));
        RestoreStatus.Text = result;
        Log("Point de restauration : " + result);
        _report.Step("Point de restauration", result);
    }

    private async Task RunAdwCleanerAsync()
    {
        Log("--- AdwCleaner ---");
        AdwStatus.Text = "En cours (voir le journal)…";
        var result = await AdwCleaner.RunAsync(new Progress<string>(Log));
        AdwStatus.Text = result.Summary;
        Log("AdwCleaner : " + result.Summary + (result.LogPath is null ? "" : " Journal : " + result.LogPath));
        _report.Step("Suppression des adwares (AdwCleaner)", result.Summary);
    }

    private void LoadPack()
    {
        PackList.Children.Clear();
        foreach (var item in SoftwarePack.Load())
            PackList.Children.Add(new CheckBox { Content = item.Name, Tag = item, IsChecked = item.Checked, Margin = new Thickness(0, 2, 14, 2) });
    }

    private void EditPack_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = SoftwarePack.EnsureFile();
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true });
            Log($"Liste du pack ouverte : {file}. Relancer l'analyse après l'avoir enregistrée pour la recharger.");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log("Impossible d'ouvrir la liste du pack : " + ex.Message);
        }
    }

    // « Déjà installé » : winget n'a rien à faire.
    private const int WingetAlreadyInstalled = unchecked((int)0x8A15002B);

    private async Task RunPackAsync()
    {
        var items = PackList.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (PackItem)c.Tag).ToList();
        Log("--- Pack logiciels nouveau PC ---");
        if (items.Count == 0)
        {
            PackStatus.Text = "Aucun logiciel coché.";
            return;
        }

        var done = new List<string>();
        int installed = 0, already = 0, failed = 0;
        foreach (var item in items)
        {
            if (_stopRequested) break;
            PackStatus.Text = $"Installation de {item.Name}…";
            Log($"{item.Name} : winget install -e --id {item.Id}");
            var code = await Winget.InstallAsync(item.Id, new Progress<string>(Log));
            if (code is null) { PackStatus.Text = "winget n'est pas installé sur ce PC."; Log(PackStatus.Text); break; }
            if (code == 0) { installed++; done.Add(item.Name + " : installé"); }
            else if (code == WingetAlreadyInstalled) { already++; done.Add(item.Name + " : déjà installé"); }
            else { failed++; done.Add($"{item.Name} : échec (code 0x{code:X8})"); }
        }
        var summary = $"{installed} installé(s)" + (already > 0 ? $", {already} déjà présent(s)" : "") + (failed > 0 ? $", {failed} en échec" : "") + ".";
        if (done.Count > 0) PackStatus.Text = summary;
        Log("Pack logiciels : " + PackStatus.Text);
        _report.Step("Pack logiciels nouveau PC", PackStatus.Text);
        _report.Detail("Logiciels du pack", done);
    }

    private void ReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_report.IsEmpty)
        {
            MessageBox.Show(this, "Lancer d'abord la maintenance : le rapport décrit les étapes réalisées.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new ExportWindow { Owner = this, FilePrefix = "Maintenance" };
        if (dialog.ShowDialog() != true || dialog.Info is null || dialog.FilePath is null) return;
        try
        {
            ReportWriter.Write(dialog.FilePath, dialog.Info, _report.ToSections());
            Process.Start(new ProcessStartInfo(dialog.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Impossible d'enregistrer le rapport : " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _stopRequested = true;
        StopButton.IsEnabled = false;
        StopButton.Content = "Arrêt demandé…";
        Log("Arrêt demandé : la maintenance s'arrête à la fin de l'étape en cours.");
    }

    private void Win11Upgrade_Click(object sender, RoutedEventArgs e)
    {
        const string title = "Passer à Windows 11";
        if (_running) return;
        var version = WindowsVersion.Read();
        if (version.IsWindows11 && MessageBox.Show(this,
                $"Ce PC est déjà sous {version.Name} {version.DisplayVersion}. Lancer quand même l'installation depuis une ISO (passage à une version plus récente, ou réparation en gardant les fichiers) ?",
                title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var blockers = Windows11Upgrade.Blockers();
        if (blockers.Count > 0)
        {
            Log("Passage à Windows 11 impossible : " + string.Join(" ", blockers));
            MessageBox.Show(this, string.Join("\n\n", blockers), title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "ISO de Windows 11 (téléchargée sur le site de Microsoft)",
            Filter = "Image ISO (*.iso)|*.iso",
            InitialDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        if (dialog.ShowDialog(this) != true) return;

        var warnings = Windows11Upgrade.Warnings();
        var message = "Windows 11 va être installé par-dessus Windows, en gardant les fichiers, les logiciels et les réglages.\n\n"
            + "• Ce PC n'est pas officiellement compatible : Microsoft ne garantit pas les mises à jour, et chaque nouvelle version annuelle "
            + "devra peut-être être installée de la même façon.\n"
            + "• Un filigrane « Configuration requise non remplie » peut apparaître.\n"
            + "• Le PC redémarre plusieurs fois (30 à 90 minutes). Brancher le chargeur.\n"
            + "• Une sauvegarde des données du client est conseillée avant de lancer.\n"
            + string.Concat(warnings.Select(w => "• " + w + "\n"))
            + "\nLancer la mise à niveau ?";
        if (MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var iso = dialog.FileName;
        var log = new Progress<string>(Log);
        Win11Button.IsEnabled = false;
        Task.Run(() => Windows11Upgrade.Start(iso, log)).ContinueWith(t =>
        {
            Win11Button.IsEnabled = true;
            var error = t.Exception?.GetBaseException().Message ?? t.Result;
            if (error is null)
            {
                _report.Step("Passage à Windows 11", "Mise à niveau lancée depuis l'ISO (PC non compatible), fichiers et logiciels gardés.");
                return;
            }
            Log("Passage à Windows 11 : " + error);
            MessageBox.Show(this, error, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void OpenWindowsUpdate_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true });

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (LogBox.Text.Length > 0) Clipboard.SetText(LogBox.Text);
    }

    private void Log(string text)
    {
        CrashGuard.Trace(text);
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_running) return;
        var answer = MessageBox.Show(this, "La maintenance est en cours. Fermer quand même ?\n\nUne installation déjà commencée continue en arrière-plan.",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) e.Cancel = true;
    }
}
