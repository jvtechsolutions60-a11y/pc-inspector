using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using PcInspector;

namespace JvtechOutils;

/// <summary>Écran d'accueil : ouvre PC Inspector, PC Sécurité ou PC Maintenance, rangés dans le même dossier.</summary>
public partial class MainWindow : Window
{
    private string _updateUrl = Updater.ReleasesPage;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = "Lanceur " + AppInfo.Version;
        Title = "JVTECH Outils " + AppInfo.Version;
        PortableBadge.Visibility = AppPaths.IsPortable ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (await Updater.CheckAsync() is not { } update) return;
        _updateUrl = update.Url;
        UpdateText.Text = $"Nouvelle version {update.Tag} disponible (vous avez {AppInfo.Version}). ";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string exe) return;
        var path = Path.Combine(AppPaths.ExeFolder, exe);
        if (!File.Exists(path))
        {
            StatusText.Text = $"{exe} est introuvable : le mettre dans le même dossier que JvtechOutils.exe.";
            return;
        }
        try
        {
            // PC Inspector, PC Sécurité et PC Maintenance demandent les droits administrateur : Windows affiche sa confirmation.
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = AppPaths.ExeFolder });
            StatusText.Text = "";
        }
        catch (Win32Exception ex)
        {
            // 1223 : l'utilisateur a refusé la demande de droits administrateur.
            StatusText.Text = ex.NativeErrorCode == 1223 ? "Lancement annulé." : $"Impossible de lancer {exe} : {ex.Message}";
        }
    }

    private void ReportsLink_Click(object sender, RoutedEventArgs e) =>
        Open(AppPaths.EnsureFolder(AppPaths.ReportsRoot));

    private void UpdateLink_Click(object sender, RoutedEventArgs e) => Open(_updateUrl);

    private void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Win32Exception ex) { StatusText.Text = "Ouverture impossible : " + ex.Message; }
    }
}
