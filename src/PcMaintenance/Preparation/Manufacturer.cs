using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PcInspector;
using PcMaintenance.Maintenance;

namespace PcMaintenance.Preparation;

/// <summary>Résultat de l'outil du constructeur.</summary>
internal sealed record ManufacturerResult(string Summary, bool RebootRequired);

/// <summary>
/// Pilotes, firmwares et BIOS officiels par l'outil du constructeur, installé avec winget puis lancé en silencieux :
/// Dell Command | Update, Lenovo System Update, HP Image Assistant. Les autres marques passent par Windows Update seul.
/// </summary>
internal static class Manufacturer
{
    public static string Read()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_ComputerSystem");
            return searcher.Get().Cast<ManagementObject>().Select(o => o["Manufacturer"]?.ToString() ?? "").FirstOrDefault()?.Trim() ?? "";
        }
        catch (ManagementException)
        {
            return "";
        }
    }

    public static async Task<ManufacturerResult> RunAsync(bool bios, IProgress<string> log)
    {
        var name = Read();
        var brand = name.ToUpperInvariant();
        log.Report($"Constructeur : {(name.Length == 0 ? "inconnu" : name)}");

        if (bios && !OnMains())
        {
            bios = false;
            log.Report("Le PC est sur batterie : le BIOS ne sera pas mis à jour (brancher le chargeur pour la prochaine fois).");
        }

        if (brand.Contains("DELL")) return await DellAsync(bios, log);
        if (brand.Contains("LENOVO")) return await LenovoAsync(bios, log);
        if (brand.Contains("HP") || brand.Contains("HEWLETT")) return await HpAsync(bios, log);
        return new ManufacturerResult($"Pas d'outil prévu pour « {name} » : pilotes par Windows Update seulement.", false);
    }

    private static async Task<ManufacturerResult> DellAsync(bool bios, IProgress<string> log)
    {
        var exe = await EnsureToolAsync("Dell.CommandUpdate", log,
            @"%ProgramFiles%\Dell\CommandUpdate\dcu-cli.exe", @"%ProgramFiles(x86)%\Dell\CommandUpdate\dcu-cli.exe");
        if (exe is null) return new ManufacturerResult("Dell Command | Update n'a pas pu être installé.", false);

        var types = bios ? "bios,firmware,driver" : "firmware,driver";
        var code = await RunToolAsync(exe, $"/applyUpdates -silent -reboot=disable -autoSuspendBitLocker=enable -updateType={types}", log);
        // 0 : fait ; 1 et 5 : redémarrage nécessaire ; 500 : rien à installer.
        return code switch
        {
            0 => new ManufacturerResult("Dell Command | Update : mises à jour installées.", false),
            1 or 5 => new ManufacturerResult("Dell Command | Update : mises à jour installées, redémarrage nécessaire.", true),
            500 => new ManufacturerResult("Dell Command | Update : rien à mettre à jour.", false),
            null => new ManufacturerResult("Dell Command | Update n'a pas pu être lancé.", false),
            var other => new ManufacturerResult($"Dell Command | Update : terminé avec le code {other}.", false),
        };
    }

    private static async Task<ManufacturerResult> LenovoAsync(bool bios, IProgress<string> log)
    {
        var exe = await EnsureToolAsync("Lenovo.SystemUpdate", log,
            @"%ProgramFiles(x86)%\Lenovo\System Update\tvsu.exe", @"%ProgramFiles%\Lenovo\System Update\tvsu.exe");
        if (exe is null) return new ManufacturerResult("Lenovo System Update n'a pas pu être installé.", false);

        // Types de paquets : 1 application, 2 pilote, 3 BIOS, 4 firmware.
        var types = bios ? "1,2,3,4" : "1,2,4";
        var code = await RunToolAsync(exe,
            $"/CM -search A -action INSTALL -packagetypes {types} -includerebootpackages 1,3,4 -noicon -noreboot -nolicense", log);
        return code is null
            ? new ManufacturerResult("Lenovo System Update n'a pas pu être lancé.", false)
            // System Update ne dit pas clairement s'il faut redémarrer : on redémarre de toute façon avant Windows Update.
            : new ManufacturerResult($"Lenovo System Update : terminé (code {code}).", true);
    }

    private static async Task<ManufacturerResult> HpAsync(bool bios, IProgress<string> log)
    {
        var exe = await EnsureToolAsync("HP.ImageAssistant", log,
            @"%ProgramFiles%\HP\HPIA\HPImageAssistant.exe", @"C:\SWSetup\HPIA\HPImageAssistant.exe");
        exe ??= FindHpia();
        if (exe is null) return new ManufacturerResult("HP Image Assistant n'a pas pu être installé.", false);

        var work = AppPaths.EnsureFolder(Path.Combine(AppPaths.ExeFolder, "HPIA"));
        var categories = bios ? "Drivers,Firmware,BIOS" : "Drivers,Firmware";
        var code = await RunToolAsync(exe,
            $"/Operation:Analyze /Action:Install /Selection:All /Category:{categories} /Silent /ReportFolder:\"{work}\\Rapport\" /SoftpaqDownloadFolder:\"{work}\\Paquets\"", log);
        // 0 : fait ; 256 : rien à installer ; 3010 : redémarrage nécessaire.
        return code switch
        {
            0 => new ManufacturerResult("HP Image Assistant : mises à jour installées.", true),
            256 => new ManufacturerResult("HP Image Assistant : rien à mettre à jour.", false),
            3010 => new ManufacturerResult("HP Image Assistant : mises à jour installées, redémarrage nécessaire.", true),
            null => new ManufacturerResult("HP Image Assistant n'a pas pu être lancé.", false),
            var other => new ManufacturerResult($"HP Image Assistant : terminé avec le code {other}.", true),
        };
    }

    /// <summary>Renvoie le chemin de l'outil, en l'installant avec winget s'il manque.</summary>
    private static async Task<string?> EnsureToolAsync(string wingetId, IProgress<string> log, params string[] paths)
    {
        if (Find(paths) is { } found) return found;
        log.Report($"Installation de {wingetId} avec winget…");
        var code = await Winget.InstallAsync(wingetId, log);
        if (code is null) log.Report("winget est absent : outil du constructeur ignoré.");
        return Find(paths);
    }

    private static string? Find(IEnumerable<string> paths) =>
        paths.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(File.Exists);

    /// <summary>winget range HP Image Assistant dans son dossier de paquets, dont le nom contient la version.</summary>
    private static string? FindHpia()
    {
        var roots = new[]
        {
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\WinGet\Packages"),
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\WinGet\Packages"),
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                var file = Directory.EnumerateDirectories(root, "HP.ImageAssistant*")
                    .SelectMany(d => Directory.EnumerateFiles(d, "HPImageAssistant.exe", SearchOption.AllDirectories))
                    .FirstOrDefault();
                if (file is not null) return file;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    private static Task<int?> RunToolAsync(string exe, string arguments, IProgress<string> log) => Task.Run<int?>(() =>
    {
        log.Report($"Lancement : {Path.GetFileName(exe)} {arguments}");
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true })!;
            // Un BIOS et une série de pilotes peuvent prendre longtemps : jusqu'à deux heures.
            if (!process.WaitForExit((int)TimeSpan.FromHours(2).TotalMilliseconds))
            {
                log.Report("L'outil du constructeur ne répond plus après deux heures : on passe à la suite.");
                return null;
            }
            log.Report($"Terminé, code {process.ExitCode}.");
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            log.Report("Lancement impossible : " + ex.Message);
            return null;
        }
    });

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>Sur secteur (ou PC fixe). 0 = sur batterie.</summary>
    private static bool OnMains() => !GetSystemPowerStatus(out var status) || status.ACLineStatus != 0;
}
