using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
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
        // Corrections d'abord, puis ce qu'il faut changer, puis ce qui est en ordre ; les plus graves en haut.
        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(_checks);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SecurityCheck.Group)));
        view.SortDescriptions.Add(new SortDescription(nameof(SecurityCheck.GroupOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(SecurityCheck.Severity), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(SecurityCheck.Order), ListSortDirection.Ascending));
        CheckList.ItemsSource = view;
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

        SecurityChecks.ResetCache();
        var all = SecurityChecks.All;
        for (int i = 0; i < all.Count; i++)
        {
            var (name, run) = all[i];
            ProgressText.Text = $"Contrôle {i + 1} sur {all.Count} : {name}…";
            var check = await Task.Run(() => SecurityChecks.RunSafely(name, run));
            check.Order = i;
            if (check.NeedsAction) check.Tutorial = Tutorials.For(check);
            _checks.Add(check);
        }

        var score = SecurityScore.Compute(_checks);
        ScoreText.Text = SecurityScore.Letter(score).ToString();
        ScoreCircle.Background = (Brush)FindResource(score >= 90 ? "OkBrush" : score >= 70 ? "WarningBrush" : "DangerBrush");
        VerdictText.Text = $"{SecurityScore.Verdict(score)} · {score} / 100";
        var toFix = _checks.Count(c => c.GroupOrder == 0);
        var toChange = _checks.Count(c => c.GroupOrder == 1);
        ProgressText.Text = toFix + toChange == 0
            ? "Aucun point à corriger."
            : $"{toFix} correction(s) à faire, {toChange} chose(s) à changer.";

        RescanButton.IsEnabled = ExportButton.IsEnabled = true;
        _running = false;
    }

    private async void RescanButton_Click(object sender, RoutedEventArgs e) => await RunAsync();

    /// <summary>Ouvre le réglage de Windows indiqué par le tutoriel.</summary>
    private void OpenSetting_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Tutorial { OpenFile: { } file } tutorial) return;
        try
        {
            Process.Start(new ProcessStartInfo(file) { Arguments = tutorial.OpenArgs ?? "", UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, "Impossible d'ouvrir ce réglage : " + ex.Message, "PC Sécurité", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

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
