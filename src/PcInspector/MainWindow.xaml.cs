using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using PcInspector.Collectors;
using PcInspector.Models;
using PcInspector.Report;

namespace PcInspector;

public partial class MainWindow : Window
{
    // Les icônes viennent de la police Segoe MDL2 Assets, présente sur Windows 10 et 11.
    private readonly List<ICollector> _collectors = new()
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
    };

    // Chaque onglet n'est collecté qu'une fois : WMI peut prendre plusieurs secondes.
    private readonly Dictionary<ICollector, InfoSection> _cache = new();

    public MainWindow()
    {
        InitializeComponent();
        SectionList.ItemsSource = _collectors;
        SectionList.SelectedIndex = 0;
    }

    private async void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedItem is not ICollector collector) return;

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

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ExportWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Info is null || dialog.FilePath is null) return;

        ExportButton.IsEnabled = false;
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
        StatusText.Text = message ?? string.Empty;
        StatusText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }
}
