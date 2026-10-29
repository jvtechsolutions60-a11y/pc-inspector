using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using Microsoft.Win32;

namespace PcMaintenance.Maintenance;

/// <summary>Repère Glary Utilities et lance sa « maintenance 1 clic », réparation comprise, sans clic.</summary>
internal static class Glary
{
    private const string OneClick = "OneClickMaintenance.exe";
    private const string Main = "Integrator.exe";

    /// <summary>Dossier d'installation de Glary Utilities, ou null s'il n'est pas installé.</summary>
    public static string? FindFolder() => Candidates().FirstOrDefault(d => File.Exists(Path.Combine(d, OneClick)) || File.Exists(Path.Combine(d, Main)));

    private static IEnumerable<string> Candidates()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var app = uninstall.OpenSubKey(name);
                if (app?.GetValue("DisplayName") is not string display || !display.StartsWith("Glary Utilities", StringComparison.OrdinalIgnoreCase)) continue;
                if (app.GetValue("InstallLocation") is string location && location.Length > 0) yield return location.Trim('"');
                if (app.GetValue("DisplayIcon") is string icon && icon.Length > 0)
                {
                    var folder = Path.GetDirectoryName(icon.Split(',')[0].Trim('"'));
                    if (!string.IsNullOrEmpty(folder)) yield return folder!;
                }
            }
        }

        foreach (var programs in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            var root = Environment.GetFolderPath(programs);
            if (string.IsNullOrEmpty(root)) continue;
            yield return Path.Combine(root, "Glary Utilities 6");
            yield return Path.Combine(root, "Glary Utilities 5");
            yield return Path.Combine(root, "Glary Utilities");
        }
    }

    /// <summary>
    /// Ouvre la maintenance 1 clic et attend que sa fenêtre soit fermée.
    /// Renvoie false si Glary s'est fermé tout de suite (il tournait peut-être déjà) : l'appelant demande alors confirmation.
    /// </summary>
    public static async Task<bool> RunOneClickAsync(string folder, IProgress<string> log)
    {
        var exe = Path.Combine(folder, OneClick);
        if (!File.Exists(exe))
        {
            exe = Path.Combine(folder, Main);
            log.Report("OneClickMaintenance.exe absent : ouverture de Glary Utilities, lancer « Maintenance 1 clic » à la main.");
        }

        log.Report("Lancement de " + exe);
        var started = DateTime.UtcNow;
        using (var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = folder }))
        {
            if (process is not null)
            {
                if (exe.EndsWith(OneClick, StringComparison.OrdinalIgnoreCase)) await Task.Run(() => Drive(process, log));
                await Task.Run(() => process.WaitForExit());
            }
        }

        // Le programme lancé peut passer la main à une autre fenêtre Glary : attendre qu'elle soit fermée aussi.
        await Task.Run(() =>
        {
            while (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(OneClick)).Length > 0)
                System.Threading.Thread.Sleep(1000);
        });

        return DateTime.UtcNow - started > TimeSpan.FromSeconds(10);
    }

    // Libellés des boutons de la maintenance 1 clic, en français et en anglais.
    private static readonly Regex ScanButton = new("^(Analyser|Rechercher|Scan)", RegexOptions.IgnoreCase);
    private static readonly Regex RepairButton = new("^(Réparer|Corriger|Repair|Fix)", RegexOptions.IgnoreCase);

    /// <summary>
    /// Pilote la fenêtre de Glary sans intervention : lance l'analyse si elle n'a pas démarré seule, clique sur
    /// « Réparer les problèmes » dès qu'il est disponible, puis ferme la fenêtre quand la réparation est finie.
    /// Si les boutons ne sont pas trouvés (autre version de Glary), la fenêtre reste ouverte et on attend sa fermeture.
    /// </summary>
    private static void Drive(Process process, IProgress<string> log)
    {
        var launched = DateTime.UtcNow;
        var deadline = launched.AddMinutes(30);
        var scanClicked = false;
        var repaired = false;
        DateTime? repairStarted = null;
        var shown = false;
        while (!process.HasExited && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(2000);
            AutomationElement? window;
            try
            {
                process.Refresh();
                if (process.MainWindowHandle == IntPtr.Zero) continue;
                window = AutomationElement.FromHandle(process.MainWindowHandle);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or ArgumentException) { continue; }

            var buttons = Buttons(window);
            if (!shown)
            {
                shown = true;
                log.Report("Boutons de Glary : " + (buttons.Count == 0 ? "aucun lisible" : string.Join(", ", buttons.Select(b => b.Name))));
            }

            if (!repaired)
            {
                var repair = buttons.FirstOrDefault(b => RepairButton.IsMatch(b.Name) && b.Enabled);
                if (repair.Element is not null && Invoke(repair.Element))
                {
                    repaired = true;
                    repairStarted = DateTime.UtcNow;
                    log.Report("Glary : réparation lancée automatiquement.");
                    continue;
                }
                var scan = buttons.FirstOrDefault(b => ScanButton.IsMatch(b.Name) && b.Enabled);
                if (!scanClicked && scan.Element is not null && DateTime.UtcNow - launched > TimeSpan.FromSeconds(10) && Invoke(scan.Element))
                {
                    scanClicked = true;
                    log.Report("Glary : analyse lancée automatiquement.");
                }
                continue;
            }

            // Réparation finie quand le bouton d'analyse revient (ou que le bouton de réparation a disparu) depuis un moment.
            var finished = buttons.Any(b => ScanButton.IsMatch(b.Name) && b.Enabled) || !buttons.Any(b => RepairButton.IsMatch(b.Name));
            if (finished && DateTime.UtcNow - repairStarted > TimeSpan.FromSeconds(20))
            {
                log.Report("Glary : réparation terminée, fermeture de la fenêtre.");
                try
                {
                    if (window.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern)) ((WindowPattern)pattern).Close();
                    else process.CloseMainWindow();
                }
                catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException) { process.CloseMainWindow(); }
                return;
            }
        }
        if (!repaired && !process.HasExited) log.Report("Glary : boutons introuvables, terminer la maintenance à la main puis fermer la fenêtre.");
    }

    private static List<(AutomationElement Element, string Name, bool Enabled)> Buttons(AutomationElement window)
    {
        var list = new List<(AutomationElement, string, bool)>();
        try
        {
            var found = window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            foreach (AutomationElement button in found)
            {
                var name = button.Current.Name?.Trim() ?? "";
                if (name.Length > 0) list.Add((button, name, button.Current.IsEnabled && !button.Current.IsOffscreen));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException) { }
        return list;
    }

    private static bool Invoke(AutomationElement button)
    {
        try
        {
            if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern)) return false;
            ((InvokePattern)pattern).Invoke();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException) { return false; }
    }
}
