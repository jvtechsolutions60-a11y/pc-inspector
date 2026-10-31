using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PcInspector.Report;

namespace PcInspector;

/// <summary>Titre et descriptif d'annonce composés à partir des composants relevés. Lecture seule sur le PC.</summary>
public partial class ListingWindow : Window
{
    private static string NotesPath => Path.Combine(AppPaths.SettingsFolder, "annonce-remarques.txt");

    private ListingSpecs? _specs;
    private bool _titleEdited;

    public ListingWindow()
    {
        InitializeComponent();
        try { if (File.Exists(NotesPath)) NotesBox.Text = File.ReadAllText(NotesPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await ReadAsync();

    private async Task ReadAsync()
    {
        StatusText.Text = "Lecture des composants…";
        _specs = await Task.Run(() => ListingSpecs.Collect());
        _titleEdited = false;
        Refresh();
        StatusText.Text = "Vérifier le texte avant de le publier.";
    }

    private void Refresh()
    {
        if (_specs is null) return;
        if (!_titleEdited) TitleBox.Text = _specs.Title();
        DescriptionBox.Text = _specs.Description(Extras(), TitleBox.Text);
    }

    private ListingExtras Extras() => new()
    {
        Case = CaseBox.Text,
        PowerSupply = PsuBox.Text,
        Cooler = CoolerBox.Text,
        Condition = ConditionBox.Text,
        Notes = NotesBox.Text,
    };

    private void Field_Changed(object sender, RoutedEventArgs e) => Refresh();

    // Le texte de la liste n'est à jour qu'après la sélection.
    private void Condition_Changed(object sender, SelectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(Refresh));

    private void Title_Changed(object sender, TextChangedEventArgs e)
    {
        if (_specs is not null && TitleBox.IsKeyboardFocusWithin) _titleEdited = true;
        // Leboncoin coupe les titres à 100 caractères.
        TitleLength.Text = $"{TitleBox.Text.Length} caractères" + (TitleBox.Text.Length > 100 ? " · trop long pour Leboncoin (100 max)" : "");
        if (_titleEdited) Refresh();
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await ReadAsync();

    private void CopyTitle_Click(object sender, RoutedEventArgs e) => Copy(TitleBox.Text, "Titre copié.");

    private void CopyDescription_Click(object sender, RoutedEventArgs e)
    {
        Copy(DescriptionBox.Text, "Descriptif copié : le coller dans l'annonce.");
        try
        {
            AppPaths.EnsureFolder(AppPaths.SettingsFolder);
            File.WriteAllText(NotesPath, NotesBox.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async void Pdf_Click(object sender, RoutedEventArgs e)
    {
        if (_specs is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Fiche technique de l'annonce",
            Filter = "PDF (*.pdf)|*.pdf|Page web (*.html)|*.html",
            FileName = "Fiche technique " + string.Concat(TitleBox.Text.Split(Path.GetInvalidFileNameChars())).Replace("·", "-"),
            InitialDirectory = AppPaths.EnsureFolder(AppPaths.ReportsRoot),
        };
        if (dialog.ShowDialog(this) != true) return;
        var path = dialog.FileName;
        var html = _specs.Html(Extras(), TitleBox.Text, ReportInfo.LoadDefaults());
        StatusText.Text = "Création du PDF…";
        try
        {
            await Task.Run(() =>
            {
                if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) PdfPrinter.FromHtml(html, path);
                else File.WriteAllText(path, html, new System.Text.UTF8Encoding(true));
            });
            StatusText.Text = "Fiche enregistrée : " + path;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            StatusText.Text = "";
            MessageBox.Show(this, "Impossible de créer la fiche : " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Copy(string text, string done)
    {
        try
        {
            Clipboard.SetText(text);
            StatusText.Text = done;
        }
        catch (COMException)
        {
            StatusText.Text = "Le presse-papiers est occupé : réessayer.";
        }
    }
}
