using System;
using System.IO;
using System.Reflection;

namespace PcInspector;

/// <summary>Nom et version du logiciel lancé (v1.0.N, N = numéro de compilation GitHub Actions), partagés par les logiciels JVTECH.</summary>
public static class AppInfo
{
    internal static Assembly Entry => Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;

    public static string Version
    {
        get
        {
            var v = Entry.GetName().Version;
            return v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>Le N de v1.0.N, pour comparer avec la dernière version publiée.</summary>
    public static int Build => Entry.GetName().Version?.Build ?? 0;

    public static string Product => Entry.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "PC Inspector";
}

/// <summary>
/// Où les logiciels JVTECH rangent leurs fichiers. En mode clé USB (lancés depuis un lecteur amovible, ou avec un
/// fichier « portable.txt » à côté du .exe), tout reste sur la clé : rien n'est écrit sur le PC du client.
/// </summary>
public static class AppPaths
{
    public static string ExeFolder => Path.GetDirectoryName(AppInfo.Entry.Location) ?? Environment.CurrentDirectory;

    public static bool IsPortable { get; } = DetectPortable();

    private static bool DetectPortable()
    {
        try
        {
            if (File.Exists(Path.Combine(ExeFolder, "portable.txt"))) return true;
            var root = Path.GetPathRoot(ExeFolder);
            return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Removable;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Dossier « JVTECH » sur la clé, ou null hors mode clé USB.</summary>
    private static string? PortableRoot => IsPortable ? Path.Combine(ExeFolder, "JVTECH") : null;

    /// <summary>Réglages mémorisés (société, technicien, logo, prix…).</summary>
    public static string SettingsFolder => PortableRoot is { } usb
        ? Path.Combine(usb, "Paramètres")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PcInspector");

    /// <summary>Téléchargements et fichiers de travail (AdwCleaner, conversion PDF…).</summary>
    public static string WorkFolder => PortableRoot is { } usb ? Path.Combine(usb, "Temp") : Path.Combine(Path.GetTempPath(), "JVTECH");

    /// <summary>Racine du dossier client commun aux trois logiciels.</summary>
    public static string ReportsRoot => PortableRoot is { } usb
        ? Path.Combine(usb, "Rapports")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "JVTECH", "Rapports");

    /// <summary>Rapports\Client\2026-10-20 : les rapports des trois logiciels d'une même intervention au même endroit.</summary>
    public static string ClientFolder(string client, DateTime date)
    {
        var name = string.IsNullOrWhiteSpace(client) ? "Sans nom" : client.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        var folder = Path.Combine(ReportsRoot, name, date.ToString("yyyy-MM-dd"));
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ReportsRoot; }
        return folder;
    }

    public static string EnsureFolder(string folder)
    {
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return folder;
    }
}
