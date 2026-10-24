using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JvtechServeur.Pxe;
using Microsoft.Win32;
using PcInspector;

namespace JvtechServeur;

public partial class MainWindow : Window
{
    private sealed record AddressItem(IPAddress Address, string Label);

    private static readonly TimeSpan AcceptDuration = TimeSpan.FromMinutes(30);

    private readonly ServerConfig _config = ServerConfig.Load();
    private readonly Postes _postes;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(20) };
    private ProxyDhcp? _dhcp;
    private TftpServer? _tftp;
    private StatusServer? _status;
    private DateTime _acceptUntil = DateTime.MinValue;
    private DateTime _lastMinute = DateTime.Now;
    private ImageUpdater.Update? _pendingUpdate;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        _postes = new Postes(Dispatcher);
        Title = $"JVTECH Serveur PXE {AppInfo.Version}";
        PosteList.ItemsSource = _postes.Items;
        _postes.Items.CollectionChanged += (_, _) => EmptyText.Visibility = _postes.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _timer.Tick += (_, _) => Tick();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AutoUpdateBox.IsChecked = _config.AutoUpdate;
        var addresses = ServerConfig.Addresses().Select(a => new AddressItem(a.Address, $"{a.Address}  ({a.Name})")).ToList();
        AddressBox.ItemsSource = addresses;
        AddressBox.SelectedItem = addresses.FirstOrDefault(a => a.Address.ToString() == _config.ServerAddress) ?? addresses.FirstOrDefault();

        try
        {
            _status = new StatusServer(_postes, Log);
            _status.Start();
        }
        catch (HttpListenerException ex)
        {
            Log($"Suivi des postes indisponible (port {ServerConfig.StatusPort}) : {ex.Message}");
        }

        RefreshImage();
        RefreshStats();
        if (_config.ImageReady) StartServer();
        _timer.Start();
        _ = CheckUpdateAsync(automatic: true);
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_busy && MessageBox.Show(this, "Une préparation de l'image est en cours. Fermer quand même ? Elle sera à refaire.",
                "JVTECH Serveur PXE", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        StopServer();
        _status?.Dispose();
    }

    // ---------- Serveur ----------

    private void ServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dhcp is null) StartServer();
        else StopServer();
    }

    private void StartServer()
    {
        if (!_config.ImageReady)
        {
            MessageBox.Show(this, "Préparez d'abord le serveur avec une ISO de Windows 11.", "JVTECH Serveur PXE", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (AddressBox.SelectedItem is not AddressItem item)
        {
            MessageBox.Show(this, "Aucune carte réseau active : branchez le câble réseau du mini PC.", "JVTECH Serveur PXE", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            _dhcp = new ProxyDhcp(item.Address, Accept, Log, (mac, type) =>
            {
                Log($"Démarrage réseau accordé à {mac} ({type}).");
                _postes.Booting(mac);
            });
            _dhcp.Start();
            _tftp = new TftpServer(item.Address, ServerConfig.TftpFolder, Log, (client, file) => Log($"{client} a chargé {file}."));
            _tftp.Start();
        }
        catch (SocketException ex)
        {
            StopServer();
            Log("Impossible de démarrer : " + ex.Message);
            MessageBox.Show(this, "Les ports du démarrage réseau (67, 69, 4011) sont déjà utilisés par un autre logiciel sur ce PC "
                + "(serveur DHCP, partage de connexion, autre serveur PXE). Fermez-le puis réessayez.\n\n" + ex.Message,
                "JVTECH Serveur PXE", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_config.ServerAddress != item.Address.ToString())
        {
            _config.ServerAddress = item.Address.ToString();
            _config.Save();
        }
        try { ImageBuilder.CopyKit(item.Address, Log); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log("Kit de préparation non mis à jour : " + ex.Message);
        }
        AddressBox.IsEnabled = false;
        Log($"Serveur démarré sur {item.Address}.");
        RefreshHeader();
    }

    private void StopServer()
    {
        _dhcp?.Dispose();
        _tftp?.Dispose();
        if (_dhcp is not null) Log("Serveur arrêté.");
        _dhcp = null;
        _tftp = null;
        _acceptUntil = DateTime.MinValue;
        AddressBox.IsEnabled = true;
        RefreshHeader();
    }

    /// <summary>
    /// Seuls les PC démarrés sur le réseau pendant la fenêtre « Accepter les PC » reçoivent l'ordre de démarrer :
    /// un PC de l'atelier qui démarrerait sur le réseau par hasard n'est jamais effacé.
    /// </summary>
    private bool Accept(string mac) => DateTime.Now < _acceptUntil;

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dhcp is null)
        {
            StartServer();
            if (_dhcp is null) return;
        }
        _acceptUntil = DateTime.Now < _acceptUntil ? DateTime.MinValue : DateTime.Now + AcceptDuration;
        Log(_acceptUntil > DateTime.Now ? "PC acceptés pendant 30 minutes." : "Les PC ne sont plus acceptés.");
        RefreshHeader();
    }

    private void RefreshHeader()
    {
        var running = _dhcp is not null;
        ServerButton.Content = running ? "Arrêter le serveur" : "Démarrer le serveur";
        // Pendant un travail sur l'image, la ligne d'état montre son avancement.
        if (!_busy)
            StatusText.Text = running ? $"Serveur démarré · {_config.ServerAddress}"
                : _config.ImageReady ? "Serveur arrêté" : "Serveur à préparer avec une ISO de Windows 11";
        var accepting = running && DateTime.Now < _acceptUntil;
        AcceptButton.Content = accepting ? "Ne plus accepter" : "Accepter les PC (30 min)";
        AcceptButton.IsEnabled = !_busy && _config.ImageReady;
        ServerButton.IsEnabled = !_busy;
        AcceptText.Text = accepting
            ? $"PC acceptés jusqu'à {_acceptUntil:HH:mm} : tout PC démarré sur le réseau sera EFFACÉ et réinstallé."
            : "Les PC ne sont pas acceptés : aucun PC ne peut être effacé par erreur.";
    }

    private void Tick()
    {
        RefreshHeader();
        if (DateTime.Now - _lastMinute >= TimeSpan.FromMinutes(1))
        {
            _lastMinute = DateTime.Now;
            _postes.Tick();
            RefreshStats();
        }
        // Vérification une fois par jour, intégration automatique quand l'atelier est calme.
        if (!_busy && (_config.LastCheck is null || DateTime.Now - _config.LastCheck > TimeSpan.FromHours(20)))
            _ = CheckUpdateAsync(automatic: true);
        else if (!_busy && _pendingUpdate is not null && _config.AutoUpdate && Idle())
            _ = ApplyUpdateAsync(_pendingUpdate);
    }

    /// <summary>Aucun PC en cours et pas de fenêtre d'acceptation : l'image peut être modifiée sans gêner une installation.</summary>
    private bool Idle() => DateTime.Now >= _acceptUntil && !_postes.Items.Any(p => p.Finished is null && p.State == "En cours");

    // ---------- Image ----------

    private void RefreshImage()
    {
        if (!_config.ImageReady)
        {
            ImageText.Text = "Pas encore préparée.";
            ImageHint.Text = "Téléchargez l'ISO de Windows 11 sur le site de Microsoft, puis « Préparer depuis une ISO ». Compter 30 à 45 minutes.";
        }
        else
        {
            ImageText.Text = _config.LastUpdate.Length > 0
                ? $"Prête, à jour avec {_config.LastUpdate} (intégrée le {_config.LastUpdateDate:dd/MM/yyyy})."
                : "Prête, version de l'ISO (sans mise à jour intégrée).";
            ImageHint.Text = _config.LastCheck is { } check ? $"Dernière vérification des mises à jour : {check:dd/MM/yyyy HH:mm}." : "";
        }
        PrepareButton.IsEnabled = CheckButton.IsEnabled = !_busy;
        CheckButton.IsEnabled &= _config.ImageReady;
        RefreshHeader();
    }

    private async void Prepare_Click(object sender, RoutedEventArgs e)
    {
        if (AddressBox.SelectedItem is not AddressItem item)
        {
            MessageBox.Show(this, "Aucune carte réseau active : branchez le câble réseau du mini PC.", "JVTECH Serveur PXE", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dialog = new OpenFileDialog { Title = "ISO de Windows 11", Filter = "Image ISO (*.iso)|*.iso" };
        if (dialog.ShowDialog(this) != true) return;

        StopServer();
        _busy = true;
        RefreshImage();
        var server = item.Address;
        var error = await Task.Run(() => ImageBuilder.Prepare(dialog.FileName, server, _config, Log, Status));
        _busy = false;
        RefreshImage();
        if (error is not null)
        {
            Log("Échec : " + error);
            MessageBox.Show(this, error, "JVTECH Serveur PXE", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Log("Serveur prêt : les PC peuvent démarrer sur le réseau.");
        StartServer();
        _ = CheckUpdateAsync(automatic: false);
    }

    private void Check_Click(object sender, RoutedEventArgs e) => _ = CheckUpdateAsync(automatic: false);

    private async Task CheckUpdateAsync(bool automatic)
    {
        if (_busy || !_config.ImageReady) return;
        _busy = true;
        RefreshImage();
        Status("Recherche de la mise à jour du mois…");
        var latest = await Task.Run(() => ImageUpdater.Latest(Log));
        _config.LastCheck = DateTime.Now;
        _config.Save();
        _busy = false;
        RefreshImage();

        _pendingUpdate = latest is not null && latest.Kb != _config.LastUpdate ? latest : null;
        UpdateBanner.Visibility = _pendingUpdate is null ? Visibility.Collapsed : Visibility.Visible;
        if (_pendingUpdate is not null)
        {
            UpdateText.Text = $"Nouvelle mise à jour de Windows 11 : {_pendingUpdate.Title}. L'intégrer à l'image fait gagner 20 à 40 minutes par PC (1 à 2 heures de travail pour le serveur).";
            Log("Mise à jour disponible : " + _pendingUpdate.Title);
            if (_config.AutoUpdate && Idle()) await ApplyUpdateAsync(_pendingUpdate);
        }
        else if (latest is not null) Log($"Image à jour ({latest.Kb}).");
        if (!automatic && latest is not null && _pendingUpdate is null)
            MessageBox.Show(this, $"L'image est déjà à jour ({latest.Kb}).", "JVTECH Serveur PXE", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ApplyUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null || _busy) return;
        if (!Idle() && MessageBox.Show(this, "Des PC sont en cours d'installation. Intégrer la mise à jour maintenant peut faire échouer leur installation. Continuer ?",
                "JVTECH Serveur PXE", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _ = ApplyUpdateAsync(_pendingUpdate);
    }

    private async Task ApplyUpdateAsync(ImageUpdater.Update update)
    {
        if (_busy) return;
        // Pas d'installation pendant que l'image est modifiée.
        var wasRunning = _dhcp is not null;
        StopServer();
        _busy = true;
        RefreshImage();
        UpdateBanner.Visibility = Visibility.Collapsed;
        var error = await Task.Run(() => ImageUpdater.Apply(update, _config, Log, Status));
        _busy = false;
        if (error is null) _pendingUpdate = null;
        else
        {
            Log("Échec de la mise à jour de l'image : " + error);
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateText.Text = $"La mise à jour {update.Kb} n'a pas pu être intégrée : {error}";
            // Pas de nouvel essai automatique avant la prochaine vérification.
            _pendingUpdate = null;
        }
        RefreshImage();
        if (wasRunning) StartServer();
    }

    private void AutoUpdate_Click(object sender, RoutedEventArgs e)
    {
        _config.AutoUpdate = AutoUpdateBox.IsChecked == true;
        _config.Save();
    }

    // ---------- Postes et statistiques ----------

    private void RefreshStats()
    {
        var stats = Postes.ReadStats();
        StatsPanel.Children.Clear();
        void Chip(string text)
        {
            var border = new Border { Style = (Style)FindResource("Chip") };
            border.Child = new TextBlock { Text = text, Foreground = System.Windows.Media.Brushes.White, FontSize = 12 };
            StatsPanel.Children.Add(border);
        }
        Chip($"{stats.ThisMonth} PC ce mois-ci ({stats.LastMonth} le mois dernier)");
        if (stats.AverageMinutes > 0) Chip($"{stats.AverageMinutes / 60} h {stats.AverageMinutes % 60:00} en moyenne");
        if (stats.ToCheck > 0) Chip($"{stats.ToCheck} à vérifier");
        foreach (var (model, count) in stats.TopModels) Chip($"{model} × {count}");
    }

    private void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Poste poste) return;
        if (poste.ReportPath is { } path && File.Exists(path)) Open(path);
        else MessageBox.Show(this, "Le rapport arrive à la fin de la préparation.", "JVTECH Serveur PXE", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Reports_Click(object sender, RoutedEventArgs e) => Open(AppPaths.EnsureFolder(ServerConfig.ReportsFolder));

    private void ClearFinished_Click(object sender, RoutedEventArgs e)
    {
        _postes.ClearFinished();
        RefreshStats();
    }

    private static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
    }

    // ---------- Journal ----------

    private void Status(string text) => Dispatcher.InvokeAsync(() =>
    {
        StatusText.Text = text;
        Log(text);
    });

    private void Log(string line)
    {
        var stamped = $"{DateTime.Now:HH:mm:ss}  {line}";
        try { File.AppendAllText(Path.Combine(AppPaths.EnsureFolder(ServerConfig.Root), "journal.log"), $"{DateTime.Now:yyyy-MM-dd} {stamped}\r\n"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Dispatcher.InvokeAsync(() =>
        {
            LogBox.AppendText(stamped + "\r\n");
            if (LogBox.LineCount > 3000) LogBox.Text = LogBox.Text.Substring(LogBox.Text.Length / 2);
            LogBox.ScrollToEnd();
        });
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(LogBox.Text); }
        catch (System.Runtime.InteropServices.COMException) { }
    }
}
