using System.Windows;
using System.Windows.Controls;
using PcInspector.Collectors;
using PcInspector.Models;

namespace PcInspector;

public partial class MainWindow : Window
{
    // Les icônes viennent de la police Segoe MDL2 Assets, présente sur Windows 10 et 11.
    private readonly List<ICollector> _collectors = new()
    {
        new SummaryCollector(),
        new PlaceholderCollector("Processeur", ""),
        new PlaceholderCollector("Carte mère", ""),
        new PlaceholderCollector("Mémoire", ""),
        new PlaceholderCollector("Carte graphique", ""),
        new PlaceholderCollector("Stockage", ""),
        new PlaceholderCollector("Réseau", ""),
        new PlaceholderCollector("Températures", ""),
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

    private void ShowStatus(string? message)
    {
        StatusText.Text = message ?? string.Empty;
        StatusText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }
}
