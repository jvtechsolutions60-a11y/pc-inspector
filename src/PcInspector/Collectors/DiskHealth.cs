using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Ce que les compteurs SMART d'un disque disent de sa santé.</summary>
public sealed class DiskHealthResult
{
    /// <summary>Lignes pour l'onglet Stockage ; les valeurs jugées commencent par ✔, ! ou ✘.</summary>
    public List<(string Label, string Value)> Lines { get; } = new();

    /// <summary>Problèmes pour le bilan, formulés après le nom du disque (« a 12 secteurs réalloués… »).</summary>
    public List<(Severity Severity, string Message)> Problems { get; } = new();

    internal void Line(string label, string value, Severity? status = null) =>
        Lines.Add((label, status is { } s ? $"{Mark(s)} {value}" : value));

    internal void Problem(Severity severity, string message) => Problems.Add((severity, message));

    private static string Mark(Severity s) => s switch
    {
        Severity.Critical => "✘",
        Severity.Warning => "!",
        _ => "✔",
    };
}

/// <summary>
/// Lit les compteurs SMART qui annoncent une panne, à la manière de Scrutiny :
/// chaque compteur est jugé avec des seuils tirés des statistiques de pannes de Backblaze,
/// plutôt qu'avec le seul état « sain » donné par le fabricant. Lecture seule, rien n'est écrit sur le disque.
/// </summary>
internal static class DiskHealth
{
    /// <summary>Résultats indexés par numéro de disque (Win32_DiskDrive.Index = MSFT_PhysicalDisk.DeviceId).</summary>
    public static Dictionary<string, DiskHealthResult> ReadAll()
    {
        var results = new Dictionary<string, DiskHealthResult>();
        try { ReadAta(results); }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException) { }

