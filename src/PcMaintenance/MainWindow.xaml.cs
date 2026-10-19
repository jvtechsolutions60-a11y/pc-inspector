using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PcMaintenance.Maintenance;

namespace PcMaintenance;

public partial class MainWindow : Window
{
    // « Aucun package installé ne correspond » : rien à mettre à jour.
    private const int WingetNothingToUpgrade = unchecked((int)0x8A150014);

    private string? _glaryFolder;
    private bool _running;
    private volatile bool _stopRequested;
    private volatile bool _rebootRequired;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ComputerText.Text = Environment.MachineName;
        ShowWindowsVersion();
        ShowGlary();
        _ = RefreshWindowsUpdateAsync();
        _ = RefreshWingetAsync();
    }

    // ---------- Résumé rapide ----------

    private void ShowWindowsVersion()
    {
        var version = WindowsVersion.Read();
        SetCard(Win11Value, version.IsWindows11 ? "Oui" : "Non", version.IsWindows11 ? "OkBrush" : "WarnBrush");
        Win11Detail.Text = $"{version.Name} {version.DisplayVersion} (build {version.Build})".Replace("  ", " ")
            + (version.IsWindows11 ? "" : "\nCompatibilité : voir PC Inspector, onglet Windows 11.");
    }

    private void ShowGlary()
    {
        _glaryFolder = Glary.FindFolder();
        if (_glaryFolder is not null)
        {
            SetCard(GlaryValue, "Installé", "OkBrush");
            GlaryDetail.Text = _glaryFolder;
            return;
        }
        SetCard(GlaryValue, "Absent", "WarnBrush");
        GlaryDetail.Text = "Non installé sur ce PC.";
        GlaryCheck.IsChecked = false;
        GlaryCheck.IsEnabled = false;
        GlaryStatus.Text = "Glary Utilities n'est pas installé : étape ignorée.";
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
                updates.Count == 0 && !reboot ? "OkBrush" : "WarnBrush");
            var titles = updates.Take(3).Select(u => "• " + u.Title).ToList();
            if (updates.Count > 3) titles.Add($"… et {updates.Count - 3} autre(s)");
            if (reboot) titles.Insert(0, "Redémarrage en attente pour finir une installation.");
            WuDetail.Text = titles.Count == 0 ? "Aucune mise à jour à installer." : string.Join("\n", titles);
            WuDetail.ToolTip = updates.Count == 0 ? null : string.Join("\n", updates.Select(u => u.Title));
        }
        catch (Exception ex) when (WindowsUpdate.IsExpected(ex))
        {
            SetCard(WuValue, "?", "BadBrush");
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
            SetCard(WingetValue, "?", "BadBrush");
            WingetDetail.Text = result.Error;
            return;
        }
        SetCard(WingetValue, updates.Count == 0 ? "À jour" : $"{updates.Count} à mettre à jour", updates.Count == 0 ? "OkBrush" : "WarnBrush");
        var names = updates.Take(3).Select(u => $"• {u.Name} {u.Installed} → {u.Available}").ToList();
        if (updates.Count > 3) names.Add($"… et {updates.Count - 3} autre(s)");
        WingetDetail.Text = names.Count == 0 ? "Tous les logiciels connus de winget sont à jour." : string.Join("\n", names);
        WingetDetail.ToolTip = updates.Count == 0 ? null : string.Join("\n", updates.Select(u => $"{u.Name} {u.Installed} → {u.Available}"));
    }

    private void SetCard(TextBlock value, string text, string? brush)
    {
        value.Text = text;
        value.Foreground = brush is null ? Brushes.Black : (Brush)FindResource(brush);
    }

    // ---------- Maintenance ----------

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        if (GlaryCheck.IsChecked != true && WuCheck.IsChecked != true && WingetCheck.IsChecked != true)
        {
            MessageBox.Show(this, "Cocher au moins une étape.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetRunning(true);
        Log("===== Début de la maintenance =====");
        try
        {
            if (GlaryCheck.IsChecked == true && !_stopRequested) await RunGlaryAsync();
            if (WuCheck.IsChecked == true && !_stopRequested) await RunWindowsUpdateAsync();
            if (WingetCheck.IsChecked == true && !_stopRequested) await RunWingetAsync();
            Log(_stopRequested ? "===== Maintenance arrêtée =====" : "===== Maintenance terminée =====");
        }
        finally
        {
            SetRunning(false);
        }

        _ = RefreshWindowsUpdateAsync();
        _ = RefreshWingetAsync();

        if (_rebootRequired || WindowsUpdate.IsRebootPending())
        {
            var answer = MessageBox.Show(this, "Un redémarrage est nécessaire pour finir les mises à jour.\n\nRedémarrer le PC maintenant ?",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { UseShellExecute = false, CreateNoWindow = true });
        }
    }

    private async Task RunGlaryAsync()
    {
        if (_glaryFolder is null) return;
        GlaryStatus.Text = "En cours : fermer la fenêtre de Glary une fois la maintenance terminée.";
        Log("--- Glary Utilities : maintenance 1 clic ---");
        try
        {
            var waited = await Glary.RunOneClickAsync(_glaryFolder, new Progress<string>(Log));
            if (!waited)
            {
                // Glary a rendu la main tout de suite (déjà ouvert, ou lancé dans une autre fenêtre).
                MessageBox.Show(this, "Glary Utilities est ouvert.\n\nCliquer sur OK quand la maintenance 1 clic est terminée pour passer à la suite.",
                    Title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            GlaryStatus.Text = "Terminé.";
            Log("Glary Utilities : terminé.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            GlaryStatus.Text = "Lancement impossible : " + ex.Message;
            Log("Glary Utilities : lancement impossible : " + ex.Message);
        }
    }

    private async Task RunWindowsUpdateAsync()
    {
        Log("--- Mises à jour Windows ---");
        var status = new Progress<string>(text => WuStatus.Text = text);
        var log = new Progress<string>(Log);
        var outcome = await Task.Run(() => InstallWindowsUpdates(status, log));
        WuStatus.Text = outcome;
        Log("Windows Update : " + outcome);
    }

    /// <summary>Recherche puis installe chaque mise à jour, en s'arrêtant entre deux si on le demande.</summary>
    private string InstallWindowsUpdates(IProgress<string> status, IProgress<string> log)
    {
        try
        {
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
        StopButton.IsEnabled = running;
        StopButton.Content = "Arrêter après l'étape";
        GlaryCheck.IsEnabled = !running && _glaryFolder is not null;
        WuCheck.IsEnabled = !running;
        WingetCheck.IsEnabled = !running;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _stopRequested = true;
        StopButton.IsEnabled = false;
        StopButton.Content = "Arrêt demandé…";
        Log("Arrêt demandé : la maintenance s'arrête à la fin de l'étape en cours.");
    }

    private void OpenWindowsUpdate_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true });

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (LogBox.Text.Length > 0) Clipboard.SetText(LogBox.Text);
    }

    private void Log(string text)
    {
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
