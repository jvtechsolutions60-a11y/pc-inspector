using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PcInspector;

namespace JvtechOutils;

/// <summary>
/// Historique des interventions : Rapports\Client\Date\fichiers, rangés par les quatre logiciels.
/// On choisit un client, on voit chaque passage (date, logiciels utilisés, fichiers) et on ouvre un rapport d'un double-clic.
/// Lecture seule : rien n'est déplacé ni supprimé.
/// </summary>
public sealed class HistoryWindow : Window
{
    /// <summary>Dossiers techniques rangés dans Rapports, qui ne sont pas des clients.</summary>
    private static readonly HashSet<string> NotClients = new(StringComparer.OrdinalIgnoreCase)
        { "Journaux", "Plantages", "Raccourcis cassés", "JVTECH Migration" };

    private sealed record Client(string Name, string Folder, List<Visit> Visits)
    {
        public DateTime Last => Visits.Count > 0 ? Visits[0].Date : DateTime.MinValue;
        public override string ToString() => $"{Name}  ·  {Visits.Count} passage(s), dernier le {Last:dd/MM/yyyy}";
    }

    private sealed record Visit(DateTime Date, string Folder, List<string> Files);

    private readonly List<Client> _clients;
    private readonly TextBox _search = new() { Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 8) };
    private readonly ListBox _list = new();
    private readonly TreeView _visits = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    public HistoryWindow()
    {
        Title = "Historique des interventions";
        Width = 900;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");
        if (TryFindResource("ContentBrush") is Brush background) Background = background;
        if (TryFindResource("MutedTextBrush") is Brush muted) _status.Foreground = muted;

        _clients = Load();

        var left = new DockPanel { Margin = new Thickness(16, 16, 8, 16) };
        var label = new TextBlock { Text = "Rechercher un client", Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(label, Dock.Top);
        DockPanel.SetDock(_search, Dock.Top);
        left.Children.Add(label);
        left.Children.Add(_search);
        left.Children.Add(_list);

        var right = new DockPanel { Margin = new Thickness(8, 16, 16, 16) };
        var hint = new TextBlock { Text = "Double-clic sur un fichier pour l'ouvrir, sur une date pour ouvrir le dossier.", Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        right.Children.Add(hint);
        right.Children.Add(_status);
        right.Children.Add(_visits);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        Content = grid;

        _search.TextChanged += (_, _) => Filter();
        _list.SelectionChanged += (_, _) => ShowVisits(_list.SelectedItem as Client);
        _visits.MouseDoubleClick += Visits_DoubleClick;
        Filter();
        _status.Text = _clients.Count == 0
            ? "Aucune intervention enregistrée pour l'instant dans " + AppPaths.ReportsRoot
            : $"{_clients.Count} client(s), {_clients.Sum(c => c.Visits.Count)} intervention(s) · {AppPaths.ReportsRoot}";
    }

    private static List<Client> Load()
    {
        var clients = new List<Client>();
        try
        {
            if (!Directory.Exists(AppPaths.ReportsRoot)) return clients;
            foreach (var folder in Directory.GetDirectories(AppPaths.ReportsRoot))
            {
                var name = Path.GetFileName(folder);
                if (NotClients.Contains(name)) continue;
                var visits = new List<Visit>();
                foreach (var day in SafeDirectories(folder))
                {
                    if (!DateTime.TryParseExact(Path.GetFileName(day), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date)) continue;
                    var files = SafeFiles(day).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
                    visits.Add(new Visit(date, day, files));
                }
                if (visits.Count > 0) clients.Add(new Client(name, folder, visits.OrderByDescending(v => v.Date).ToList()));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return clients.OrderByDescending(c => c.Last).ToList();
    }

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try { return Directory.GetDirectories(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string folder)
    {
        try { return Directory.GetFiles(folder, "*", SearchOption.AllDirectories); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private void Filter()
    {
        var text = _search.Text.Trim();
        _list.ItemsSource = _clients.Where(c => text.Length == 0 || c.Name.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        else ShowVisits(null);
    }

    private void ShowVisits(Client? client)
    {
        _visits.Items.Clear();
        if (client is null) return;
        foreach (var visit in client.Visits)
        {
            var tools = Tools(visit.Files);
            var node = new TreeViewItem
            {
                Header = $"{visit.Date:dddd d MMMM yyyy}  ·  {visit.Files.Count} fichier(s)" + (tools.Length > 0 ? "  ·  " + tools : ""),
                Tag = visit.Folder,
                FontWeight = FontWeights.SemiBold,
                IsExpanded = visit == client.Visits[0],
            };
            foreach (var file in visit.Files)
                node.Items.Add(new TreeViewItem { Header = file.Substring(visit.Folder.Length).TrimStart('\\'), Tag = file, FontWeight = FontWeights.Normal });
            _visits.Items.Add(node);
        }
    }

    /// <summary>Quels logiciels ont servi ce jour-là, d'après les noms de fichiers.</summary>
    private static string Tools(List<string> files)
    {
        var names = string.Join("|", files.Select(Path.GetFileName)).ToLowerInvariant();
        var tools = new List<string>();
        if (names.Contains("inspector") || names.Contains("fiche") || names.Contains("rapport")) tools.Add("Inspector");
        if (names.Contains("sécurité") || names.Contains("securite")) tools.Add("Sécurité");
        if (names.Contains("maintenance") || names.Contains("préparation") || names.Contains("preparation")) tools.Add("Maintenance");
        if (names.Contains("migration")) tools.Add("Migration");
        return string.Join(", ", tools);
    }

    private void Visits_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_visits.SelectedItem is not TreeViewItem { Tag: string target }) return;
        e.Handled = true;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Win32Exception ex) { _status.Text = "Ouverture impossible : " + ex.Message; }
    }
}
