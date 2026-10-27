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
using System.Windows.Media;
using System.Windows.Navigation;
using PcInspector.Collectors;
using PcInspector.Models;
using PcInspector.Report;

namespace PcInspector;

public partial class MainWindow : Window
{
    private readonly HealthCollector _health = new();
    private string _updateUrl = Updater.ReleasesPage;

    // Les icônes viennent de la police Segoe MDL2 Assets, présente sur Windows 10 et 11.
    private static IEnumerable<SidebarItem> Tabs() => new (string Category, ICollector[] Collectors)[]
    {
        ("MATÉRIEL", new ICollector[]
        {
            new SummaryCollector(),
            new CpuCollector(),
            new MotherboardCollector(),
            new MemoryCollector(),
            new GpuCollector(),
            new StorageCollector(),
            new DiskUsageCollector(),
            new NetworkCollector(),
            new SpeedTestCollector(),
            new TemperatureCollector(),
        }),
        ("DIAGNOSTIC", new ICollector[]
        {
            new DiagnosticCollector(),
            new MissingDriversCollector(),
            new BootPerformanceCollector(),
            new BatteryCollector(),
            new Windows11Collector(),
            new SecurityCollector(),
            new LicenseCollector(),
            new AdviceCollector(),
            new UpgradeQuoteCollector(),
        }),
        ("LOGICIEL", new ICollector[]
        {
            new SystemCollector(),
            new SoftwareCollector(),
            new WingetCollector(),
            new DriversCollector(),
            new ServicesCollector(),
            new StartupCollector(),
        }),
    }.SelectMany(g => g.Collectors.Select(c => new SidebarItem(c, g.Category)));

    private readonly List<ICollector> _collectors;

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
        VersionText.Text = App.Version;
        Title = "PC Inspector " + App.Version;
        PortableText.Visibility = AppPaths.IsPortable ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) =>
        {
            if (await Updater.CheckAsync() is not { } update) return;
            _updateUrl = update.Url;
            UpdateText.Text = $"Nouvelle version {update.Tag} : télécharger";
            UpdateNotice.Visibility = Visibility.Visible;
        };
        var items = new List<SidebarItem> { new(_health, "ACCUEIL") };
        items.AddRange(Tabs());
        _collectors = items.Select(i => i.Collector).ToList();
        var view = new ListCollectionView(items);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SidebarItem.Category)));
        SectionList.ItemsSource = view;
        // Le PC est analysé dès l'ouverture : le bilan est l'écran d'accueil.
        SectionList.SelectedIndex = 0;
    }

    private ICollector? SelectedCollector => (SectionList.SelectedItem as SidebarItem)?.Collector;

    private async void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e) => await ShowSelectedAsync(false);

    private async Task ShowSelectedAsync(bool refresh)
    {
        if (SelectedCollector is not { } collector) return;

        var isHealth = collector == _health;
        HealthPanel.Visibility = isHealth ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.Visibility = isHealth ? Visibility.Collapsed : Visibility.Visible;
        if (isHealth)
        {
            // Après une intervention, tous les onglets sont relus, pas seulement le bilan.
            if (refresh) _cache.Clear();
            await ShowHealth();
            return;
        }

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
        else if (e.Key == Key.F5 && RefreshButton.IsEnabled && HealthRefreshButton.IsEnabled)
        {
            e.Handled = true;
            await ShowSelectedAsync(true);
        }
    }

    private async Task ShowHealth()
    {
        if (!_cache.TryGetValue(_health, out var section))
        {
            ScoreText.Text = "…";
            ScoreBadge.Background = (Brush)FindResource("InfoBrush");
            VerdictText.Text = "Bilan en cours…";
            CountsText.Text = "Vérification des disques, de la mémoire, de la batterie, des plantages, de la sécurité et des mises à jour.";
            FindingList.ItemsSource = null;
            HealthRefreshButton.IsEnabled = false;
            section = await Task.Run(() => CollectSafely(_health));
            _cache[_health] = section;
            HealthRefreshButton.IsEnabled = true;
            if (SelectedCollector != _health) return;
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

    private async void HealthRefreshButton_Click(object sender, RoutedEventArgs e) => await ShowSelectedAsync(true);

    private void FindingDetail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tab) return;
        var target = (SectionList.ItemsSource as ListCollectionView)?.Cast<SidebarItem>().FirstOrDefault(i => i.Title == tab);
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

    private void PeripheralTest_Click(object sender, RoutedEventArgs e) => new PeripheralTestWindow { Owner = this }.Show();

    private void LiveMonitor_Click(object sender, RoutedEventArgs e) => new LiveMonitorWindow { Owner = this }.Show();

    private async void Label_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ExportWindow
        {
            Owner = this,
            FilePrefix = "Etiquette",
            Filter = "Étiquette à imprimer (*.html)|*.html|PDF (*.pdf)|*.pdf",
        };
        if (dialog.ShowDialog() != true || dialog.Info is null || dialog.FilePath is null) return;
        var info = dialog.Info;
        var path = dialog.FilePath;
        try
        {
            await Task.Run(() => LabelWriter.Write(path, info));
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Impossible d'enregistrer l'étiquette : " + ex.Message, "Étiquette PC", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TrackingSheet_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ExportWindow
        {
            Owner = this,
            FilePrefix = "Fiche-suivi",
            Filter = "PDF (*.pdf)|*.pdf|Page web (*.html)|*.html",
        };
        if (dialog.ShowDialog() != true || dialog.Info is null || dialog.FilePath is null) return;
        var info = dialog.Info;
        var path = dialog.FilePath;
        ShowStatus("Fiche de suivi : bilan du PC en cours…");
        try
        {
            await Task.Run(() => TrackingSheetWriter.Write(path, info));
            ShowStatus("Fiche de suivi enregistrée.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Impossible d'enregistrer la fiche de suivi : " + ex.Message, "Fiche de suivi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateLink_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_updateUrl) { UseShellExecute = true });

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri.OriginalString == Prices.EditLink)
        {
            e.Handled = true;
            try
            {
                var file = Prices.EnsureFile();
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true });
                ShowStatus("Après avoir enregistré les prix, actualiser l'onglet (F5).");
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                ShowStatus("Impossible d'ouvrir le fichier des prix : " + ex.Message);
            }
            return;
        }
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
