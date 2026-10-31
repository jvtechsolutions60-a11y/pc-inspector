using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PcInspector;
using PcMaintenance.Preparation;

namespace PcMaintenance;

/// <summary>
/// Écran de la préparation revente, lancé par « PcMaintenance.exe /preparation » (au premier démarrage en mode audit,
/// puis après chaque redémarrage). Il n'y a rien à cliquer : il montre où en est le PC.
/// </summary>
public partial class PreparationWindow : Window
{
    private sealed record StepRow(string Name, string Hint, string Mark, Brush Brush, FontWeight Weight);

    private static readonly (PreparationStep Step, string Name, string Hint)[] Steps =
    {
        (PreparationStep.Start, "Mise en route", "Réseau, pas de veille"),
        (PreparationStep.Debloat, "Applications inutiles", "Jeux et essais préinstallés retirés"),
        (PreparationStep.Manufacturer, "Constructeur", "Pilotes et BIOS Dell, Lenovo ou HP"),
        (PreparationStep.WindowsUpdate, "Windows Update", "Mises à jour et pilotes, jusqu'à 0"),
        (PreparationStep.Finish, "Contrôle et rapport", "Pilotes manquants, activation, nettoyage"),
    };

    private PreparationRunner? _runner;
    private bool _finished;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMinutes(1) };

    public PreparationWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Title = "Préparation revente " + AppInfo.Version;
        // Reprend le journal des passages précédents, pour tout voir après un redémarrage.
        try
        {
            if (File.Exists(PreparationState.LogPath)) LogBox.Text = File.ReadAllText(PreparationState.LogPath);
            LogBox.ScrollToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        _runner = new PreparationRunner(new Progress<string>(Log), new Progress<PreparationStep>(ShowStep), new Progress<string>(t => StatusText.Text = t));
        ShowStep(_runner.Current);
        _clock.Tick += (_, _) => ShowElapsed();
        _clock.Start();
        ShowElapsed();

        var outcome = await _runner.RunAsync();
        _finished = true;
        Busy.IsIndeterminate = false;
        StopButton.IsEnabled = false;
        switch (outcome)
        {
            case PreparationRunner.Outcome.Reseal:
                ShowStep(PreparationStep.Done);
                StatusText.Text = "Terminé : le PC s'éteint, prêt pour son acheteur.";
                _runner.Reseal();
                await System.Threading.Tasks.Task.Delay(2000);
                Application.Current.Shutdown();
                break;
            case PreparationRunner.Outcome.Finished:
                ShowStep(PreparationStep.Done);
                StatusText.Text = "Terminé. Le PC reste en mode audit (remise au premier démarrage désactivée dans options.ini).";
                break;
            case PreparationRunner.Outcome.Reboot:
                StatusText.Text += " Reprise automatique après le redémarrage.";
                break;
            default:
                StatusText.Text = "Préparation arrêtée.";
                break;
        }
    }

    private void ShowElapsed()
    {
        if (_runner is null) return;
        var elapsed = DateTime.Now - _runner.Started;
        SinceText.Text = $"Commencée le {_runner.Started:dd/MM à HH:mm}, depuis {(int)elapsed.TotalHours} h {elapsed.Minutes:00}";
    }

    private void ShowStep(PreparationStep current)
    {
        var done = (Brush)FindResource("OkBrush");
        var active = (Brush)FindResource("AccentBrush");
        var waiting = (Brush)FindResource("InfoBrush");
        StepList.ItemsSource = Steps.Select(s => s.Step < current
                ? new StepRow(s.Name, s.Hint, "✔", done, FontWeights.Normal)
                : s.Step == current
                    ? new StepRow(s.Name, s.Hint, "▶", active, FontWeights.SemiBold)
                    : new StepRow(s.Name, s.Hint, "○", waiting, FontWeights.Normal))
            .ToList();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_runner is null) return;
        var answer = MessageBox.Show(this, "Arrêter la préparation à la fin de l'étape en cours ?\n\nElle ne se relancera plus au démarrage. Pour la reprendre : C:\\JVTECH\\PcMaintenance.exe /preparation",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _runner.StopRequested = true;
        StopButton.IsEnabled = false;
        StopButton.Content = "Arrêt demandé…";
        Log("Arrêt demandé : la préparation s'arrête à la fin de l'étape en cours.");
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (LogBox.Text.Length > 0) Clipboard.SetText(LogBox.Text);
    }

    private void Log(string text)
    {
        CrashGuard.Trace(text);
        LogBox.AppendText($"[{DateTime.Now:dd/MM HH:mm:ss}] {text}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_finished) return;
        var answer = MessageBox.Show(this, "La préparation est en cours. Fermer quand même ?\n\nElle reprendra au prochain démarrage du PC.",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) e.Cancel = true;
    }
}
