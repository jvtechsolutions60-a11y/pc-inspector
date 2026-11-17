using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PcInspector;
using PcInspector.Collectors;
using PcMaintenance.Maintenance;

namespace JvtechEntretien;

public sealed record Antivirus(string Nom, bool Actif, bool AJour);

/// <summary>Photo de l'état du PC, prise avant puis après la maintenance. Lecture seule.</summary>
public sealed class Etat
{
    public DateTime Date { get; init; } = DateTime.Now;

    /// <summary>Mises à jour Windows en attente (titres) ; null si l'agent Windows Update n'a pas répondu.</summary>
    public List<string>? MajWindows { get; init; }
    public string? ErreurWindows { get; init; }

    /// <summary>Logiciels de la liste sûre à mettre à jour ; null si winget absent ou en échec.</summary>
    public List<WingetUpdate>? MajLogiciels { get; init; }
    /// <summary>Autres logiciels avec une mise à jour, volontairement non touchés (hors liste sûre).</summary>
    public int? AutresLogiciels { get; init; }
    public string? ErreurLogiciels { get; init; }

    public long? DisqueLibre { get; init; }
    public long? DisqueTotal { get; init; }
    public double? DisqueLibrePourcent => DisqueLibre is { } l && DisqueTotal is > 0 and var t ? 100.0 * l / t : null;

    /// <summary>Antivirus déclarés à Windows ; null si illisible (Windows Server, accès refusé).</summary>
    public List<Antivirus>? Antivirus { get; init; }

    public bool RedemarrageNecessaire { get; init; }

    /// <summary>Version de Windows, ex. « Windows 11 Professionnel 24H2 (26100.2314) ».</summary>
    public string Windows { get; init; } = "";

    public static Etat Relever(Reglages reglages, Action<string> journal)
    {
        List<string>? windows = null;
        string? erreurWindows = null;
        try
        {
            windows = WindowsUpdate.Create().Search().Select(u => u.Title).ToList();
        }
        catch (Exception ex) when (WindowsUpdate.IsExpected(ex))
        {
            erreurWindows = WindowsUpdate.Describe(ex);
            journal("Windows Update : " + erreurWindows);
        }

        var winget = Winget.MisesAJour(journal);
        List<WingetUpdate>? logiciels = null;
        int? autres = null;
        if (winget.MisesAJour is { } toutes)
        {
            logiciels = toutes.Where(u => reglages.Logiciels.Contains(u.Id, StringComparer.OrdinalIgnoreCase)).ToList();
            autres = toutes.Count - logiciels.Count;
        }
        else journal("Logiciels : " + winget.Erreur);

        long? libre = null, total = null;
        try
        {
            var disque = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            libre = disque.AvailableFreeSpace;
            total = disque.TotalSize;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }

        return new Etat
        {
            MajWindows = windows,
            ErreurWindows = erreurWindows,
            MajLogiciels = logiciels,
            AutresLogiciels = autres,
            ErreurLogiciels = winget.Erreur,
            DisqueLibre = libre,
            DisqueTotal = total,
            Antivirus = LireAntivirus(),
            RedemarrageNecessaire = WindowsState.IsRebootPending(),
            Windows = LireWindows(),
        };
    }

    internal static string LireWindows()
    {
        try
        {
            using var cle = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (cle is null) return "";
            var build = cle.GetValue("CurrentBuild") as string ?? "";
            var nom = cle.GetValue("ProductName") as string ?? "Windows";
            // ProductName dit encore « Windows 10 » sur Windows 11 : le numéro de build fait foi.
            if (int.TryParse(build, out var b) && b >= 22000) nom = nom.Replace("Windows 10", "Windows 11");
            var version = cle.GetValue("DisplayVersion") as string ?? cle.GetValue("ReleaseId") as string ?? "";
            var ubr = cle.GetValue("UBR") is int u ? "." + u : "";
            return $"{nom} {version} ({build}{ubr})".Replace("  ", " ").Trim();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return ""; }
    }

    /// <summary>
    /// Antivirus déclarés au Centre de sécurité. productState : octet 2 = actif (0x10 ou 0x11), octet 3 = à jour (0x00).
    /// </summary>
    private static List<Antivirus>? LireAntivirus()
    {
        try
        {
            return Wmi.Query("SELECT displayName, productState FROM AntiVirusProduct", @"root\SecurityCenter2").Select(p =>
            {
                var etat = Convert.ToUInt32(p["productState"] ?? 0u);
                return new Antivirus(p.Str("displayName") ?? "?", ((etat >> 8) & 0xFF) is 0x10 or 0x11, (etat & 0xFF) == 0x00);
            }).ToList();
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or System.Runtime.InteropServices.COMException
                                       or UnauthorizedAccessException or InvalidCastException or FormatException or OverflowException)
        {
            return null; // Centre de sécurité absent (Windows Server) ou illisible
        }
    }
}
