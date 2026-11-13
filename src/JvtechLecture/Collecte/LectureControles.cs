using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using Jvtech.Lecture.Regles;
using PcInspector.Collectors;
using PcInspector.Models;

namespace Jvtech.Lecture.Collecte;

/// <summary>
/// Lecture des informations nécessaires aux contrôles d'Atelier : batterie, mémoire, disques, type de PC.
/// Lecture seule (WMI, journal Système, requêtes de santé des disques). Les décisions sont dans <see cref="RegleControles"/>.
/// </summary>
public static class LectureControles
{
    // Win32_SystemEnclosure.ChassisTypes : portable, notebook, sous-notebook, tablette, convertible, détachable…
    private static readonly HashSet<ulong> TypesPortables = new() { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };

    public static bool EstPortable()
    {
        foreach (var boitier in Wmi.QueryOptional("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
        {
            try
            {
                if (boitier["ChassisTypes"] is Array types && types.Cast<object>().Any(t => TypesPortables.Contains(Convert.ToUInt64(t))))
                    return true;
            }
            catch (Exception ex) when (ex is System.Management.ManagementException or FormatException or InvalidCastException) { }
        }
        // Repli : une batterie présente signale un portable.
        return Wmi.QueryOptional("SELECT Name FROM Win32_Battery").Count > 0;
    }

    public static MesureBatterie Batterie(bool portable)
    {
        var nombre = Wmi.QueryOptional("SELECT Name FROM Win32_Battery").Count;
        ulong? Somme(string wql, string propriete)
        {
            var valeurs = Wmi.QueryOptional(wql, Wmi.WmiScope).Select(o => o.ULong(propriete)).Where(v => v is > 0).ToList();
            return valeurs.Count == 0 ? null : valeurs.Aggregate(0UL, (s, v) => s + v!.Value);
        }
        var nominale = Somme("SELECT DesignedCapacity FROM BatteryStaticData", "DesignedCapacity");
        var actuelle = Somme("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity");
        var cycles = Wmi.QueryOptional("SELECT CycleCount FROM BatteryCycleCount", Wmi.WmiScope).Select(o => o.ULong("CycleCount")).FirstOrDefault(v => v is > 0);
        return new MesureBatterie(portable, nombre, nominale, actuelle, cycles);
    }

    // Codes du Gestionnaire de périphériques qui ne signalent pas un pilote : 22 = désactivé, 45 = non branché.
    private static readonly HashSet<ulong> CodesSansProbleme = new() { 0, 22, 45 };

    /// <summary>
    /// Périphériques en erreur (pilote manquant ou en défaut), par leur nom. Null si la liste n'a pas pu être lue.
    /// </summary>
    public static IReadOnlyList<string>? PeripheriquesEnErreur()
    {
        try
        {
            return Wmi.Query("SELECT Name, PNPClass, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0")
                .Where(d => d.ULong("ConfigManagerErrorCode") is { } code && !CodesSansProbleme.Contains(code))
                .Select(d => $"{d.Str("Name") ?? "Périphérique inconnu"} (code {d.ULong("ConfigManagerErrorCode")})")
                .Distinct()
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static int NombreBarrettes() => Wmi.QueryOptional("SELECT Capacity FROM Win32_PhysicalMemory").Count(m => m.ULong("Capacity") is > 0);

    /// <summary>Dernier résultat du diagnostic mémoire de Windows (événements 1101 : aucune erreur, 1102 : erreurs).</summary>
    public static DiagnosticMemoire? DernierDiagnosticMemoire()
    {
        const string requete = "*[System[Provider[@Name='Microsoft-Windows-MemoryDiagnostics-Results'] and (EventID=1101 or EventID=1102)]]";
        var dernier = Evenements(requete, null).OrderByDescending(e => e.Date).FirstOrDefault();
        return dernier.Date == default ? null : new DiagnosticMemoire(dernier.Date, dernier.Id == 1102);
    }

    /// <summary>Erreurs mémoire signalées par le matériel (WHEA, événement 47) sur les 90 derniers jours.</summary>
    public static int ErreursMemoireMaterielles()
    {
        const string requete = "*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and EventID=47 and TimeCreated[timediff(@SystemTime) <= 7776000000]]]";
        return Evenements(requete, 500).Count;
    }

    private static List<(int Id, DateTime Date)> Evenements(string requete, int? maximum)
    {
        var liste = new List<(int, DateTime)>();
        try
        {
            using var lecteur = new EventLogReader(new EventLogQuery("System", PathType.LogName, requete) { ReverseDirection = true });
            for (var e = lecteur.ReadEvent(); e is not null; e = lecteur.ReadEvent())
            {
                using (e)
                    if (e.TimeCreated is { } quand) liste.Add((e.Id, quand));
                if (maximum is { } max && liste.Count >= max) break;
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException) { }
        return liste;
    }

    /// <summary>Santé des disques internes (USB exclus). Un disque sans résultat est « non lu », jamais « sain ».</summary>
    public static IReadOnlyList<EtatDisque> Disques()
    {
        var sante = DiskHealth.ReadAll();
        var etats = new List<EtatDisque>();
        foreach (var disque in Wmi.QueryOptional("SELECT DeviceId, FriendlyName, BusType FROM MSFT_PhysicalDisk", Wmi.StorageScope))
        {
            if (disque.ULong("BusType") == 7 || disque.Str("DeviceId") is not { } id) continue; // USB
            var nom = Champ.Nettoyer(disque.Str("FriendlyName")) ?? "Disque " + id;
            if (!sante.TryGetValue(id, out var r))
            {
                etats.Add(new EtatDisque(nom, false, Array.Empty<string>(), Array.Empty<string>(), "Windows ne donne pas les compteurs SMART de ce disque"));
                continue;
            }
            etats.Add(new EtatDisque(nom, r.Unavailable is null,
                r.Problems.Where(p => p.Severity == Severity.Critical).Select(p => p.Message).ToList(),
                r.Problems.Where(p => p.Severity == Severity.Warning).Select(p => p.Message).ToList(),
                r.Unavailable));
        }
        return etats;
    }
}
