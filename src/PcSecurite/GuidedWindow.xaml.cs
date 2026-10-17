using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using PcSecurite.Checks;

namespace PcSecurite;

/// <summary>
/// Présente les points à corriger un par un. Le technicien fait la correction lui-même ;
/// « Vérifier » refait seulement le contrôle, sans rien modifier.
/// </summary>
public partial class GuidedWindow : Window
{
    private readonly MainWindow _main;
    private readonly List<string> _names;
    private int _index;

    public GuidedWindow(MainWindow main, List<string> names)
    {
        InitializeComponent();
        _main = main;
        _names = names;
        Progress.Maximum = names.Count;
        ShowPoint(0);
    }

    private SecurityCheck? Check => _main.Current(_names[_index]);

    private void ShowPoint(int index)
    {
        _index = index;
        ResultText.Text = "";
        Refresh();
    }

    private void Refresh()
    {
        var check = Check;
        if (check is null) return;
        var done = _names.Count(n => _main.Current(n) is { NeedsAction: false });
        PositionText.Text = $"Point {_index + 1} sur {_names.Count} · {done} corrigé(s)";
        Progress.Value = done;

        StatusDot.Fill = (Brush)FindResource(check.Status switch
        {
            CheckStatus.Ok => "OkBrush",
            CheckStatus.Warning => "WarningBrush",
            CheckStatus.Danger => "DangerBrush",
            _ => "InfoBrush",
        });
        NameText.Text = check.Name;
        DetailText.Text = check.Detail;
        AdviceText.Text = check.Advice ?? "";
        AdviceText.Visibility = check.Advice is null ? Visibility.Collapsed : Visibility.Visible;

        // Une fois corrigé, le contrôle n'a plus de tutoriel : on garde celui du point d'origine à l'écran.
        if (check.Tutorial is not null) _tutorials[check.Name] = check.Tutorial;
        _tutorials.TryGetValue(check.Name, out var tutorial);
        StepsText.Text = tutorial?.Text ?? "Pas de tutoriel pour ce point : suivre le conseil ci-dessus.";
        OpenButton.Content = tutorial?.OpenLabel;
        OpenButton.Tag = tutorial;
        OpenButton.Visibility = tutorial?.OpenLabel is null ? Visibility.Collapsed : Visibility.Visible;

        PreviousButton.IsEnabled = _index > 0;
        NextButton.Content = _index < _names.Count - 1 ? "Suivant ▶" : "Terminer";
    }

    private readonly Dictionary<string, Tutorial> _tutorials = new();

    private void OpenButton_Click(object sender, RoutedEventArgs e) => MainWindow.OpenSetting(this, OpenButton.Tag as Tutorial);

    private async void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        ResultText.Text = "Vérification en cours…";
        var fresh = await _main.RecheckAsync(_names[_index]);
        CheckButton.IsEnabled = true;
        if (fresh is null) return;
        Refresh();
        ResultText.Foreground = (Brush)FindResource(fresh.NeedsAction ? "DangerBrush" : "OkBrush");
        ResultText.Text = fresh.NeedsAction
            ? "Toujours à corriger. Certains réglages demandent un redémarrage avant d'être pris en compte."
            : "✓ Corrigé : ce point est maintenant en ordre.";
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_index > 0) ShowPoint(_index - 1);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_index < _names.Count - 1) ShowPoint(_index + 1);
        else Close();
    }
}
