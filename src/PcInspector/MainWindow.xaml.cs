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
            new LicenseCollector(),
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

    // Collectes lancées (ou finies) : un onglet déjà en cours de lecture en arrière-plan n'est pas relancé au clic.
    private readonly Dictionary<ICollector, Task<InfoSection>> _tasks = new();

    // Lecture des onglets en arrière-plan pendant que le technicien regarde le bilan : trois à la fois, pour ne pas
    // saturer WMI ni ralentir le PC. Un onglet cliqué n'attend pas son tour.
    private readonly System.Threading.SemaphoreSlim _prefetchGate = new(3);
    private bool _prefetched;
    private readonly HashSet<ICollector> _queued = new();

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
            if (refresh) { _cache.Clear(); _tasks.Clear(); lock (_queued) _queued.Clear(); _prefetched = false; }
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
            section = await SectionAsync(collector, refresh);
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
            section = await SectionAsync(_health, refresh: false);
            _cache[_health] = section;
            HealthRefreshButton.IsEnabled = true;
            Prefetch();
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
        if (tab == Finding.SecurityApp)
        {
            OpenSecurityApp();
            return;
        }
        var target = (SectionList.ItemsSource as ListCollectionView)?.Cast<SidebarItem>().FirstOrDefault(i => i.Title == tab);
        if (target is not null) SectionList.SelectedItem = target;
    }

    /// <summary>Le détail de la sécurité est dans PC Sécurité, posé à côté de PC Inspector par le lanceur.</summary>
    private void OpenSecurityApp()
    {
        var path = System.IO.Path.Combine(AppPaths.ExeFolder, "PcSecurite.exe");
        if (!System.IO.File.Exists(path))
        {
            MessageBox.Show(this, "PcSecurite.exe est introuvable : le mettre dans le même dossier que PcInspector.exe (comme dans le dossier JVTECH Outils).",
                "PC Sécurité", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = AppPaths.ExeFolder }); }
        catch (System.ComponentModel.Win32Exception) { /* droits administrateur refusés */ }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        // Plus de fenêtre à remplir : société, technicien et logo mémorisés, client du jour s'il y en a un, sinon le PC.
        var info = ReportInfo.LoadDefaults();
        info.Date = DateTime.Now;
        var pcName = $"{MachineIdentity.Model()} {MachineIdentity.Serial()}".Trim();
        var folder = AppPaths.ClientFolder(string.IsNullOrWhiteSpace(info.Client) ? pcName : info.Client, info.Date);
        var baseName = string.Join("_", new[] { "Rapport", info.JobNumber, pcName, info.Date.ToString("yyyy-MM-dd HH-mm") }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) baseName = baseName.Replace(c, '-');

        ExportButton.IsEnabled = false;
        HealthExportButton.IsEnabled = false;
        ShowStatus("Analyse complète du PC pour le rapport…");
        try
        {
            // Le rapport contient tous les onglets, y compris ceux pas encore ouverts.
            // Tous en même temps : ceux déjà lus en arrière-plan sont immédiats.
            var sections = (await Task.WhenAll(_collectors.Select(c => _cache.TryGetValue(c, out var cached)
                ? Task.FromResult(cached) : SectionAsync(c, refresh: false)))).ToList();
            for (var i = 0; i < _collectors.Count; i++) _cache[_collectors[i]] = sections[i];

            // PDF d'abord ; sans Edge ni Chrome, la page web (même contenu, imprimable depuis le navigateur).
            var path = System.IO.Path.Combine(folder, baseName + ".pdf");
            try { await Task.Run(() => ReportWriter.Write(path, info, sections)); }
            catch (System.IO.IOException)
            {
                path = System.IO.Path.Combine(folder, baseName + ".html");
                await Task.Run(() => ReportWriter.Write(path, info, sections));
            }
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

    /// <summary>La collecte d'un onglet : reprise si elle est déjà lancée, relancée si on demande d'actualiser.</summary>
    private Task<InfoSection> SectionAsync(ICollector collector, bool refresh)
    {
        bool waiting;
        lock (_queued) waiting = _queued.Remove(collector);
        // Encore en file d'attente derrière les autres onglets : on le lit tout de suite à part.
        if (refresh || waiting || !_tasks.TryGetValue(collector, out var task))
        {
            task = Task.Run(() => CollectSafely(collector));
            _tasks[collector] = task;
        }
        return task;
    }

    /// <summary>
    /// Une fois le bilan affiché, les autres onglets sont lus en arrière-plan : au clic, ils s'affichent tout de suite.
    /// Le test de débit (réseau) n'est lancé qu'à la demande.
    /// </summary>
    private void Prefetch()
    {
        if (_prefetched) return;
        _prefetched = true;
        foreach (var collector in _collectors.Where(c => c != _health && c is not SpeedTestCollector))
        {
            if (_tasks.ContainsKey(collector)) continue;
            lock (_queued) _queued.Add(collector);
            _tasks[collector] = Task.Run(async () =>
            {
                await _prefetchGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    // Déjà lu à part parce que le technicien a cliqué dessus : rien à faire.
                    lock (_queued) if (!_queued.Remove(collector)) return new InfoSection(collector.Title);
                    return CollectSafely(collector);
                }
                finally { _prefetchGate.Release(); }
            });
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

    private void Listing_Click(object sender, RoutedEventArgs e) => new ListingWindow { Owner = this }.Show();

    /// <summary>Impression directe sur l'étiqueteuse choisie dans la fenêtre d'impression de Windows.</summary>
    private void Label_Click(object sender, RoutedEventArgs e)
    {
        var info = ReportInfo.LoadDefaults();
        info.Date = DateTime.Now;
        try { LabelPrinter.Print(this, info); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Printing.PrintSystemException or System.IO.IOException)
        {
            MessageBox.Show(this, "Impression impossible : " + ex.Message, "Étiquette PC", MessageBoxButton.OK, MessageBoxImage.Error);
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
                Process.Start(new ProcessStartInfo(AppPaths.SystemTool("notepad.exe"), $"\"{file}\"") { UseShellExecute = true });
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
