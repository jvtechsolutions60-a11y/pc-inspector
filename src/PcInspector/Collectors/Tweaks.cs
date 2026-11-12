using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Un réglage de Windows modifié par un « optimiseur » et le moyen de le remettre par défaut.</summary>
internal sealed record TweakFinding(string Name, string Message, string Fix);

/// <summary>
/// « Mauvais tweaks » : réglages que des optimiseurs ou des guides changent sans gain réel, au risque de problèmes
/// difficiles à trouver (horloge HPET, maintenance automatique, TRIM, profil « Performances optimales »).
/// Lecture seule : PC Inspector signale et donne la commande, il ne change rien.
/// </summary>
internal static class Tweaks
{
    public const string UltimatePerformance = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private static readonly string[] BcdTimerKeys = { "useplatformclock", "disabledynamictick", "useplatformtick" };

    public static List<TweakFinding> ReadAll()
    {
        string? bcd = null;
        try
        {
            var lines = new List<string>();
            var run = ProcessRunner.Run("bcdedit.exe", "/enum {current}", lines.Add, TimeSpan.FromSeconds(15));
            if (run.ExitCode == 0) bcd = string.Join("\n", lines);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }

        var cpu = Wmi.Query("SELECT Name FROM Win32_Processor").FirstOrDefault()?.Str("Name");
        return Judge(
            bcd,
            ReadDword(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "MaintenanceDisabled"),
            ReadDword(@"SYSTEM\CurrentControlSet\Control\FileSystem", "DisableDeleteNotify"),
            ReadString(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", "ActivePowerScheme"),
            cpu);
    }

    /// <summary>Règles, à partir des valeurs lues (null = non lu ou absent, donc rien à signaler).</summary>
    internal static List<TweakFinding> Judge(string? bcdOutput, int? maintenanceDisabled, int? disableDeleteNotify, string? activeScheme, string? cpuName)
    {
        var found = new List<TweakFinding>();

        var timers = BcdTimerKeys.Where(k => BcdHas(bcdOutput, k)).ToList();
        if (timers.Count > 0)
            found.Add(new TweakFinding("Horloge (HPET)",
                $"réglage d'horloge modifié ({string.Join(", ", timers)}) : aucun gain, peut causer saccades et bugs difficiles à trouver.",
                string.Join(" ; ", timers.Select(k => $"bcdedit /deletevalue {{current}} {k}")) + " puis redémarrer"));

        if (maintenanceDisabled == 1)
            found.Add(new TweakFinding("Maintenance automatique",
                "maintenance automatique de Windows désactivée (nettoyage, TRIM, optimisation des disques) : aucun gain, use le SSD plus vite.",
                @"reg delete ""HKLM\Software\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance"" /v MaintenanceDisabled /f puis redémarrer"));

        if (disableDeleteNotify is { } trim && trim != 0)
            found.Add(new TweakFinding("TRIM",
                "commande TRIM désactivée : le SSD ralentit et s'use plus vite.",
                "fsutil behavior set DisableDeleteNotify 0 puis redémarrer"));

        if (string.Equals(activeScheme?.Trim(), UltimatePerformance, StringComparison.OrdinalIgnoreCase) && IsRecentCpu(cpuName))
            found.Add(new TweakFinding("Alimentation",
                "profil « Performances optimales » activé : inutile sur ce processeur récent, il chauffe et consomme plus sans gain.",
                "Paramètres > Système > Alimentation : profil « Utilisation normale » (ou powercfg /setactive SCHEME_BALANCED)"));

        return found;
    }

    /// <summary>La clé est présente dans la sortie de bcdedit (le nom de la clé n'est pas traduit).</summary>
    internal static bool BcdHas(string? bcdOutput, string key) =>
        bcdOutput is not null && bcdOutput.Split('\n').Any(l =>
            l.TrimStart().StartsWith(key, StringComparison.OrdinalIgnoreCase)
            && (l.TrimStart().Length == key.Length || char.IsWhiteSpace(l.TrimStart()[key.Length])));

    /// <summary>
    /// Intel Core 12e génération et plus, Core Ultra, Ryzen 5000 et plus : « Performances optimales » n'y apporte rien.
    /// Processeur non reconnu : faux (on ne signale pas).
    /// </summary>
    internal static bool IsRecentCpu(string? name)
    {
        if (name is null || name.Trim().Length == 0) return false;
        if (Regex.IsMatch(name, @"Core(\(TM\))?\s+Ultra", RegexOptions.IgnoreCase)) return true;
        var intel = Regex.Match(name, @"\bi[3579]-(\d{4,5})", RegexOptions.IgnoreCase);
        if (intel.Success)
        {
            var digits = intel.Groups[1].Value;
            // 12700 → 12 ; portables 1135G7 → 11, 1235U → 12 ; anciens 8700 → 8.
            var generation = digits.Length == 5 || digits[0] == '1' ? int.Parse(digits.Substring(0, 2)) : int.Parse(digits.Substring(0, 1));
            return generation >= 12;
        }
        var ryzen = Regex.Match(name, @"Ryzen\s+(?:\d|Threadripper)\D*?(\d)\d{3}", RegexOptions.IgnoreCase);
        if (ryzen.Success) return int.Parse(ryzen.Groups[1].Value) >= 5;
        return false;
    }

    private static int? ReadDword(string path, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue(name) is int value ? value : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { return null; }
    }

    private static string? ReadString(string path, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { return null; }
    }
}
