using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

    // La fenêtre de Glary est dessinée par Glary lui-même : Windows n'y voit souvent aucun bouton. On clique alors au
    // centre du gros bouton vert en bas de la fenêtre, qui affiche « Analyser » puis « Réparer les problèmes ».
    private const double ButtonX = 0.50;
    private const double ButtonY = 0.88;

    /// <summary>
    /// Pilote la maintenance 1 clic sans intervention : clic sur « Analyser », attente de la fin de l'analyse,
    /// clic sur « Réparer », attente de la fin de la réparation, puis fermeture de la fenêtre.
    /// Si quelque chose ne colle pas, la fenêtre reste ouverte et la suite attend qu'on la ferme.
    /// </summary>
    private static void Drive(Process launched, IProgress<string> log)
    {
        var window = WaitForWindow(launched, TimeSpan.FromSeconds(60));
        if (window is not { } w)
        {
            log.Report("Glary : fenêtre introuvable, terminer la maintenance à la main puis fermer la fenêtre.");
            return;
        }
        Thread.Sleep(3000); // laisser la fenêtre se dessiner

        log.Report("Glary : clic sur « Analyser ».");
        if (!Press(w, ScanButton, log)) return;
        // L'analyse démarre : attendre qu'elle se termine (Glary ne travaille plus et le bouton vert est revenu).
        if (!WaitUntilIdle(w, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(30), log)) return;

        log.Report("Glary : analyse terminée, clic sur « Réparer les problèmes ».");
        if (Press(w, RepairButton, log))
        {
            if (!WaitUntilIdle(w, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(30), log)) return;
        }
        else log.Report("Glary : pas de bouton « Réparer » (rien à réparer ?).");

        log.Report("Glary : réparation terminée, fermeture de la fenêtre.");
        try { w.Process.CloseMainWindow(); }
        catch (InvalidOperationException) { }
    }

    private readonly record struct GlaryWindow(Process Process, IntPtr Handle);

    /// <summary>La fenêtre de la maintenance 1 clic, même si le programme lancé a passé la main à un autre processus.</summary>
    private static GlaryWindow? WaitForWindow(Process launched, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var candidates = new List<Process>();
            try { if (!launched.HasExited) { launched.Refresh(); candidates.Add(launched); } }
            catch (InvalidOperationException) { }
            candidates.AddRange(Process.GetProcessesByName(Path.GetFileNameWithoutExtension(OneClick)));
            foreach (var process in candidates)
            {
                try
                {
                    process.Refresh();
                    if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero && IsWindowVisible(process.MainWindowHandle))
                        return new GlaryWindow(process, process.MainWindowHandle);
                }
                catch (InvalidOperationException) { }
            }
            Thread.Sleep(1000);
        }
        return null;
    }

    /// <summary>Clique sur le bouton par son nom si Windows le voit, sinon sur le bouton vert du bas de la fenêtre.</summary>
    private static bool Press(GlaryWindow window, Regex label, IProgress<string> log)
    {
        if (window.Process.HasExited) return false;
        ShowWindow(window.Handle, 9); // SW_RESTORE
        SetForegroundWindow(window.Handle);
        Thread.Sleep(500);

        if (FindByName(window.Handle, label) is { } element)
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            {
                try { ((InvokePattern)pattern).Invoke(); return true; }
                catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException) { }
            }
            var r = element.Current.BoundingRectangle;
            if (!r.IsEmpty) { Click((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2)); return true; }
        }

        if (!GetWindowRect(window.Handle, out var rect)) return false;
        var x = rect.Left + (int)((rect.Right - rect.Left) * ButtonX);
        var y = rect.Top + (int)((rect.Bottom - rect.Top) * ButtonY);
        if (!IsGreen(x, y))
        {
            log.Report("Glary : le bouton vert n'est pas à l'endroit attendu, terminer à la main puis fermer la fenêtre.");
            return false;
        }
        Click(x, y);
        return true;
    }

    private static AutomationElement? FindByName(IntPtr handle, Regex label)
    {
        try
        {
            foreach (AutomationElement element in AutomationElement.FromHandle(handle).FindAll(TreeScope.Descendants, Condition.TrueCondition))
            {
                var name = element.Current.Name?.Trim() ?? "";
                if (label.IsMatch(name) && element.Current.IsEnabled && !element.Current.IsOffscreen) return element;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or ArgumentException) { }
        return null;
    }

    /// <summary>
    /// Attend que Glary ait fini : presque plus de processeur utilisé pendant 10 secondes et le bouton vert affiché
    /// (pendant l'analyse, le bouton du bas sert à arrêter : on ne clique pas dessus).
    /// </summary>
    private static bool WaitUntilIdle(GlaryWindow window, TimeSpan minimum, TimeSpan timeout, IProgress<string> log)
    {
        var started = DateTime.UtcNow;
        var lastCpu = Cpu(window.Process);
        var quietSince = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            Thread.Sleep(2000);
            if (window.Process.HasExited) return false;
            var cpu = Cpu(window.Process);
            if ((cpu - lastCpu).TotalMilliseconds > 200) quietSince = DateTime.UtcNow;
            lastCpu = cpu;
            if (DateTime.UtcNow - started < minimum || DateTime.UtcNow - quietSince < TimeSpan.FromSeconds(10)) continue;
            if (!GetWindowRect(window.Handle, out var rect)) return false;
            var x = rect.Left + (int)((rect.Right - rect.Left) * ButtonX);
            var y = rect.Top + (int)((rect.Bottom - rect.Top) * ButtonY);
            if (FindByName(window.Handle, ScanButton) is not null || FindByName(window.Handle, RepairButton) is not null || IsGreen(x, y)) return true;
        }
        log.Report("Glary : délai dépassé, terminer à la main puis fermer la fenêtre.");
        return false;
    }

    private static TimeSpan Cpu(Process process)
    {
        try { process.Refresh(); return process.TotalProcessorTime; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return TimeSpan.Zero; }
    }

    /// <summary>Vrai si le point de l'écran est du vert franc des boutons de Glary.</summary>
    private static bool IsGreen(int x, int y)
    {
        var dc = GetDC(IntPtr.Zero);
        try
        {
            var color = GetPixel(dc, x, y);
            int r = (int)(color & 0xFF), g = (int)((color >> 8) & 0xFF), b = (int)((color >> 16) & 0xFF);
            return g > 140 && g > r + 60 && g > b + 60;
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }

    private static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(150);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // bouton gauche enfoncé
        Thread.Sleep(80);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // relâché
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
}
