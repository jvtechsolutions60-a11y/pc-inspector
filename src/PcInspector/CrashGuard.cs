using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
/// Limites : journaux et rapports d'erreur gardés <see cref="KeepDays"/> jours, journal du jour plafonné à
/// <see cref="MaxDailyBytes"/> ; mots de passe, jetons et clés masqués avant écriture (<see cref="Redact"/>).
/// </summary>
public static class CrashGuard
{
    private static string _name = "JVTECH";
    private static readonly object TraceLock = new();
    private static bool _capReached;

    /// <summary>Durée de conservation des journaux et des rapports d'erreur.</summary>
    public const int KeepDays = 90;
    /// <summary>Taille maximale du journal d'une journée : au-delà, plus rien n'est ajouté ce jour-là.</summary>
    public const long MaxDailyBytes = 5 * 1024 * 1024;

    // « MotDePasse=xxx », « password: xxx », « Jeton=xxx », « /p:xxx »… : la valeur est remplacée par ***.
    private static readonly Regex SecretValue = new(
        @"(?<key>\b(mot ?de ?passe|motdepasse(protege)?|password|passwd|pwd|jeton(protege)?|token|secret|keymaterial|x-jvtech-jeton)\b\s*[=:]\s*|/p(assword)?:)(?<value>""[^""]*""|\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // « net user nom motdepasse » : le deuxième argument est le mot de passe.
    private static readonly Regex NetUser = new(@"(?<key>\bnet(\.exe)?""?\s+user\s+(""[^""]+""|\S+)\s+)(?<value>(?!/)(""[^""]*""|\S+))", RegexOptions.IgnoreCase);

    /// <summary>Masque les mots de passe, jetons et clés d'un texte avant qu'il soit écrit dans un journal.</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var masked = SecretValue.Replace(text, m => m.Groups["key"].Value + "***");
        return NetUser.Replace(masked, m => m.Groups["key"].Value + "***");
    }

    /// <summary>Supprime les journaux et rapports d'erreur de plus de <see cref="KeepDays"/> jours. Ne lève jamais d'erreur.</summary>
    public static void PurgeOld(DateTime now)
    {
        foreach (var name in new[] { "Journaux", "Plantages" })
        {
            try
            {
                var folder = Path.Combine(AppPaths.ReportsRoot, name);
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.GetFiles(folder, "*.txt"))
                    if (now - File.GetLastWriteTime(file) > TimeSpan.FromDays(KeepDays)) File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Mode ligne de commande : aucune fenêtre d'erreur (un script ou un RMM attend le code de sortie).</summary>
    public static bool Silent { get; set; }

    public static void Install(Application app, string name)
    {
        _name = name;
        app.DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write(e.ExceptionObject as Exception, fatal: true);
        TaskScheduler.UnobservedTaskException += (_, e) => { e.SetObserved(); Write(e.Exception, fatal: false); };
        // Chaque clic est noté : on sait ce qui a été fait juste avant un plantage.
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(OnClick), handledEventsToo: true);
        app.Exit += (_, e) => Trace($"Fermeture normale (code {e.ApplicationExitCode})");
        PurgeOld(DateTime.Now);
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
                var size = File.Exists(file) ? new FileInfo(file).Length : 0;
                if (size >= MaxDailyBytes)
                {
                    if (_capReached) return;
                    _capReached = true;
                    text = $"Journal du jour plein ({MaxDailyBytes / 1024 / 1024} Mo) : plus rien n'est ajouté aujourd'hui.";
                }
                File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss}] {Redact(text)}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception) { /* le journal ne doit jamais faire planter le logiciel */ }
    }

    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var file = Write(e.Exception, fatal: false);
        if (Silent) return;
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
                .AppendLine(Redact(ex?.ToString() ?? "(erreur inconnue)"));
            File.WriteAllText(file, text.ToString(), Encoding.UTF8);
            return file;
        }
        catch (Exception) { return null; }
    }
}
