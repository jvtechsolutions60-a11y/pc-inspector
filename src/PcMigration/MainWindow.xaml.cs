using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PcInspector;
using PcInspector.Report;
using PcMigration.Migration;

namespace PcMigration;

/// <summary>
/// Migration d'un client vers un nouveau PC : on sauvegarde l'ancien PC sur un disque externe, puis on restaure sur le
/// nouveau. Rien n'est supprimé chez le client, ni sur l'ancien PC ni sur le nouveau.
/// </summary>
public partial class MainWindow : Window
{
    private sealed class ItemRow
    {
        public ItemRow(CheckBox check, TextBlock status) { Check = check; Status = status; }
        public CheckBox Check { get; }
        public TextBlock Status { get; }
    }

    private readonly Dictionary<string, ItemRow> _backupRows = new();
    private readonly Dictionary<string, ItemRow> _restoreRows = new();
    private readonly Dictionary<string, ulong> _sizes = new();
    private List<UserProfile> _profiles = new();
    private string _updateUrl = Updater.ReleasesPage;
    private MigrationReport? _report;
    private string _reportClient = "";
    private bool _running;
    private bool _stopRequested;
    private int _sizeVersion;

    public MainWindow()
    {
        InitializeComponent();
        ComputerText.Text = Environment.MachineName;
        VersionText.Text = AppInfo.Version;
        Title = "PC Migration " + AppInfo.Version;
        PortableText.Visibility = AppPaths.IsPortable ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ClientBox.Text = ReportInfo.LoadDefaults().Client;
        _profiles = UserProfile.All();
        // Logiciel coupé net pendant une sauvegarde ou une restauration : on vide les fichiers secrets restés en clair.
        foreach (var profile in _profiles) Migration.Secrets.Cleanup(profile);
        var current = UserProfile.Interactive(_profiles);
        foreach (var combo in new[] { BackupProfileCombo, RestoreProfileCombo })
        {
            combo.ItemsSource = _profiles;
            combo.SelectedItem = current;
        }
        if (current is not null && !current.IsProcessUser)
            Log($"Le logiciel tourne avec un autre compte administrateur : le compte du client visé est « {current.Name} ».");
        RefreshDrives();
        RefreshSources();
        // Une sauvegarde trouvée sur un disque branché : on est sûrement sur le nouveau PC.
        if (SourceCombo.Items.Count > 0 && Layout.Find().FirstOrDefault() is { } latest
            && !string.Equals(latest.ReadManifest().TryGetValue("Ordinateur", out var pc) ? pc : "", Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            Tabs.SelectedIndex = 1;

        if (await Updater.CheckAsync() is not { } update) return;
        _updateUrl = update.Url;
        UpdateText.Text = $"Nouvelle version {update.Tag} : télécharger";
        UpdateArea.Visibility = Visibility.Visible;
    }

    // ===================== Sauvegarde =====================

    private UserProfile? BackupProfile => BackupProfileCombo.SelectedItem as UserProfile;

    private void BackupProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => BuildBackupItems();

    private void BuildBackupItems()
    {
        BackupItems.Children.Clear();
        _backupRows.Clear();
        _sizes.Clear();
        if (BackupProfile is not { } profile) return;

        foreach (var folder in profile.Folders())
            AddRow(BackupItems, _backupRows, Items.FolderPrefix + folder.Key, folder.Label, folder.Path + " — calcul de la taille…",
                Directory.Exists(folder.Path));
        var browsers = Browser.InstalledFor(profile);
        var names = browsers.Count == 0 ? "aucun navigateur trouvé" : string.Join(", ", browsers.Select(b => b.Name));
        AddRow(BackupItems, _backupRows, Items.Browsers, "Navigateurs : favoris, historique, extensions, onglets",
            $"{names}. Les connexions aux sites (cookies) sont à refaire sur le nouveau PC.", browsers.Count > 0);
        AddRow(BackupItems, _backupRows, Items.Passwords, "Mots de passe des navigateurs",
            "Export guidé de Chrome, Edge, Brave… (un clic par navigateur), chiffré aussitôt. Firefox : avec son profil.",
            browsers.Any(b => b.Chromium));
        AddRow(BackupItems, _backupRows, Items.Apps, "Applications (réinstallation automatique avec winget)",
            "Liste winget + liste complète des programmes ; ceux absents de winget sont à réinstaller à la main.", true);
        AddRow(BackupItems, _backupRows, Items.WiFi, "Réseaux Wi-Fi et leurs clés", "Clés chiffrées avec le mot de passe de la sauvegarde.", true);
        AddRow(BackupItems, _backupRows, Items.Outlook, "Outlook : signatures et fichiers .pst",
            "Les .pst rangés dans Documents partent avec Documents.", Directory.Exists(Path.Combine(profile.Roaming, @"Microsoft\Signatures"))
                                                                      || Directory.Exists(Path.Combine(profile.Local, @"Microsoft\Outlook")));
        AddRow(BackupItems, _backupRows, Items.Wallpaper, "Fond d'écran", "L'image du Bureau.", true);
        AddRow(BackupItems, _backupRows, Items.Printers, "Liste des imprimantes", "Pour les réinstaller avec le pilote du fabricant.", true);
        foreach (var row in _backupRows.Values) row.Check.Click += (_, _) => UpdateBackupTotal();
        _ = MeasureAsync(profile);
    }

    private static void AddRow(Panel panel, Dictionary<string, ItemRow> rows, string key, string label, string hint, bool isChecked)
    {
        var check = new CheckBox { Content = label, IsChecked = isChecked, FontSize = 14, Margin = new Thickness(0, 4, 0, 0) };
        var status = new TextBlock { Text = hint, Style = (Style)Application.Current.FindResource("ItemStatus") };
        panel.Children.Add(check);
        panel.Children.Add(status);
        rows[key] = new ItemRow(check, status);
    }

    /// <summary>Taille des dossiers et des navigateurs, calculée en arrière-plan (peut prendre une minute).</summary>
    private async Task MeasureAsync(UserProfile profile)
    {
        var version = ++_sizeVersion;
        BackupTotal.Text = "Calcul de la taille…";
        foreach (var folder in profile.Folders())
        {
            var size = await Task.Run(() => Tools.Measure(folder.Path));
            if (version != _sizeVersion) return;
            _sizes[Items.FolderPrefix + folder.Key] = size.Bytes;
            if (_backupRows.TryGetValue(Items.FolderPrefix + folder.Key, out var row))
                row.Status.Text = Directory.Exists(folder.Path)
                    ? $"{folder.Path} — {size.Files} fichiers, {Tools.Size(size.Bytes)}"
                    : $"{folder.Path} — absent";
            UpdateBackupTotal();
        }
        var browsers = await Task.Run(() => Browser.InstalledFor(profile).Aggregate(new FolderSize(0, 0),
            (sum, b) => sum + Tools.Measure(b.PathIn(profile), Browser.CacheFolders)));
        if (version != _sizeVersion) return;
        _sizes[Items.Browsers] = browsers.Bytes;
        UpdateBackupTotal();
    }

    private ulong NeededBytes() => _backupRows.Where(r => r.Value.Check.IsChecked == true)
        .Aggregate(0UL, (sum, r) => sum + (_sizes.TryGetValue(r.Key, out var size) ? size : 0));

    private void UpdateBackupTotal()
    {
        BackupTotal.Text = $"Total à copier : environ {Tools.Size(NeededBytes())}";
        UpdateDestInfo();
    }

    private string? Destination => (DriveCombo.SelectedItem as ComboBoxItem)?.Tag as string;

    private void RefreshDrives_Click(object sender, RoutedEventArgs e) => RefreshDrives();

    private void RefreshDrives()
    {
        DriveCombo.Items.Clear();
        var system = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        ComboBoxItem? best = null;
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is DriveType.CDRom or DriveType.Unknown or DriveType.NoRootDirectory) continue;
                var root = drive.RootDirectory.FullName;
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "" : $" « {drive.VolumeLabel} »";
                var kind = drive.DriveType == DriveType.Removable ? "amovible" : drive.DriveType == DriveType.Network ? "réseau"
                    : string.Equals(root, system, StringComparison.OrdinalIgnoreCase) ? "disque de Windows" : "disque";
                var item = new ComboBoxItem { Content = $"{root}{label} — {kind}, {Tools.Size((ulong)drive.AvailableFreeSpace)} libres", Tag = root };
                DriveCombo.Items.Add(item);
                if (!string.Equals(root, system, StringComparison.OrdinalIgnoreCase) && (best is null || drive.DriveType == DriveType.Removable)) best = item;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        DriveCombo.SelectedItem = best;
        UpdateDestInfo();
    }

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Dossier où créer la sauvegarde") is not { } path) return;
        var item = new ComboBoxItem { Content = path, Tag = path };
        DriveCombo.Items.Add(item);
        DriveCombo.SelectedItem = item;
    }

