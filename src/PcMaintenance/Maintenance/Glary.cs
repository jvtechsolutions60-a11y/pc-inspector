using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PcMaintenance.Maintenance;

/// <summary>Repère Glary Utilities et lance sa « maintenance 1 clic ».</summary>
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
            if (process is not null) await Task.Run(() => process.WaitForExit());
        }

        // Le programme lancé peut passer la main à une autre fenêtre Glary : attendre qu'elle soit fermée aussi.
        await Task.Run(() =>
        {
            while (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(OneClick)).Length > 0)
                System.Threading.Thread.Sleep(1000);
        });

        return DateTime.UtcNow - started > TimeSpan.FromSeconds(10);
    }
}
