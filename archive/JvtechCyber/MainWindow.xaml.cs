using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JvtechCyber.Diagnostic;
using Microsoft.Win32;
using PcInspector;
using PcInspector.Report;

namespace JvtechCyber;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, List<RadioButton>> _levelButtons = new();
    private readonly Dictionary<string, TextBox> _remarkBoxes = new();
    private readonly Dictionary<string, TextBlock> _findingTexts = new();
    private CyberDiagnostic _diagnostic = new();
    private CyberDiagnostic? _previous;
    private string _updateUrl = Updater.ReleasesPage;
    private bool _loading;

    public MainWindow()
    {
        InitializeComponent();
        Title = "JVTECH Diagnostic Cyber " + AppInfo.Version;
        BuildThemes();
        Display(new CyberDiagnostic());
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshCompanies();
        if (await Updater.CheckAsync() is not { } update) return;
        _updateUrl = update.Url;
        UpdateText.Text = $"   Nouvelle version {update.Tag} disponible";
    }

    // ---------- Questionnaire ----------

    private void BuildThemes()
    {
        var number = 1;
        foreach (var theme in Referentiel.Themes)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = $"{number++}. {theme.Name}" + (theme.Weight > 1 ? "   ★ essentiel" : ""),
                Style = (Style)FindResource("SectionTitle"),
                Margin = new Thickness(0, 0, 0, 2),
            });
            panel.Children.Add(new TextBlock { Text = theme.Question, Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 0, 0, 8) });

            var buttons = new List<RadioButton>();
            foreach (var level in theme.Levels)
            {
                var button = new RadioButton
                {
                    GroupName = theme.Id,
                    Margin = new Thickness(0, 3, 0, 3),
                    Content = new TextBlock { Text = $"{level.Value} · {level.Situation}", TextWrapping = TextWrapping.Wrap },
                };
                button.Checked += (_, _) => LevelChanged();
                buttons.Add(button);
                panel.Children.Add(button);
            }
            _levelButtons[theme.Id] = buttons;

            var finding = new TextBlock { Foreground = (Brush)FindResource("InfoBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
            _findingTexts[theme.Id] = finding;
            panel.Children.Add(finding);

            var remark = new TextBox { Margin = new Thickness(0, 8, 0, 0), ToolTip = "Remarque sur ce thème (reprise dans le rapport)" };
            _remarkBoxes[theme.Id] = remark;
            var remarkRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var label = new TextBlock { Text = "Remarque", Foreground = (Brush)FindResource("MutedTextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 8, 0) };
            DockPanel.SetDock(label, Dock.Left);
            remarkRow.Children.Add(label);
            remarkRow.Children.Add(remark);
            panel.Children.Add(remarkRow);

            ThemePanel.Children.Add(new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 12), Child = panel });
        }
    }

    /// <summary>Affiche un diagnostic dans le formulaire.</summary>
    private void Display(CyberDiagnostic diagnostic)
    {
        _loading = true;
        _diagnostic = diagnostic;
        _previous = diagnostic.Company.Length > 0 ? diagnostic.Previous() : null;
        CompanyBox.Text = diagnostic.Company;
        ContactBox.Text = diagnostic.ContactName;
        ActivityBox.Text = diagnostic.Activity;
        WorkstationsBox.Text = diagnostic.Workstations > 0 ? diagnostic.Workstations.ToString() : "";
        DateBox.SelectedDate = diagnostic.Date;
        NotesBox.Text = diagnostic.Notes;
        foreach (var theme in Referentiel.Themes)
        {
            _levelButtons[theme.Id][diagnostic.Level(theme.Id)].IsChecked = true;
            _remarkBoxes[theme.Id].Text = diagnostic.Remarks.TryGetValue(theme.Id, out var r) ? r : "";
        }
        _loading = false;
        RefreshPostes();
        RefreshScore();
        var history = diagnostic.Company.Length > 0 ? CyberDiagnostic.History(diagnostic.Company) : new List<string>();
        HistoryText.Text = history.Count == 0 ? "" : "Diagnostics faits : " + string.Join(", ", history.Select(f => Path.GetFileNameWithoutExtension(f).Replace("diagnostic-", "")));
        RedoButton.IsEnabled = history.Count > 0;
    }

    /// <summary>Recopie le formulaire dans le diagnostic en cours.</summary>
    private void Gather()
    {
        _diagnostic.Company = CompanyBox.Text.Trim();
        _diagnostic.ContactName = ContactBox.Text.Trim();
        _diagnostic.Activity = ActivityBox.Text.Trim();
        _diagnostic.Workstations = int.TryParse(WorkstationsBox.Text.Trim(), out var n) ? n : 0;
        _diagnostic.Date = DateBox.SelectedDate ?? DateTime.Today;
        _diagnostic.Notes = NotesBox.Text.Trim();
        foreach (var theme in Referentiel.Themes)
        {
            _diagnostic.Levels[theme.Id] = Math.Max(0, _levelButtons[theme.Id].FindIndex(b => b.IsChecked == true));
            _diagnostic.Remarks[theme.Id] = _remarkBoxes[theme.Id].Text.Trim();
        }
    }

    private void LevelChanged()
    {
        if (_loading) return;
        Gather();
        RefreshScore();
    }

    private void Field_Changed(object sender, TextChangedEventArgs e) => StatusText.Text = "";

    private void RefreshScore()
    {
        var score = _diagnostic.Score;
        ScoreText.Text = score.ToString();
        ScoreCircle.Background = (Brush)FindResource(score >= 80 ? "OkBrush" : score >= 55 ? "InfoBrush" : score >= 30 ? "WarningBrush" : "DangerBrush");
        GradeText.Text = $"{CyberDiagnostic.Grade(score)} · {score}/100";
        CompareText.Text = _previous is null ? "Note calculée sur les 9 thèmes, les thèmes essentiels comptent double."
            : $"{(score - _previous.Score >= 0 ? "+" : "")}{score - _previous.Score} points depuis le diagnostic du {_previous.Date:dd/MM/yyyy} ({_previous.Score}/100)";

        ThreatPanel.Children.Clear();
        foreach (var threat in Referentiel.Threats)
        {
            var exposure = _diagnostic.ExposureOf(threat);
            var dot = exposure switch { Exposure.Exposed => "#F87171", Exposure.Partial => "#FBBF24", _ => "#4ADE80" };
            var text = new TextBlock { Foreground = Brushes.White, ToolTip = threat.Example };
            text.Inlines.Add(new System.Windows.Documents.Run("● ") { Foreground = (Brush)new BrushConverter().ConvertFrom(dot)! });
            text.Inlines.Add(new System.Windows.Documents.Run(threat.Name));
            ThreatPanel.Children.Add(new Border { Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 8, 6), Child = text });
        }
    }

    private void RefreshPostes()
    {
        var postes = _diagnostic.Postes;
        PostesText.Text = postes.Count == 0 ? "Aucun poste importé depuis PC Sécurité."
            : $"{postes.Count} poste(s) importé(s) : {string.Join(", ", postes.Select(p => p.Name))}";
        foreach (var theme in Referentiel.Themes)
        {
            var text = PosteResult.Finding(theme, postes);
            if (text is not null && PosteResult.Ceiling(theme, postes) is { } ceiling && _diagnostic.Level(theme.Id) > ceiling)
                text += $"  → les postes ne justifient pas plus que le niveau {ceiling}.";
            _findingTexts[theme.Id].Text = text ?? "";
            _findingTexts[theme.Id].Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // ---------- Entreprises ----------

    private void RefreshCompanies(string? select = null)
    {
        try
        {
            var companies = CyberDiagnostic.Companies();
            CompanyList.ItemsSource = companies;
            if (select is not null) CompanyList.SelectedItem = companies.FirstOrDefault(c => c == select);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Impossible de lire les diagnostics : " + ex.Message;
        }
    }

    private void CompanyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CompanyList.SelectedItem is not string company) return;
        var latest = CyberDiagnostic.History(company).FirstOrDefault();
        if (latest is null) return;
        try { Display(CyberDiagnostic.Load(latest)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Impossible d'ouvrir le diagnostic : " + ex.Message;
        }
    }

    private void NewCompany_Click(object sender, RoutedEventArgs e)
    {
        CompanyList.SelectedItem = null;
        Display(new CyberDiagnostic());
        CompanyBox.Focus();
    }

    /// <summary>Nouveau diagnostic d'une entreprise déjà vue : mêmes réponses au départ, date du jour, postes à réimporter.</summary>
    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        Gather();
        var copy = new CyberDiagnostic
        {
            Company = _diagnostic.Company,
            ContactName = _diagnostic.ContactName,
            Activity = _diagnostic.Activity,
            Workstations = _diagnostic.Workstations,
            Date = DateTime.Today,
        };
        foreach (var pair in _diagnostic.Levels) copy.Levels[pair.Key] = pair.Value;
        Display(copy);
        StatusText.Text = "Nouveau diagnostic à la date du jour : revoir chaque thème et réimporter les postes.";
    }

    private bool Save()
    {
        Gather();
        if (_diagnostic.Company.Length == 0)
        {
            MessageBox.Show(this, "Indiquer le nom de l'entreprise.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            CompanyBox.Focus();
            return false;
        }
        try
        {
            _diagnostic.Save();
            StatusText.Text = "Enregistré : " + _diagnostic.FilePath;
            RefreshCompanies(_diagnostic.Company);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Impossible d'enregistrer : " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    // ---------- Postes, tarifs, rapport ----------

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Résultats PC Sécurité des postes",
            Filter = "Résultats PC Sécurité (*.jvsec)|*.jvsec",
            Multiselect = true,
            InitialDirectory = AppPaths.EnsureFolder(Path.Combine(CyberDiagnostic.Root, "Postes")),
        };
        if (dialog.ShowDialog(this) != true) return;
        Gather();
        var errors = new List<string>();
        foreach (var file in dialog.FileNames)
        {
            try
            {
                var poste = PosteResult.Import(file);
                _diagnostic.Postes.RemoveAll(p => p.Name.Equals(poste.Name, StringComparison.OrdinalIgnoreCase));
                _diagnostic.Postes.Add(poste);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                errors.Add($"{Path.GetFileName(file)} : {ex.Message}");
            }
        }
        if (_diagnostic.Workstations < _diagnostic.Postes.Count) WorkstationsBox.Text = _diagnostic.Postes.Count.ToString();
        RefreshPostes();
        StatusText.Text = errors.Count == 0 ? $"{dialog.FileNames.Length} poste(s) importé(s)." : string.Join(" · ", errors);
    }

    private void Tarifs_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Tarifs.EnsureFile()}\"") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            StatusText.Text = "Impossible d'ouvrir les tarifs : " + ex.Message;
        }
    }

    private async void Report_Click(object sender, RoutedEventArgs e)
    {
        if (!Save()) return;
        var dialog = new SaveFileDialog
        {
            Title = "Rapport du diagnostic cyber",
            Filter = "PDF (*.pdf)|*.pdf|Page web (*.html)|*.html",
            FileName = CyberDiagnostic.Safe($"Diagnostic cyber - {_diagnostic.Company} - {_diagnostic.Date:yyyy-MM-dd}"),
            InitialDirectory = _diagnostic.Folder,
        };
        if (dialog.ShowDialog(this) != true) return;
        var path = dialog.FileName;
        var diagnostic = _diagnostic;
        var info = ReportInfo.LoadDefaults();
        StatusText.Text = "Création du rapport…";
        try
        {
            await Task.Run(() => CyberReport.Write(path, diagnostic, info));
            StatusText.Text = "Rapport enregistré : " + path;
            Open(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "";
            MessageBox.Show(this, "Impossible de créer le rapport : " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Open(AppPaths.EnsureFolder(CyberDiagnostic.Root));

    private void UpdateLink_Click(object sender, RoutedEventArgs e) => Open(_updateUrl);

    private void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Win32Exception ex) { StatusText.Text = "Ouverture impossible : " + ex.Message; }
    }
}
