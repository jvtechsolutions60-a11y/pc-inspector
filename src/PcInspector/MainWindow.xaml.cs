using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using PcInspector.Collectors;
using PcInspector.Models;
using PcInspector.Report;

namespace PcInspector;

public partial class MainWindow : Window
{
    private readonly HealthCollector _health = new();
    private readonly List<ICollector> _collectors;

    // Chaque onglet n'est collecté qu'une fois : WMI peut prendre plusieurs secondes.
    private readonly Dictionary<ICollector, InfoSection> _cache = new();

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = App.Version;
        Title = "PC Inspector " + App.Version;
        _collectors = new List<ICollector> { _health };
        _collectors.AddRange(DetailCollectors());
        SectionList.ItemsSource = _collectors;
        // Le PC est analysé dès l'ouverture : le bilan est l'écran d'accueil.
        SectionList.SelectedIndex = 0;
    }

    // Les icônes viennent de la police Segoe MDL2 Assets, présente sur Windows 10 et 11.
    private static IEnumerable<ICollector> DetailCollectors() => new ICollector[]
    {
        new SummaryCollector(),
        new CpuCollector(),
        new MotherboardCollector(),
        new MemoryCollector(),
        new GpuCollector(),
        new StorageCollector(),
        new NetworkCollector(),
        new TemperatureCollector(),
        new DiagnosticCollector(),
        new BatteryCollector(),
        new Windows11Collector(),
        new SecurityCollector(),
        new AdviceCollector(),
        new SystemCollector(),
        new SoftwareCollector(),
        new DriversCollector(),
        new ServicesCollector(),
        new StartupCollector(),
    };

    private async void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedItem is not ICollector collector) return;

        var isHealth = collector == _health;
        HealthPanel.Visibility = isHealth ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.Visibility = isHealth ? Visibility.Collapsed : Visibility.Visible;
        if (isHealth)
        {
            await ShowHealth();
            return;
        }

        SectionTitle.Text = collector.Title;
        GroupList.ItemsSource = null;

        if (!_cache.TryGetValue(collector, out var section))
        {
            ShowStatus("Analyse en cours…");
            section = await Task.Run(() => CollectSafely(collector));
            _cache[collector] = section;
            if (SectionList.SelectedItem != collector) return;
        }

        ShowStatus(section.Error);
        GroupList.ItemsSource = section.Groups;
    }

    private async Task ShowHealth()
    {
        if (!_cache.TryGetValue(_health, out var section))
        {
            ScoreText.Text = "…";
            ScoreBadge.Background = (Brush)FindResource("MutedTextBrush");
            VerdictText.Text = "Bilan en cours…";
            CountsText.Text = "Vérification des disques, de la mémoire, de la batterie, des plantages et de la sécurité.";
            FindingList.ItemsSource = null;
            RefreshButton.IsEnabled = false;
            section = await Task.Run(() => CollectSafely(_health));
            _cache[_health] = section;
            RefreshButton.IsEnabled = true;
            if (SectionList.SelectedItem != _health) return;
        }

        if (_health.Report is not { } report)
        {
            VerdictText.Text = "Bilan impossible";
            CountsText.Text = section.Error;
            return;
        }

        ScoreText.Text = report.Score.ToString();
        ScoreBadge.Background = (Brush)FindResource(report.Level switch
        {
            Severity.Critical => "CriticalBrush",
            Severity.Warning => "WarningBrush",
            _ => "OkBrush",
        });
        VerdictText.Text = report.Verdict;
        CountsText.Text = report.Counts;
        FindingList.ItemsSource = report.Findings;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        // Après une intervention, tous les onglets sont relus, pas seulement le bilan.
        _cache.Clear();
        await ShowHealth();
    }

    private void FindingDetail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tab) return;
        var target = _collectors.FirstOrDefault(c => c.Title == tab);
        if (target is not null) SectionList.SelectedItem = target;
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ExportWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Info is null || dialog.FilePath is null) return;

        ExportButton.IsEnabled = false;
        HealthExportButton.IsEnabled = false;
        ShowStatus("Analyse complète du PC pour le rapport…");
        try
        {
            // Le rapport contient tous les onglets, y compris ceux pas encore ouverts.
            var sections = new List<InfoSection>();
            foreach (var collector in _collectors)
            {
                if (!_cache.TryGetValue(collector, out var section))
                {
                    section = await Task.Run(() => CollectSafely(collector));
                    _cache[collector] = section;
                }
                sections.Add(section);
            }

            var info = dialog.Info;
            var path = dialog.FilePath;
            await Task.Run(() => ReportWriter.Write(path, info, sections));
            ShowStatus(null);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            ShowStatus(null);
            MessageBox.Show(this, "Impossible d'enregistrer le rapport : " + ex.Message, "Exporter le rapport",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ExportButton.IsEnabled = true;
            HealthExportButton.IsEnabled = true;
        }
    }

    private static InfoSection CollectSafely(ICollector collector)
    {
        try
        {
            return collector.Collect();
        }
        catch (Exception ex)
        {
            return new InfoSection(collector.Title) { Error = $"Impossible de lire ces informations : {ex.Message}" };
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void ShowStatus(string? message)
    {
        var visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Text = HealthStatus.Text = message ?? string.Empty;
        StatusText.Visibility = HealthStatus.Visibility = visibility;
    }
}
