using System;
using System.Collections.Generic;
using System.Linq;
using PcInspector.Collectors;
using PcInspector.Models;
using PcSecurite.Checks;

namespace JvtechSonde;

/// <summary>Les seuils : une valeur lue devient un niveau. Sans accès au PC, pour être vérifiées par les tests.</summary>
internal static class Rules
{
    /// <summary>Plus la valeur est haute, plus c'est grave (remplissage, mémoire, température…). Null = non mesurable.</summary>
    public static Level Above(double? value, double warning, double critical)
    {
        if (value is not { } v || double.IsNaN(v)) return Level.Unknown;
        if (v >= critical) return Level.Critical;
        return v >= warning ? Level.Warning : Level.Ok;
    }

    /// <summary>Utilisation moyenne du processeur : seulement orange, une charge ponctuelle n'est jamais une panne.</summary>
    public static Level CpuUsage(double? percent, double warning) =>
        percent is not { } p ? Level.Unknown : p >= warning ? Level.Warning : Level.Ok;

    /// <summary>
    /// Utilisation du processeur entre deux relevés de GetSystemTimes (temps cumulés, le temps noyau compte l'inactivité).
    /// Null si les relevés sont incohérents.
    /// </summary>
    public static double? CpuPercent(long idle0, long kernel0, long user0, long idle1, long kernel1, long user1)
    {
        var idle = idle1 - idle0;
        var total = (kernel1 - kernel0) + (user1 - user0);
        if (total <= 0 || idle < 0 || idle > total) return null;
        return Math.Round((total - idle) * 100.0 / total, 1);
    }

    /// <summary>Dernière recherche réussie de Windows Update : trop ancienne = Windows Update bloqué ou désactivé.</summary>
    public static Level UpdateSearch(DateTime? last, DateTime now, int maxDays)
    {
        if (last is null) return Level.Unknown;
        var days = (now - last.Value).TotalDays;
        if (days > maxDays * 3) return Level.Critical;
        return days > maxDays ? Level.Warning : Level.Ok;
    }

    /// <summary>Écrans bleus et arrêts brutaux sur 30 jours.</summary>
    public static Level Crashes(int? count, int warning, int critical)
    {
        if (count is not { } c) return Level.Unknown;
        if (c >= critical) return Level.Critical;
        return c >= warning ? Level.Warning : Level.Ok;
    }

    /// <summary>Même feu que dans PC Sécurité : danger = rouge, à améliorer = orange.</summary>
    public static Level FromSecurity(CheckStatus status) => status switch
    {
        CheckStatus.Danger => Level.Critical,
        CheckStatus.Warning => Level.Warning,
        CheckStatus.Ok or CheckStatus.Info => Level.Ok,
        CheckStatus.NotApplicable => Level.NotApplicable,
        _ => Level.Unknown,
    };

    /// <summary>Santé d'un disque d'après PC Inspector : un problème critique = rouge, à surveiller = orange.</summary>
    public static Level FromDiskHealth(DiskHealthResult result)
    {
        if (result.Problems.Any(p => p.Severity == Severity.Critical)) return Level.Critical;
        if (result.Problems.Any(p => p.Severity == Severity.Warning)) return Level.Warning;
        return result.Unavailable is not null ? Level.Unknown : Level.Ok;
    }

    /// <summary>Le niveau global : le pire des points mesurés (les points non mesurables ne comptent pas).</summary>
    public static Level Overall(IEnumerable<Point> points)
    {
        var levels = points.Select(p => p.Level).Where(l => l is Level.Critical or Level.Warning or Level.Ok).ToList();
        return levels.Count == 0 ? Level.Unknown : levels.Min();
    }
}
