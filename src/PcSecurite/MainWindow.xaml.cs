using System;
using System.Collections.Generic;
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

/// <summary>
/// L'écran principal. PC Sécurité ne fait que lire et conseiller : aucun bouton ne modifie le PC,
/// les boutons « Ouvrir… » affichent seulement la page de réglages de Windows.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<SecurityCheck> _checks = new();
    private bool _running;

    // État de la première analyse, pour montrer ce qui a été corrigé pendant l'intervention.
    private int? _initialScore;
    private readonly List<string> _initiallyToDo = new();

    private string _updateUrl = Updater.ReleasesPage;

    public MainWindow()
    {
        InitializeComponent();
        Title = "PC Sécurité " + AppInfo.Version;
        if (AppPaths.IsPortable) PortableRun.Text = "  ·  mode clé USB";
        Loaded += async (_, _) =>
        {
            if (await Updater.CheckAsync() is not { } update) return;
            _updateUrl = update.Url;
            UpdateText.Text = $"  ·  nouvelle version {update.Tag} : télécharger";
        };
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
        SetButtons(false);
        _checks.Clear();
        ScoreText.Text = "…";
        ScoreCircle.Background = (Brush)FindResource("InfoBrush");
        ScoreBar.Foreground = (Brush)FindResource("AccentBrush");
        ScoreBar.Value = 0;
        VerdictText.Text = "Analyse en cours…";
        UpdateCounters();

        SecurityChecks.ResetCache();
        var all = SecurityChecks.All;
        for (int i = 0; i < all.Count; i++)
        {
            ProgressText.Text = $"Contrôle {i + 1} sur {all.Count} : {all[i].Name}…";
            _checks.Add(await RunCheckAsync(i));
            ScoreBar.Value = (i + 1) * 100.0 / all.Count;
            UpdateCounters();
        }

        if (_initialScore is null)
        {
            _initialScore = SecurityScore.Compute(_checks);
            _initiallyToDo.AddRange(_checks.Where(c => c.NeedsAction).Select(c => c.Name));
        }
        UpdateSummary();
        SetButtons(true);
        _running = false;
    }

    private static async Task<SecurityCheck> RunCheckAsync(int index)
    {
        var (name, run) = SecurityChecks.All[index];
        var check = await Task.Run(() => SecurityChecks.RunSafely(name, run));
        check.Order = index;
        if (check.NeedsAction) check.Tutorial = Tutorials.For(check);
        return check;
    }

    /// <summary>Le point tel qu'il est affiché maintenant (il change après une vérification).</summary>
    public SecurityCheck? Current(string name) => _checks.FirstOrDefault(c => c.Name == name);

    /// <summary>Refait un seul contrôle, en lecture seule, après que le technicien a corrigé le point.</summary>
    public async Task<SecurityCheck?> RecheckAsync(string name)
    {
        var old = Current(name);
        if (old is null) return null;
        SecurityChecks.ResetCache();
        var fresh = await RunCheckAsync(old.Order);
        _checks[_checks.IndexOf(old)] = fresh;
        UpdateSummary();
        return fresh;
    }

    /// <summary>Points rouges ou orange à la première analyse, en ordre maintenant.</summary>
    private List<string> Corrected() =>
        _initiallyToDo.Where(n => Current(n) is { NeedsAction: false }).ToList();

    private void UpdateSummary()
    {
        var score = SecurityScore.Compute(_checks);
        var letter = SecurityScore.Letter(score);
        ScoreText.Text = letter.ToString();
        ScoreCircle.Background = ScoreBar.Foreground = GradeBrushes.For(letter);
        ScoreBar.Value = score;
        UpdateCounters();
        VerdictText.Text = _initialScore is { } before && before != score
            ? $"{SecurityScore.Verdict(score)} · {before} → {score} / 100"
            : $"{SecurityScore.Verdict(score)} · {score} / 100";

        var toFix = _checks.Count(c => c.GroupOrder == 0);
        var toChange = _checks.Count(c => c.GroupOrder == 1);
        var corrected = Corrected().Count;
        ProgressText.Text = (toFix + toChange == 0
            ? "Aucun point à corriger."
            : $"{toFix} correction(s) à faire, {toChange} chose(s) à changer.")
            + (corrected > 0 ? $" {corrected} point(s) corrigé(s) pendant l'intervention." : "");
        GuidedButton.IsEnabled = !_running && toFix + toChange > 0;
    }

    private void UpdateCounters()
    {
        DangerCount.Text = $" {_checks.Count(c => c.Status == CheckStatus.Danger)} urgent(s)";
        WarningCount.Text = $" {_checks.Count(c => c.Status == CheckStatus.Warning)} à améliorer";
        OkCount.Text = $" {_checks.Count(c => c.Status == CheckStatus.Ok)} en ordre";
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F5 && RescanButton.IsEnabled)
        {
            e.Handled = true;
            RescanButton_Click(sender, e);
        }
    }

    private void SetButtons(bool enabled)
    {
        RescanButton.IsEnabled = ExportButton.IsEnabled = ClientGuideButton.IsEnabled = CyberButton.IsEnabled = enabled;
        GuidedButton.IsEnabled = enabled && _checks.Any(c => c.NeedsAction);
    }

    private async void RescanButton_Click(object sender, RoutedEventArgs e) => await RunAsync();

    private void GuidedButton_Click(object sender, RoutedEventArgs e)
    {
        var names = _checks.Where(c => c.NeedsAction)
            .OrderBy(c => c.GroupOrder).ThenBy(c => c.Severity).ThenBy(c => c.Order)
            .Select(c => c.Name).ToList();
        if (names.Count == 0) return;
        new GuidedWindow(this, names) { Owner = this }.ShowDialog();
    }

    /// <summary>Ouvre la page de réglages de Windows indiquée par le tutoriel, sans rien y changer.</summary>
    private void OpenSetting_Click(object sender, RoutedEventArgs e) =>
        OpenSetting(this, (sender as FrameworkElement)?.Tag as Tutorial);

    public static void OpenSetting(Window owner, Tutorial? tutorial)
    {
        if (tutorial?.OpenFile is not { } file) return;
        try
        {
            Process.Start(new ProcessStartInfo(file) { Arguments = tutorial.OpenArgs ?? "", UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(owner, "Impossible d'ouvrir ce réglage : " + ex.Message, "PC Sécurité", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateLink_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_updateUrl) { UseShellExecute = true });

    private void ExportButton_Click(object sender, RoutedEventArgs e) =>
        Export("Rapport", (path, info) =>
            ReportWriter.Write(path, info, SecurityReport.ToSections(_checks.ToList(), _initialScore, Corrected())));

    private void ClientGuideButton_Click(object sender, RoutedEventArgs e) =>
        Export("Conseils securite", (path, info) => ClientGuide.Write(path, info, _checks.ToList()));

    /// <summary>
    /// Résultats de ce poste au format lu par JVTECH Diagnostic Cyber (.jvsec) : une ligne par contrôle,
    /// « Controle=Nom|Statut|Détail ». Rien n'est envoyé : le fichier est importé à la main.
    /// </summary>
    private void CyberButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Résultats pour le diagnostic cyber",
            Filter = "Résultats PC Sécurité (*.jvsec)|*.jvsec",
            FileName = Environment.MachineName + ".jvsec",
            InitialDirectory = AppPaths.EnsureFolder(System.IO.Path.Combine(AppPaths.ReportsRoot, "Diagnostic cyber", "Postes")),
        };
        if (dialog.ShowDialog(this) != true) return;
        var lines = new List<string>
        {
            "# PC Sécurité, résultats pour JVTECH Diagnostic Cyber",
            "Poste=" + Environment.MachineName,
            "Windows=" + WindowsName(),
            "DatePoste=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };
        lines.AddRange(_checks.Select(c => $"Controle={c.Name}|{c.Status}|{OneLine(c.Detail)}"));
        try
        {
            System.IO.File.WriteAllLines(dialog.FileName, lines, new System.Text.UTF8Encoding(true));
            MessageBox.Show(this, "Résultats enregistrés. Dans JVTECH Diagnostic Cyber, cliquer sur « Importer les postes ».",
                "PC Sécurité", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Impossible d'enregistrer le fichier : " + ex.Message, "PC Sécurité", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string OneLine(string? text) => (text ?? "").Replace("\r", "").Replace("\n", " ").Replace("|", "/").Trim();

    private static string WindowsName()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var name = key?.GetValue("ProductName") as string ?? "Windows";
            // ProductName dit encore « Windows 10 » sur Windows 11 : le numéro de build tranche.
            if (int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var build) && build >= 22000) name = name.Replace("Windows 10", "Windows 11");
            return $"{name} {key?.GetValue("DisplayVersion") as string}".Trim();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return "Windows";
        }
    }

    private void Export(string prefix, Action<string, ReportInfo> write)
    {
        var dialog = new ExportWindow { Owner = this, FilePrefix = prefix };
        if (dialog.ShowDialog() != true || dialog.Info is null || dialog.FilePath is null) return;
        try
        {
            write(dialog.FilePath, dialog.Info);
            Process.Start(new ProcessStartInfo(dialog.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Impossible d'enregistrer le fichier : " + ex.Message, "PC Sécurité",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
