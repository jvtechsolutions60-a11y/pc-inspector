using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
        new SystemCollector(),
        new SoftwareCollector(),
        new DriversCollector(),
        new ServicesCollector(),
        new StartupCollector(),
    };

    // Chaque onglet n'est collecté qu'une fois : WMI peut prendre plusieurs secondes.
    private readonly Dictionary<ICollector, InfoSection> _cache = new();

    /// <summary>Un onglet de la barre latérale, rangé dans sa catégorie.</summary>
    public sealed record SidebarItem(ICollector Collector, string Category)
    {
        public string Title => Collector.Title;
        public string Icon => Collector.Icon;
    }

    public MainWindow()
    {
        InitializeComponent();
        // Les 8 premiers onglets décrivent le matériel, les 5 suivants le diagnostic, le reste le logiciel.
        var items = _collectors.Select((c, i) => new SidebarItem(c, i < 8 ? "MATÉRIEL" : i < 13 ? "DIAGNOSTIC" : "LOGICIEL")).ToList();
        var view = new ListCollectionView(items);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SidebarItem.Category)));
        SectionList.ItemsSource = view;
        SectionList.SelectedIndex = 0;
    }

    private ICollector? SelectedCollector => (SectionList.SelectedItem as SidebarItem)?.Collector;

    private async void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e) => await ShowSelectedAsync(false);

    private async Task ShowSelectedAsync(bool refresh)
    {
        if (SelectedCollector is not { } collector) return;

        SectionTitle.Text = collector.Title;
        GroupList.ItemsSource = null;

        if (refresh || !_cache.TryGetValue(collector, out var section))
        {
            ShowStatus("Analyse en cours…");
            LoadingBar.Visibility = Visibility.Visible;
            RefreshButton.IsEnabled = false;
            section = await Task.Run(() => CollectSafely(collector));
            _cache[collector] = section;
            LoadingBar.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
            if (SelectedCollector != collector) return;
        }

        ShowStatus(section.Error);
        ApplyFilter();
    }

    /// <summary>Affiche les lignes de l'onglet qui contiennent le texte recherché (toutes si la recherche est vide).</summary>
    private void ApplyFilter()
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (SelectedCollector is not { } collector || !_cache.TryGetValue(collector, out var section)) return;

        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            GroupList.ItemsSource = section.Groups;
            return;
        }

        bool Match(string? text) => text?.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        var groups = new List<InfoGroup>();
        foreach (var group in section.Groups)
        {
            var filtered = new InfoGroup(group.Title);
            filtered.Items.AddRange(Match(group.Title) ? group.Items : group.Items.Where(i => Match(i.Label) || Match(i.Value)));
            if (filtered.Items.Count > 0) groups.Add(filtered);
        }
        GroupList.ItemsSource = groups;
        if (groups.Count == 0) ShowStatus($"Aucune ligne ne contient « {query} ».");
        else ShowStatus(section.Error);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ShowSelectedAsync(true);

    /// <summary>Copie l'onglet affiché (filtré ou non) en texte, prêt à coller dans un devis ou un e-mail.</summary>
    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (GroupList.ItemsSource is not IEnumerable<InfoGroup> groups) return;
        var sb = new StringBuilder().AppendLine(SectionTitle.Text);
        foreach (var group in groups)
        {
            sb.AppendLine().AppendLine(group.Title);
            foreach (var item in group.Items) sb.AppendLine($"  {item.Label} : {item.Value}");
        }
        try
        {
            Clipboard.SetText(sb.ToString());
            ShowStatus("Onglet copié dans le presse-papiers.");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            ShowStatus("Presse-papiers occupé, réessayer.");
        }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocused)
        {
            SearchBox.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && RefreshButton.IsEnabled)
        {
            e.Handled = true;
            await ShowSelectedAsync(true);
        }
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
