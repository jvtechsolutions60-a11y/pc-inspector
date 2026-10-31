using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace PcInspector;

/// <summary>
/// Filet de sécurité commun aux logiciels : une erreur imprévue ne ferme plus la fenêtre. Elle est écrite dans
/// Rapports\Plantages (à envoyer pour corriger) et un message l'explique ; le logiciel reste ouvert.
/// Le journal de chaque lancement est aussi écrit au fil de l'eau dans Rapports\Journaux : même si Windows ferme le
/// logiciel d'un coup, les dernières lignes avant la fermeture y restent. Sans ligne « Fermeture » à la fin, il a planté.
/// </summary>
public static class CrashGuard
{
    private static string _name = "JVTECH";
    private static readonly object TraceLock = new();

    public static void Install(Application app, string name)
    {
        _name = name;
        app.DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write(e.ExceptionObject as Exception, fatal: true);
        TaskScheduler.UnobservedTaskException += (_, e) => { e.SetObserved(); Write(e.Exception, fatal: false); };
        // Chaque clic est noté : on sait ce qui a été fait juste avant un plantage.
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(OnClick), handledEventsToo: true);
        app.Exit += (_, e) => Trace($"Fermeture normale (code {e.ApplicationExitCode})");
        Trace($"===== Démarrage de {name} {AppInfo.Version} sur {Environment.MachineName} ({Environment.OSVersion}) =====");
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase button) return;
        var label = button.Content as string
                    ?? (button.Content as TextBlock)?.Text
                    ?? button.ToolTip as string
                    ?? (string.IsNullOrEmpty(button.Name) ? button.GetType().Name : button.Name);
        var state = button is ToggleButton toggle ? (toggle.IsChecked == true ? " (coché)" : " (décoché)") : "";
        var window = Window.GetWindow(button)?.Title;
        Trace($"Clic : {label}{state}{(string.IsNullOrEmpty(window) ? "" : " — " + window)}");
    }

    /// <summary>Ajoute une ligne au journal du jour (Rapports\Journaux\Logiciel date.txt). Ne lève jamais d'erreur.</summary>
    public static void Trace(string text)
    {
        try
        {
            var folder = Path.Combine(AppPaths.ReportsRoot, "Journaux");
            var file = Path.Combine(folder, $"{_name} {DateTime.Now:yyyy-MM-dd}.txt");
            lock (TraceLock)
            {
                Directory.CreateDirectory(folder);
                File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception) { /* le journal ne doit jamais faire planter le logiciel */ }
    }

    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var file = Write(e.Exception, fatal: false);
        try
        {
            MessageBox.Show(
                $"Une erreur imprévue s'est produite, le logiciel reste ouvert.\n\n{e.Exception.Message}" +
                (file is null ? "" : $"\n\nLe détail est enregistré dans :\n{file}\n\nEnvoie ce fichier pour que l'erreur soit corrigée."),
                _name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception) { /* pas de fenêtre possible : le fichier suffit */ }
    }

    /// <summary>Écrit le rapport d'erreur et renvoie son chemin (null si l'écriture a échoué).</summary>
    public static string? Write(Exception? ex, bool fatal)
    {
        Trace($"ERREUR {(fatal ? "FATALE" : "récupérée")} : {ex?.GetType().Name} : {ex?.Message}");
        try
        {
            var folder = Path.Combine(AppPaths.ReportsRoot, "Plantages");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"{_name} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.txt");
            var text = new StringBuilder()
                .AppendLine($"{_name} {AppInfo.Version} — {DateTime.Now:dd/MM/yyyy HH:mm:ss}")
                .AppendLine($"Windows : {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64" : "32")} bits)")
                .AppendLine(fatal ? "Erreur fatale : le logiciel s'est fermé." : "Erreur récupérée : le logiciel est resté ouvert.")
                .AppendLine()
                .AppendLine(ex?.ToString() ?? "(erreur inconnue)");
            File.WriteAllText(file, text.ToString(), Encoding.UTF8);
            return file;
        }
        catch (Exception) { return null; }
    }
}
