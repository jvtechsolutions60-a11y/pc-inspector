using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PcMaintenance.Maintenance;

/// <summary>
/// Pilote l'agent Windows Update (objets COM « Microsoft.Update.* ») : recherche, téléchargement, installation.
/// Les objets COM sont appelés par réflexion pour éviter une référence d'interopérabilité.
/// </summary>
internal sealed class WindowsUpdate
{
    // OperationResultCode de l'agent Windows Update.
    private const int Succeeded = 2;
    private const int SucceededWithErrors = 3;

    private readonly object _session;

    private WindowsUpdate(object session) => _session = session;

    public static WindowsUpdate Create()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session")
                   ?? throw new InvalidOperationException("Agent Windows Update introuvable sur ce PC.");
        var session = Activator.CreateInstance(type)!;
        Set(session, "ClientApplicationID", "PC Maintenance");
        return new WindowsUpdate(session);
    }

    // Type de mise à jour pour l'agent : 1 = logiciel, 2 = pilote.
    private const int SoftwareType = 1;

    /// <summary>
    /// Mises à jour logicielles non installées et non masquées (comme la page Windows Update).
    /// Avec les pilotes : tous les pilotes proposés, mais pas les mises à jour logicielles facultatives (« préversions »).
    /// </summary>
    public List<Update> Search(bool withDrivers = false)
    {
        var searcher = Call(_session, "CreateUpdateSearcher");
        var result = Call(searcher, "Search", withDrivers ? "IsInstalled=0 and IsHidden=0" : "IsInstalled=0 and IsHidden=0 and Type='Software'");
        var updates = Get(result, "Updates");
        var count = Convert.ToInt32(Get(updates, "Count"));
        var list = new List<Update>();
        for (var i = 0; i < count; i++)
        {
            var item = Get(updates, "Item", i)!;
            if (withDrivers && Get(item, "BrowseOnly") is true && Convert.ToInt32(Get(item, "Type")) == SoftwareType) continue;
            list.Add(new Update(item, Get(item, "Title")?.ToString() ?? "?"));
        }
        return list;
    }

    /// <summary>Télécharge puis installe une mise à jour. Renvoie true si elle est installée.</summary>
    public bool Install(Update update, out bool rebootRequired)
    {
        rebootRequired = false;
        if (Get(update.Com, "EulaAccepted") is false) Call(update.Com, "AcceptEula");

        var collection = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!)!;
        Call(collection, "Add", update.Com);

        if (Get(update.Com, "IsDownloaded") is not true)
        {
            var downloader = Call(_session, "CreateUpdateDownloader");
            Set(downloader, "Updates", collection);
            var downloaded = Call(downloader, "Download");
            if (!IsSuccess(downloaded)) return false;
        }

        var installer = Call(_session, "CreateUpdateInstaller");
        Set(installer, "Updates", collection);
        var installed = Call(installer, "Install");
        rebootRequired = Get(installed, "RebootRequired") is true;
        return IsSuccess(installed);
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

    private static bool IsSuccess(object? result) =>
        Convert.ToInt32(Get(result, "ResultCode")) is Succeeded or SucceededWithErrors;

    /// <summary>Erreurs attendues de l'agent (service arrêté, pas de réseau…), à afficher plutôt qu'à planter.</summary>
    public static bool IsExpected(Exception ex) => ex is TargetInvocationException or COMException or InvalidOperationException;

    public static string Describe(Exception ex) =>
        (ex is TargetInvocationException { InnerException: { } inner } ? inner : ex) switch
        {
            COMException com => $"erreur Windows Update 0x{com.ErrorCode:X8}",
            var other => other.Message,
        };

    private static object? Call(object? target, string method, params object[] args) =>
        target?.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);

    private static object? Get(object? target, string property, params object[] args) =>
        target?.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, args);

    private static void Set(object? target, string property, object value)
    {
        if (target is null) return;
        try
        {
            target.GetType().InvokeMember(property, BindingFlags.SetProperty, null, target, new[] { value });
        }
        catch (Exception ex) when (!(value is string) && ex is TargetInvocationException or COMException or MissingMethodException)
        {
            // Certaines propriétés objet de l'agent s'affectent par référence.
            target.GetType().InvokeMember(property, BindingFlags.PutRefDispProperty, null, target, new[] { value });
        }
    }

    internal sealed record Update(object Com, string Title);
}
