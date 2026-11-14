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
    private sealed record AddressItem(IPAddress Address, IPAddress Mask, string Label);
    /// <summary>Carte réseau du serveur, lue par le suivi des postes (autre fil) pour n'accepter que le réseau local.</summary>
    private volatile Tuple<IPAddress?, IPAddress?> _statusNetwork = Tuple.Create<IPAddress?, IPAddress?>(null, null);
    /// <summary>Marge après la fin de « Accepter les PC » pour qu'un PC accepté finisse de charger Windows PE.</summary>
    private static readonly TimeSpan TftpGrace = TimeSpan.FromMinutes(15);

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
        var addresses = ServerConfig.Addresses().Select(a => new AddressItem(a.Address, a.Mask, $"{a.Address}  ({a.Name})")).ToList();
        AddressBox.ItemsSource = addresses;
        AddressBox.SelectedItem = addresses.FirstOrDefault(a => a.Address.ToString() == _config.ServerAddress) ?? addresses.FirstOrDefault();

        ImageBuilder.ProtectRoot(Log);
        if (AddressBox.SelectedItem is AddressItem selected) _statusNetwork = Tuple.Create<IPAddress?, IPAddress?>(selected.Address, selected.Mask);
        try
        {
            _status = new StatusServer(_postes, Log, _config.ReportToken, () => (_statusNetwork.Item1, _statusNetwork.Item2));
            _status.Start();
        }
        catch (HttpListenerException ex)
        {
            Log($"Suivi des postes indisponible (port {ServerConfig.StatusPort}) : {ex.Message}");
        }

        if (_config.SecretsLost)
        {
            Log("ALERTE : mot de passe du partage ou jeton du suivi illisible sur ce PC (serveur.ini copié depuis un autre PC ou abîmé). Refaire « Préparer depuis une ISO » : un nouveau mot de passe et un nouveau jeton seront créés.");
            MessageBox.Show(this, "Les secrets du serveur (mot de passe du partage, jeton du suivi) ne peuvent pas être lus sur ce PC.\n\n" +
                "Refaites « Préparer depuis une ISO » : de nouveaux secrets seront créés et Windows PE sera refait avec eux.",
                "JVTECH Serveur PXE", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            _tftp = new TftpServer(item.Address, ServerConfig.TftpFolder, Log, (client, file) => Log($"{client} a chargé {file}."),
                () => DateTime.Now < _acceptUntil + TftpGrace);
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
        _statusNetwork = Tuple.Create<IPAddress?, IPAddress?>(item.Address, item.Mask);
        try { ImageBuilder.CopyKit(item.Address, _config.ReportToken, Log); }
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
        lock (_approved) { _approved.Clear(); _asked.Clear(); }
        if (Idle()) SetShareAccount(false);
        AddressBox.IsEnabled = true;
        RefreshHeader();
    }

    /// <summary>
    /// Seuls les PC démarrés sur le réseau pendant la fenêtre « Accepter les PC » ET autorisés un par un par le
    /// technicien (adresse MAC, pour cette fenêtre seulement) reçoivent l'ordre de démarrer (audit I11). Un PC de
    /// l'atelier qui démarrerait sur le réseau par hasard n'est jamais effacé ; sur le PC, EFFACER doit encore être tapé.
    /// </summary>
    private bool Accept(string mac)
    {
        if (DateTime.Now >= _acceptUntil) return false;
        lock (_approved)
        {
            if (_approved.Contains(mac)) return true;
            if (!_asked.Add(mac)) return false;
        }
        Dispatcher.BeginInvoke(new Action(() => AskApproval(mac)));
        return false;
    }

    private readonly HashSet<string> _approved = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _asked = new(StringComparer.OrdinalIgnoreCase);

    private void AskApproval(string mac)
    {
        Log($"{mac} demande à démarrer sur le réseau : en attente de votre accord.");
        var yes = MessageBox.Show(this,
            $"Un PC demande à démarrer sur le serveur.\n\nAdresse MAC : {mac}\n\n" +
            "Vérifiez que c'est bien le PC à préparer (étiquette sous le PC, ou adresse affichée à l'écran du démarrage réseau). " +
            "Autoriser ce PC ? (sur le PC, il faudra encore taper EFFACER)",
            "JVTECH Serveur PXE", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        lock (_approved)
        {
            if (yes && DateTime.Now < _acceptUntil) _approved.Add(mac);
            else _asked.Remove(mac); // refusé : redemandé s'il redémarre
        }
        Log(yes ? $"{mac} autorisé : il démarrera au prochain essai (relancer le démarrage réseau s'il a abandonné)." : $"{mac} refusé.");
    }

    /// <summary>
    /// Compte du partage (dont le mot de passe est dans Windows PE) actif seulement quand on prépare des PC (audit I08) :
    /// activé avec « Accepter les PC », désactivé quand plus rien n'est en cours.
    /// </summary>
    private void SetShareAccount(bool active)
    {
        if (_shareActive == active) return;
        var (code, _) = Tools.Run(Tools.System32("net.exe"), $"user {ServerConfig.ShareUser} /active:{(active ? "yes" : "no")}", null, TimeSpan.FromSeconds(30));
        if (code == 0) _shareActive = active;
        Log(code == 0 ? $"Compte du partage {(active ? "activé" : "désactivé")}." : $"Compte du partage : changement impossible (code {code}).");
    }

    private bool? _shareActive;

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dhcp is null)
        {
            StartServer();
            if (_dhcp is null) return;
        }
        _acceptUntil = DateTime.Now < _acceptUntil ? DateTime.MinValue : DateTime.Now + AcceptDuration;
        lock (_approved) { _approved.Clear(); _asked.Clear(); }
        if (_acceptUntil > DateTime.Now) SetShareAccount(true);
        Log(_acceptUntil > DateTime.Now ? "PC acceptés pendant 30 minutes, après votre accord pour chacun." : "Les PC ne sont plus acceptés.");
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
            ? $"PC acceptés jusqu'à {_acceptUntil:HH:mm}, un par un après votre accord : un PC autorisé, sur lequel on tape EFFACER, sera effacé et réinstallé."
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
            // Plus rien en cours (fenêtre fermée, aucun PC en installation) : compte du partage désactivé.
            if (Idle()) SetShareAccount(false);
            else if (_postes.Items.Any(p => p.Finished is null && p.State == "En cours")) SetShareAccount(true);
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
