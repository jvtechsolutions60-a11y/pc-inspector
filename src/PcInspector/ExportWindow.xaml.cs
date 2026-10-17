using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using PcInspector.Report;

namespace PcInspector;

/// <summary>Demande l'en-tête du rapport (client, n° d'intervention…) puis l'emplacement du fichier.</summary>
public partial class ExportWindow : Window
{
    public ExportWindow()
    {
        InitializeComponent();
        var defaults = ReportInfo.LoadDefaults();
        CompanyBox.Text = defaults.Company;
        TechnicianBox.Text = defaults.Technician;
        LogoBox.Text = defaults.LogoPath;
        Loaded += (_, _) => ClientBox.Focus();
    }

    public ReportInfo? Info { get; private set; }

    /// <summary>Début du nom de fichier proposé (« Rapport » par défaut).</summary>
    public string FilePrefix { get; set; } = "Rapport";
    public string? FilePath { get; private set; }

    private void BrowseLogo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choisir le logo",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.svg",
        };
        if (dialog.ShowDialog(this) == true) LogoBox.Text = dialog.FileName;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var info = new ReportInfo
        {
            Company = CompanyBox.Text.Trim(),
            Technician = TechnicianBox.Text.Trim(),
            LogoPath = LogoBox.Text.Trim(),
            Client = ClientBox.Text.Trim(),
            JobNumber = JobBox.Text.Trim(),
            Notes = NotesBox.Text,
            Date = DateTime.Now,
        };

        var dialog = new SaveFileDialog
        {
            Title = "Enregistrer le rapport",
            Filter = "Page web (*.html)|*.html|Texte (*.txt)|*.txt",
            FileName = DefaultFileName(info),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;

        info.SaveDefaults();
        Info = info;
        FilePath = dialog.FileName;
        DialogResult = true;
    }

    private string DefaultFileName(ReportInfo info)
    {
        var parts = new[] { FilePrefix, info.JobNumber, info.Client, info.Date.ToString("yyyy-MM-dd") }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var name = string.Join("_", parts);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        return name;
    }
}
