using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using PcInspector.Collectors;

namespace PcSecurite.Checks;

/// <summary>Mises à jour en attente (agent Windows Update) et date de la dernière mise à jour installée.</summary>
internal static class WindowsUpdateInfo
{
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(90);

    /// <summary>Titres des mises à jour en attente, ou null si la recherche a échoué ou a été trop longue.</summary>
    public static List<string>? PendingUpdates()
    {
        var search = Task.Run(Search);
        return search.Wait(SearchTimeout) ? search.Result : null;
    }

    private static List<string>? Search()
    {
        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType is null) return null;
            var session = Activator.CreateInstance(sessionType);
            var searcher = Call(session, "CreateUpdateSearcher");
            var result = Call(searcher, "Search", "IsInstalled=0 and IsHidden=0 and Type='Software'");
            var updates = Get(result, "Updates");
            var count = Convert.ToInt32(Get(updates, "Count"));
            var titles = new List<string>();
            for (int i = 0; i < count; i++)
                titles.Add(Get(Get(updates, "Item", i), "Title")?.ToString() ?? "?");
            return titles;
        }
        catch (Exception ex) when (ex is TargetInvocationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>Date du correctif le plus récent (Win32_QuickFixEngineering).</summary>
    public static DateTime? LastInstalled()
    {
        DateTime? latest = null;
        foreach (var fix in Wmi.QueryOptional("SELECT InstalledOn FROM Win32_QuickFixEngineering"))
        {
            var raw = fix.Str("InstalledOn");
            if (string.IsNullOrWhiteSpace(raw)) continue;
            // Windows renvoie la date au format américain (M/d/yyyy), parfois en hexadécimal sur de vieux systèmes.
            if (DateTime.TryParse(raw, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var date)
                && (latest is null || date > latest))
                latest = date;
        }
        return latest;
    }

    /// <summary>Windows note dans ces clés qu'un redémarrage est nécessaire pour finir une installation.</summary>
    public static bool IsRebootPending()
    {
        string[] keys =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
        };
        foreach (var path in keys)
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path);
            if (key is not null) return true;
        }
        return false;
    }

    private static object? Call(object? target, string method, params object[] args) =>
        target?.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);

    private static object? Get(object? target, string property, params object[] args) =>
        target?.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, args);
}
