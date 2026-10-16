using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using PcInspector;
using PcInspector.Report;
using PcSecurite.Checks;

namespace PcSecurite;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<SecurityCheck> _checks = new();
    private bool _running;

    public MainWindow()
    {
        InitializeComponent();
        CheckList.ItemsSource = _checks;
        Loaded += async (_, _) => await RunAsync();
    }

    /// <summary>Lance les contrôles un par un : chaque feu apparaît dès que son contrôle est fini.</summary>
    private async Task RunAsync()
    {
        if (_running) return;
        _running = true;
        RescanButton.IsEnabled = ExportButton.IsEnabled = false;
        _checks.Clear();
        ScoreText.Text = "…";
        ScoreCircle.Background = (Brush)FindResource("InfoBrush");
        VerdictText.Text = "Analyse en cours…";

        var all = SecurityChecks.All;
        for (int i = 0; i < all.Count; i++)
        {
            var (name, run) = all[i];
            ProgressText.Text = $"Contrôle {i + 1} sur {all.Count} : {name}…";
            _checks.Add(await Task.Run(() => SecurityChecks.RunSafely(name, run)));
        }

        var score = SecurityScore.Compute(_checks);
        ScoreText.Text = score.ToString();
        ScoreCircle.Background = (Brush)FindResource(score >= 90 ? "OkBrush" : score >= 70 ? "WarningBrush" : "DangerBrush");
        VerdictText.Text = SecurityScore.Verdict(score);
        var toFix = _checks.Count(c => c.Status is CheckStatus.Danger or CheckStatus.Warning);
        ProgressText.Text = toFix == 0 ? "Aucun point à corriger." : $"{toFix} point(s) à corriger.";

        RescanButton.IsEnabled = ExportButton.IsEnabled = true;
        _running = false;
    }

    private async void RescanButton_Click(object sender, RoutedEventArgs e) => await RunAsync();

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ExportWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Info is null || dialog.FilePath is null) return;
        try
        {
            ReportWriter.Write(dialog.FilePath, dialog.Info, SecurityReport.ToSections(_checks.ToList()));
            Process.Start(new ProcessStartInfo(dialog.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Impossible d'enregistrer le rapport : " + ex.Message, "Exporter le rapport",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