    private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDestInfo();

    private void UpdateDestInfo()
    {
        if (DestInfo is null) return;
        if (Destination is not { } dest) { DestInfo.Text = "Brancher le disque externe puis cliquer ⟳."; return; }
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(dest)!);
            var free = (ulong)drive.AvailableFreeSpace;
            var needed = NeededBytes();
            var text = $"La sauvegarde sera créée dans {Path.Combine(dest, Layout.RootName)}. Libre : {Tools.Size(free)}";
            if (needed > 0) text += free > needed ? " : assez de place." : $" : PAS ASSEZ DE PLACE (il faut environ {Tools.Size(needed)}).";
            if (drive.DriveFormat.Equals("FAT32", StringComparison.OrdinalIgnoreCase))
                text += " Disque en FAT32 : les fichiers de plus de 4 Go ne passent pas (formater en exFAT ou NTFS).";
            if (string.Equals(Path.GetPathRoot(dest), Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))
                text += " Attention : c'est le disque de Windows de ce PC, pas un disque externe.";
            DestInfo.Text = text;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            DestInfo.Text = "Disque illisible : " + ex.Message;
        }
    }

    private async void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        if (BackupProfile is not { } profile) { Warn("Choisir le compte Windows du client."); return; }
        if (Destination is not { } dest) { Warn("Brancher le disque externe et le choisir (⟳ pour actualiser la liste)."); return; }
        var items = _backupRows.Where(r => r.Value.Check.IsChecked == true).Select(r => r.Key).ToList();
        if (items.Count == 0) { Warn("Cocher au moins un élément à sauvegarder."); return; }

        var password = BackupPassword.Password;
        if (password != BackupPassword2.Password) { Warn("Les deux mots de passe de la sauvegarde ne sont pas identiques."); return; }
        if ((items.Contains(Items.Passwords) || items.Contains(Items.WiFi)) && password.Length < 6)
        {
            Warn("Choisir un mot de passe de la sauvegarde (6 caractères au moins) : il chiffre les mots de passe des navigateurs et les clés Wi-Fi.\n\nOu décocher ces deux éléments.");
            return;
        }
        try
        {
            var free = (ulong)new DriveInfo(Path.GetPathRoot(dest)!).AvailableFreeSpace;
            if (NeededBytes() > free && !Confirm($"Il faut environ {Tools.Size(NeededBytes())} et il reste {Tools.Size(free)} sur le disque. Lancer quand même ?"))
                return;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }

        Layout layout;
        try { layout = Layout.Create(dest, ClientBox.Text, Environment.MachineName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Warn("Impossible de créer le dossier de sauvegarde : " + ex.Message);
            return;
        }

        SetRunning(true);
        var ctx = NewContext(layout, profile, password.Length > 0 ? password : null);
        _reportClient = ClientBox.Text.Trim();
        ctx.Report.Begin("Migration : sauvegarde de l'ancien PC", new[]
        {
            ("Ancien PC", Environment.MachineName),
            ("Compte Windows", profile.Name),
            ("Sauvegarde", layout.Folder),
        });
        _report = ctx.Report;
        Log($"===== Sauvegarde de {profile.Name} vers {layout.Folder} =====");
        try
        {
            if (ctx.Password is { } pw) Vault.WriteCheck(layout.Folder, pw);
            layout.WriteManifest(Manifest(profile, items, ctx.Password is not null));
            await Task.Run(() => Backup.Run(ctx, items));
            layout.WriteManifest(Manifest(profile, items, ctx.Password is not null));
        }
        finally
        {
            ctx.Report.Step("Résultat", _stopRequested ? "Arrêtée avant la fin : INCOMPLÈTE"
                : ctx.Problems.Count > 0 ? "INCOMPLÈTE : " + string.Join(" ; ", ctx.Problems) : "Complète : aucun fichier manquant signalé");
            ctx.Report.Finish();
            ShowResults(ctx, _backupRows);
            SaveReport(Path.Combine(layout.Folder, "Rapport de sauvegarde.html"));
            SetRunning(false);
        }
        Log(_stopRequested ? "===== Sauvegarde arrêtée =====" : "===== Sauvegarde terminée =====");
        // Jamais « terminée » tout court s'il manque quelque chose : le technicien doit le voir avant d'effacer l'ancien PC.
        StatusText.Text = _stopRequested ? "Sauvegarde arrêtée : elle est INCOMPLÈTE."
            : ctx.Problems.Count > 0 ? $"Sauvegarde terminée mais INCOMPLÈTE ({ctx.Problems.Count} élément(s)) : ne pas effacer l'ancien PC avant d'avoir vérifié."
            : "Sauvegarde terminée et complète : brancher le disque sur le nouveau PC et ouvrir l'onglet 2.";
        RefreshSources();
        MessageBox.Show(this, $"{StatusText.Text}{Problems(ctx)}\n\nDossier : {layout.Folder}", Title, MessageBoxButton.OK,
            _stopRequested || ctx.Problems.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private Dictionary<string, string> Manifest(UserProfile profile, IEnumerable<string> items, bool encrypted) => new()
    {
        ["Logiciel"] = "PC Migration " + AppInfo.Version,
        ["Date"] = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
        ["Ordinateur"] = Environment.MachineName,
        ["Compte"] = profile.Name,
        ["Client"] = ClientBox.Text.Trim(),
        ["Windows"] = Environment.OSVersion.VersionString,
        ["Chiffrement"] = encrypted ? "oui" : "non",
        ["Elements"] = string.Join(",", items),
    };

    // ===================== Restauration =====================

    private Layout? Source => (SourceCombo.SelectedItem as ComboBoxItem)?.Tag as Layout;

    private void RefreshSources_Click(object sender, RoutedEventArgs e) => RefreshSources();

    private void RefreshSources()
    {
        SourceCombo.Items.Clear();
        foreach (var layout in Layout.Find()) SourceCombo.Items.Add(SourceItem(layout));
        SourceCombo.SelectedIndex = SourceCombo.Items.Count > 0 ? 0 : -1;
        if (SourceCombo.Items.Count == 0)
        {
            SourceInfo.Text = $"Aucune sauvegarde trouvée : brancher le disque externe (dossier « {Layout.RootName} ») puis cliquer ⟳.";
            BuildRestoreItems();
        }
    }

    private static ComboBoxItem SourceItem(Layout layout)
    {
        var m = layout.ReadManifest();
        string Get(string key) => m.TryGetValue(key, out var v) ? v : "?";
        return new ComboBoxItem { Content = $"{Get("Ordinateur")} — {Get("Compte")} — {Get("Date")}  ({layout.Folder})", Tag = layout };
    }

    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Dossier de la sauvegarde (celui qui contient migration.ini)") is not { } path) return;
        if (!File.Exists(Path.Combine(path, Layout.ManifestName)))
        {
            Warn($"Ce dossier ne contient pas de sauvegarde PC Migration ({Layout.ManifestName} absent).");
            return;
        }
        var item = SourceItem(new Layout(path));
        SourceCombo.Items.Add(item);
        SourceCombo.SelectedItem = item;
    }

    private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => BuildRestoreItems();

    private void BuildRestoreItems()
    {
        RestoreItems.Children.Clear();
        _restoreRows.Clear();
        AppList.Children.Clear();
        AppsExpander.Visibility = Visibility.Collapsed;
        if (Source is not { } layout) return;

        var m = layout.ReadManifest();
        var client = m.TryGetValue("Client", out var c) && c.Length > 0 ? $", client {c}" : "";
        SourceInfo.Text = $"Sauvegarde de {(m.TryGetValue("Ordinateur", out var pc) ? pc : "?")} (compte {(m.TryGetValue("Compte", out var account) ? account : "?")}) " +
                          $"du {(m.TryGetValue("Date", out var date) ? date : "?")}{client}.";
        if (m.TryGetValue("Client", out var name) && name.Length > 0) ClientBox.Text = name;

        var available = Restore.Available(layout);
        var target = RestoreProfileCombo.SelectedItem as UserProfile;
        if (available.Contains(Items.Apps))
        {
            var ids = Winget.ReadIds(layout.WingetFile);
            AddRow(RestoreItems, _restoreRows, Items.Apps, $"Réinstaller les applications ({ids.Count})",
                "Avec winget, sans fenêtre ni question. Les applications déjà installées sont passées.", true);
            foreach (var id in ids)
                AppList.Children.Add(new CheckBox { Content = id, Tag = id, IsChecked = true, Margin = new Thickness(0, 2, 14, 2), MinWidth = 220 });
            AppsExpander.Visibility = ids.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            // L'expander reste juste sous la ligne des applications.
            RestoreItems.Children.Remove(AppsExpander);
            ((Panel)AppsExpander.Parent)?.Children.Remove(AppsExpander);
            RestoreItems.Children.Add(AppsExpander);
        }
        foreach (var key in available.Where(i => i.StartsWith(Items.FolderPrefix)))
        {
            var folderKey = key.Substring(Items.FolderPrefix.Length);
            var folder = target?.FolderByKey(folderKey);
            AddRow(RestoreItems, _restoreRows, key, folder?.Label ?? folderKey,
                folder is null ? "Dossier inconnu sur ce PC" : $"Vers {folder.Path}. Un fichier plus récent déjà présent est gardé.", folder is not null);
        }
        if (available.Contains(Items.Browsers))
            AddRow(RestoreItems, _restoreRows, Items.Browsers, "Navigateurs : favoris, historique, extensions, onglets",
                "Le profil déjà présent sur ce PC est mis de côté (renommé), pas supprimé.", true);
        if (available.Contains(Items.Passwords))
            AddRow(RestoreItems, _restoreRows, Items.Passwords, "Mots de passe des navigateurs",
                "Import guidé dans chaque navigateur ; le fichier déchiffré est effacé juste après.", true);
        if (available.Contains(Items.WiFi))
            AddRow(RestoreItems, _restoreRows, Items.WiFi, "Réseaux Wi-Fi", "Ajoutés pour tous les comptes du PC.", true);
        if (available.Contains(Items.Outlook))
            AddRow(RestoreItems, _restoreRows, Items.Outlook, "Outlook : signatures et fichiers .pst", ".pst copiés dans Documents\\Fichiers Outlook.", true);
        if (available.Contains(Items.Wallpaper))
            AddRow(RestoreItems, _restoreRows, Items.Wallpaper, "Fond d'écran", "", true);
        if (available.Contains(Items.Printers))
            AddRow(RestoreItems, _restoreRows, Items.Printers, "Liste des imprimantes", "Reprise dans le rapport : pilotes à installer depuis le site du fabricant.", true);
        RestorePasswordCard.Visibility = Vault.HasCheck(layout.Folder) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CheckAllApps_Click(object sender, RoutedEventArgs e) => SetApps(true);
    private void UncheckAllApps_Click(object sender, RoutedEventArgs e) => SetApps(false);

    private void SetApps(bool value)
    {
        foreach (var check in AppList.Children.OfType<CheckBox>()) check.IsChecked = value;
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        if (Source is not { } layout) { Warn("Choisir la sauvegarde à restaurer (brancher le disque externe puis ⟳)."); return; }
        if (RestoreProfileCombo.SelectedItem is not UserProfile profile) { Warn("Choisir le compte Windows du client sur ce PC."); return; }
        var items = _restoreRows.Where(r => r.Value.Check.IsChecked == true).Select(r => r.Key).ToList();
        if (items.Count == 0) { Warn("Cocher au moins un élément à restaurer."); return; }
        var apps = AppList.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();

        string? password = null;
        if (Vault.HasCheck(layout.Folder))
        {
            password = RestorePassword.Password;
            var needed = items.Contains(Items.Passwords) || items.Contains(Items.WiFi);
            if (password.Length == 0 && needed) { Warn("Taper le mot de passe de la sauvegarde (mots de passe et Wi-Fi)."); return; }
            if (password.Length > 0 && !await Task.Run(() => Vault.CheckPassword(layout.Folder, password)))
            {
                Warn("Mot de passe de la sauvegarde incorrect.");
                return;
            }
            if (password.Length == 0) password = null;
        }
        var m = layout.ReadManifest();
        if (string.Equals(m.TryGetValue("Ordinateur", out var fromPc) ? fromPc : "", Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            && !Confirm("Cette sauvegarde a été faite sur CE PC. La restaurer quand même ici ?"))
            return;

        SetRunning(true);
        var ctx = NewContext(layout, profile, password);
        _reportClient = m.TryGetValue("Client", out var client) ? client : ClientBox.Text.Trim();
        ctx.Report.Begin("Migration : restauration sur le nouveau PC", new[]
        {
            ("Ancien PC", fromPc ?? "?"),
            ("Sauvegarde du", m.TryGetValue("Date", out var date) ? date : "?"),
            ("Nouveau PC", Environment.MachineName),
            ("Compte Windows", profile.Name),
        });
        _report = ctx.Report;
        Log($"===== Restauration de {layout.Folder} dans le compte {profile.Name} =====");
        try
        {
            await Task.Run(() => Restore.Run(ctx, items, apps));
        }
        finally
        {
            ctx.Report.Step("Résultat", _stopRequested ? "Arrêtée avant la fin : INCOMPLÈTE"
                : ctx.Problems.Count > 0 ? "INCOMPLÈTE : " + string.Join(" ; ", ctx.Problems) : "Complète : aucun fichier manquant signalé");
            ctx.Report.Finish();
            ShowResults(ctx, _restoreRows);
            SaveReport(Path.Combine(AppPaths.ClientFolder(_reportClient, DateTime.Now), $"Migration {DateTime.Now:yyyy-MM-dd HH-mm}.html"));
            SetRunning(false);
        }
        Log(_stopRequested ? "===== Restauration arrêtée =====" : "===== Restauration terminée =====");
        StatusText.Text = _stopRequested ? "Restauration arrêtée : elle est INCOMPLÈTE."
            : ctx.Problems.Count > 0 ? $"Restauration terminée mais INCOMPLÈTE ({ctx.Problems.Count} élément(s)) : voir le rapport."
            : "Restauration terminée : vérifier avec le client, puis faire le rapport de migration.";
        MessageBox.Show(this, StatusText.Text + Problems(ctx) + "\n\nRedémarrer le PC est conseillé (Wi-Fi, fond d'écran, profils des navigateurs).",
            Title, MessageBoxButton.OK, _stopRequested || ctx.Problems.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    // ===================== Commun =====================

    private JobContext NewContext(Layout layout, UserProfile profile, string? password) =>
        new(layout, profile, password, new Progress<string>(Log),
            message => Dispatcher.Invoke(() =>
            {
                Activate();
                return MessageBox.Show(this, message, Title, MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK;
            }),
            () => _stopRequested);

    private static string Problems(JobContext ctx) =>
        ctx.Problems.Count == 0 ? "" : "\n\n" + string.Join("\n", ctx.Problems.Take(8).Select(p => "• " + p)) + (ctx.Problems.Count > 8 ? "\n• …" : "");

    private static void ShowResults(JobContext ctx, Dictionary<string, ItemRow> rows)
    {
        foreach (var pair in ctx.Results)
            if (rows.TryGetValue(pair.Key, out var row)) row.Status.Text = "→ " + pair.Value;
    }

    /// <summary>Copie du rapport enregistrée d'office (dans la sauvegarde, ou dans le dossier du client).</summary>
    private void SaveReport(string path)
    {
        if (_report is null || _report.IsEmpty) return;
        try
        {
            var info = ReportInfo.LoadDefaults();
            if (_reportClient.Length > 0) info.Client = _reportClient;
            ReportWriter.Write(path, info, _report.ToSections());
            Log("Rapport enregistré : " + path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log("Rapport non enregistré : " + ex.Message);
        }
    }

    private void ReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null || _report.IsEmpty)
        {
            MessageBox.Show(this, "Lancer d'abord une sauvegarde ou une restauration : le rapport décrit ce qui a été transféré.", Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new ExportWindow { Owner = this, FilePrefix = "Migration" };
        if (dialog.ShowDialog() != true || dialog.Info is null || dialog.FilePath is null) return;
        try
        {
            ReportWriter.Write(dialog.FilePath, dialog.Info, _report.ToSections());
            Process.Start(new ProcessStartInfo(dialog.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "Impossible d'enregistrer le rapport : " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _stopRequested = false;
        BackupButton.IsEnabled = RestoreButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        StopButton.Content = "Arrêter après l'étape";
        BackupProfileCombo.IsEnabled = RestoreProfileCombo.IsEnabled = DriveCombo.IsEnabled = SourceCombo.IsEnabled = !running;
        if (running) StatusText.Text = "En cours… (suivre le journal)";
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _stopRequested = true;
        StopButton.IsEnabled = false;
        StopButton.Content = "Arrêt demandé…";
        Log("Arrêt demandé : arrêt à la fin de l'étape en cours.");
    }

    private void Log(string text)
    {
        // Appelé d'un autre fil : on repasse par celui de la fenêtre au lieu de planter.
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Log(text))); return; }
        CrashGuard.Trace(text);
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(LogBox.Text); }
        catch (System.Runtime.InteropServices.ExternalException) { }
    }

    private void UpdateLink_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_updateUrl) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_running && !Confirm("Une copie est en cours. Fermer quand même ? La sauvegarde ou la restauration sera incomplète."))
            e.Cancel = true;
    }

    private static string? PickFolder(string description)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = description, ShowNewFolderButton = true };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private void Warn(string message) => MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
}