        foreach (var disk in Wmi.QueryOptional("SELECT DeviceId, BusType FROM MSFT_PhysicalDisk", Wmi.StorageScope))
        {
            if (disk.ULong("BusType") != 17 || disk.Str("DeviceId") is not { } id || results.ContainsKey(id)) continue;
            if (ReadNvmeLog(id) is { } log) results[id] = JudgeNvme(log);
        }
        return results;
    }

    // ---------- Disques SATA / ATA : attributs SMART via WMI ----------

    private const byte Reallocated = 5, SpinRetry = 10, PowerOnHours = 9, ReportedUncorrectable = 187,
                        Temperature = 194, Pending = 197, Uncorrectable = 198, CrcErrors = 199;

    private sealed record SmartAttribute(byte Id, byte Current, ulong Raw);

    private static void ReadAta(Dictionary<string, DiskHealthResult> results)
    {
        var data = Wmi.QueryOptional("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictData", Wmi.WmiScope);
        if (data.Count == 0) return;

        var predict = Wmi.QueryOptional("SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", Wmi.WmiScope)
            .Where(p => p.Str("InstanceName") is not null)
            .GroupBy(p => p.Str("InstanceName")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Bool("PredictFailure"), StringComparer.OrdinalIgnoreCase);
        var thresholds = Wmi.QueryOptional("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictThresholds", Wmi.WmiScope)
            .Where(p => p.Str("InstanceName") is not null)
            .GroupBy(p => p.Str("InstanceName")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => ParseThresholds(g.First()["VendorSpecific"] as byte[]), StringComparer.OrdinalIgnoreCase);

        // L'InstanceName WMI vaut le PNPDeviceID du disque suivi de "_0".
        var drives = Wmi.QueryOptional("SELECT Index, PNPDeviceID FROM Win32_DiskDrive")
            .Where(d => d.Str("PNPDeviceID") is not null && d.Str("Index") is not null)
            .ToList();

        foreach (var entry in data)
        {
            if (entry.Str("InstanceName") is not { } instance || entry["VendorSpecific"] is not byte[] raw) continue;
            var cut = instance.LastIndexOf('_');
            var pnp = cut > 0 ? instance.Substring(0, cut) : instance;
            var drive = drives.FirstOrDefault(d => string.Equals(d.Str("PNPDeviceID"), pnp, StringComparison.OrdinalIgnoreCase));
            if (drive?.Str("Index") is not { } index) continue;

            predict.TryGetValue(instance, out var failing);
            thresholds.TryGetValue(instance, out var limits);
            results[index] = JudgeAta(ParseAttributes(raw), limits ?? new Dictionary<byte, byte>(), failing == true);
        }
    }

    /// <summary>Table SMART ATA : 2 octets d'en-tête puis 30 entrées de 12 octets (id, drapeaux, valeur, pire, 6 octets bruts).</summary>
    private static Dictionary<byte, SmartAttribute> ParseAttributes(byte[] raw)
    {
        var attributes = new Dictionary<byte, SmartAttribute>();
        for (int o = 2; o + 12 <= raw.Length && o < 2 + 30 * 12; o += 12)
        {
            var id = raw[o];
            if (id == 0 || attributes.ContainsKey(id)) continue;
            ulong value = 0;
            for (int i = 5; i >= 0; i--) value = (value << 8) | raw[o + 5 + i];
            attributes[id] = new SmartAttribute(id, raw[o + 3], value);
        }
        return attributes;
    }

    private static Dictionary<byte, byte> ParseThresholds(byte[]? raw)
    {
        var limits = new Dictionary<byte, byte>();
        if (raw is null) return limits;
        for (int o = 2; o + 12 <= raw.Length && o < 2 + 30 * 12; o += 12)
            if (raw[o] != 0) limits[raw[o]] = raw[o + 1];
        return limits;
    }

    private static DiskHealthResult JudgeAta(Dictionary<byte, SmartAttribute> attrs, Dictionary<byte, byte> limits, bool predictFailure)
    {
        var r = new DiskHealthResult();

        if (predictFailure)
        {
            r.Line("Alerte SMART", "Le disque annonce une panne prochaine", Severity.Critical);
            r.Problem(Severity.Critical, "annonce lui-même une panne prochaine (SMART) : sauvegarder immédiatement et le remplacer.");
        }
        else r.Line("Alerte SMART", "Aucune", Severity.Ok);

        // Seuils inspirés de Scrutiny / Backblaze : dès le premier secteur défectueux, le risque de panne grimpe nettement.
        if (attrs.TryGetValue(Reallocated, out var a5))
        {
            var s = a5.Raw >= 50 ? Severity.Critical : a5.Raw > 0 ? Severity.Warning : Severity.Ok;
            r.Line("Secteurs réalloués", a5.Raw.ToString(), s);
            if (s != Severity.Ok)
                r.Problem(s, $"a {a5.Raw} secteur(s) réalloué(s) : sauvegarder et prévoir son remplacement.");
        }

        ulong unreadable = 0;
        if (attrs.TryGetValue(Pending, out var a197))
        {
            r.Line("Secteurs en attente", a197.Raw.ToString(), a197.Raw > 0 ? Severity.Critical : Severity.Ok);
            unreadable = Math.Max(unreadable, a197.Raw);
        }
        if (attrs.TryGetValue(Uncorrectable, out var a198))
        {
            r.Line("Secteurs irrécupérables", a198.Raw.ToString(), a198.Raw > 0 ? Severity.Critical : Severity.Ok);
            unreadable = Math.Max(unreadable, a198.Raw);
        }
        if (unreadable > 0)
            r.Problem(Severity.Critical, $"a {unreadable} secteur(s) illisible(s) : sauvegarder immédiatement et le remplacer.");

        if (attrs.TryGetValue(ReportedUncorrectable, out var a187))
        {
            var s = a187.Raw >= 10 ? Severity.Critical : a187.Raw > 0 ? Severity.Warning : Severity.Ok;
            r.Line("Erreurs non corrigées", a187.Raw.ToString(), s);
            if (s != Severity.Ok)
                r.Problem(s, $"a signalé {a187.Raw} erreur(s) de lecture non corrigée(s) : sauvegarder et surveiller.");
        }

        if (attrs.TryGetValue(SpinRetry, out var a10))
        {
            r.Line("Relances du moteur", a10.Raw.ToString(), a10.Raw > 0 ? Severity.Warning : Severity.Ok);
            if (a10.Raw > 0)
                r.Problem(Severity.Warning, $"a eu {a10.Raw} difficulté(s) à démarrer son moteur : sauvegarder et surveiller.");
        }

        // Les erreurs CRC viennent presque toujours du câble ou du connecteur, pas du disque.
        if (attrs.TryGetValue(CrcErrors, out var a199))
        {
            var s = a199.Raw >= 10 ? Severity.Warning : (Severity?)null;
            r.Line("Erreurs de transmission (CRC)", a199.Raw >= 10 ? $"{a199.Raw} : vérifier le câble SATA" : a199.Raw.ToString(), s);
            if (s is not null)
                r.Problem(Severity.Warning, $"a {a199.Raw} erreurs de transmission : vérifier ou changer le câble SATA.");
        }

        var below = attrs.Values
            .Where(a => limits.TryGetValue(a.Id, out var t) && t > 0 && t < 0xFE && a.Current <= t)
            .Select(a => a.Id.ToString())
            .ToList();
        if (below.Count > 0)
        {
            r.Line("Sous le seuil du fabricant", "Attribut(s) " + string.Join(", ", below), Severity.Critical);
            r.Problem(Severity.Critical, "a un indicateur SMART sous le seuil du fabricant : sauvegarder et le remplacer.");
        }

        if (attrs.TryGetValue(PowerOnHours, out var a9)) r.Line("Heures de fonctionnement", $"{a9.Raw & 0xFFFFFFFF} h");
        if (attrs.TryGetValue(Temperature, out var a194) && (a194.Raw & 0xFF) is > 0 and < 120 and var t194)
            r.Line("Température", $"{t194} °C");

        return r;
    }

    // ---------- SSD NVMe : journal de santé (log page 02h) ----------

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const int StorageDeviceProtocolSpecificProperty = 50, ProtocolTypeNvme = 3, NvmeDataTypeLogPage = 2;
    private const int NvmeHealthLog = 2, LogSize = 512, SpecificDataSize = 40, HeaderSize = 8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize,
                                               byte[] output, int outputSize, out int returned, IntPtr overlapped);

    /// <summary>Demande au SSD son journal de santé. Simple requête de lecture : rien n'est modifié.</summary>
    private static byte[]? ReadNvmeLog(string diskNumber)
    {
        const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareReadWrite = 3, OpenExisting = 3;
        var path = @"\\.\PhysicalDrive" + diskNumber;

        // Certains pilotes exigent un accès lecture-écriture pour répondre à la requête, même si elle ne fait que lire.
        foreach (var access in new[] { GenericRead, GenericRead | GenericWrite })
        {
            using var handle = CreateFile(path, access, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid) continue;

            // STORAGE_PROPERTY_QUERY suivi de STORAGE_PROTOCOL_SPECIFIC_DATA puis de la place pour le journal.
            var buffer = new byte[HeaderSize + SpecificDataSize + LogSize];
            void Put(int offset, int value) => BitConverter.GetBytes(value).CopyTo(buffer, offset);
            Put(0, StorageDeviceProtocolSpecificProperty);
            Put(4, 0);                       // PropertyStandardQuery
            Put(8, ProtocolTypeNvme);
            Put(12, NvmeDataTypeLogPage);
            Put(16, NvmeHealthLog);
            Put(20, 0);
            Put(24, SpecificDataSize);       // ProtocolDataOffset
            Put(28, LogSize);                // ProtocolDataLength

            if (!DeviceIoControl(handle, IoctlStorageQueryProperty, buffer, buffer.Length, buffer, buffer.Length, out _, IntPtr.Zero))
                continue;

            // Réponse : STORAGE_PROTOCOL_DATA_DESCRIPTOR (Version, Size, puis les mêmes données de protocole).
            var offset = HeaderSize + BitConverter.ToInt32(buffer, HeaderSize + 16);
            var length = BitConverter.ToInt32(buffer, HeaderSize + 20);
            if (length < LogSize || offset < HeaderSize || offset + LogSize > buffer.Length) continue;

            var log = new byte[LogSize];
            Array.Copy(buffer, offset, log, 0, LogSize);
            return log;
        }
        return null;
    }

    private static DiskHealthResult JudgeNvme(byte[] log)
    {
        var r = new DiskHealthResult();
        ulong U64(int offset) => BitConverter.ToUInt64(log, offset);   // les compteurs font 16 octets, les 8 premiers suffisent

        var warning = log[0];
        if (warning != 0)
        {
            var reasons = new List<string>();
            if ((warning & 0x01) != 0) reasons.Add("réserve épuisée");
            if ((warning & 0x02) != 0) reasons.Add("température");
            if ((warning & 0x04) != 0) reasons.Add("fiabilité dégradée");
            if ((warning & 0x08) != 0) reasons.Add("passé en lecture seule");
            if ((warning & 0x10) != 0) reasons.Add("sauvegarde de secours en échec");
            var text = reasons.Count > 0 ? string.Join(", ", reasons) : $"code {warning}";
            r.Line("Alerte critique", text, Severity.Critical);
            r.Problem(Severity.Critical, $"lève une alerte critique ({text}) : sauvegarder immédiatement et le remplacer.");
        }
        else r.Line("Alerte critique", "Aucune", Severity.Ok);

        int spare = log[3], spareThreshold = log[4], used = log[5];
        if (spare <= 100)
        {
            var s = spare < spareThreshold ? Severity.Critical : spare < 50 ? Severity.Warning : Severity.Ok;
            r.Line("Réserve disponible", $"{spare} % (seuil {spareThreshold} %)", s);
            if (s == Severity.Critical)
                r.Problem(s, $"n'a plus que {spare} % de réserve, sous le seuil du fabricant : sauvegarder et le remplacer.");
            else if (s == Severity.Warning)
                r.Problem(s, $"a entamé sa réserve ({spare} % restants) : prévoir son remplacement.");
        }

        r.Line("Vie consommée", $"{used} %", used >= 100 ? Severity.Critical : used >= 80 ? Severity.Warning : Severity.Ok);

        // Scrutiny considère une seule erreur média comme un échec du SSD.
        var mediaErrors = U64(160);
        r.Line("Erreurs de données (média)", mediaErrors.ToString(), mediaErrors > 0 ? Severity.Critical : Severity.Ok);
        if (mediaErrors > 0)
            r.Problem(Severity.Critical, $"a enregistré {mediaErrors} erreur(s) de données : sauvegarder et prévoir son remplacement.");

        r.Line("Arrêts brutaux", U64(144).ToString());
        r.Line("Données écrites", Wmi.FormatBytes(U64(48) * 512_000));
        r.Line("Heures de fonctionnement", $"{U64(128)} h");
        if (BitConverter.ToUInt16(log, 1) is > 273 and var kelvin && kelvin - 273 < 120)
            r.Line("Température", $"{kelvin - 273} °C");
        return r;
    }
}
