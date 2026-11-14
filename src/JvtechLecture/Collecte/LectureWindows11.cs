using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Jvtech.Lecture.Regles;
using Microsoft.Win32;
using PcInspector.Collectors;

namespace Jvtech.Lecture.Collecte;

/// <summary>
/// Lecture des exigences Windows 11, sans droits administrateur : WMI, registre en lecture, liste des périphériques
/// pour le TPM (Win32_Tpm demande les droits administrateur). Les décisions sont dans <see cref="CompatibiliteWindows11"/>.
/// </summary>
public static class LectureWindows11
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFirmwareType(out int type);

    /// <summary>Base des processeurs : win11-processeurs.txt à côté du .exe s'il est plus récent, sinon la base intégrée.</summary>
    public static BaseWindows11 Base(string dossierExe, out string? avertissement)
    {
        var fichier = Path.Combine(dossierExe, BaseWindows11.NomFichier);
        string? texte = null;
        try { if (File.Exists(fichier)) texte = File.ReadAllText(fichier); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return BaseWindows11.Choisir(texte, out avertissement);
    }

    public static MesuresWindows11 Lire()
    {
        var cpu = Wmi.QueryOptional("SELECT Name, NumberOfCores, MaxClockSpeed, AddressWidth FROM Win32_Processor").FirstOrDefault();
        var ram = Wmi.QueryOptional("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault()?.ULong("TotalPhysicalMemory");
        var lecteur = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var disque = Wmi.QueryOptional($"SELECT Size FROM Win32_LogicalDisk WHERE DeviceID = '{lecteur.Replace("'", "")}'").FirstOrDefault()?.ULong("Size");
        var (tpmLu, tpmVersion) = Tpm();
        int? build = null;
        try
        {
            using var cle = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (int.TryParse(cle?.GetValue("CurrentBuild") as string, out var b)) build = b;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }

        return new MesuresWindows11
        {
            Processeur = cpu?.Str("Name")?.Trim(),
            Coeurs = cpu?.ULong("NumberOfCores") is { } c ? (int)c : null,
            FrequenceMhz = cpu?.ULong("MaxClockSpeed") is { } f ? (int)f : null,
            Processeur64Bits = cpu?.ULong("AddressWidth") is { } w ? w == 64 : null,
            MemoireOctets = ram,
            DisqueSystemeOctets = disque,
            Uefi = Uefi(),
            SecureBoot = SecureBoot(),
            TpmLu = tpmLu,
            TpmVersion = tpmVersion,
            BuildWindows = build,
        };
    }

    /// <summary>GetFirmwareType : 1 = BIOS hérité, 2 = UEFI. Null si Windows ne répond pas.</summary>
    private static bool? Uefi()
    {
        try { return GetFirmwareType(out var type) ? type switch { 1 => false, 2 => true, _ => null } : null; }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { return null; }
    }

    private static bool? SecureBoot()
    {
        try
        {
            using var cle = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            return cle?.GetValue("UEFISecureBootEnabled") switch { 1 => true, 0 => false, _ => null };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    /// <summary>TPM vu dans la liste des périphériques (« Module de plateforme sécurisée 2.0 »), lisible sans droits.</summary>
    private static (bool Lu, string? Version) Tpm()
    {
        try
        {
            var noms = Wmi.Query("SELECT Name, Status FROM Win32_PnPEntity WHERE PNPClass = 'SecurityDevices'")
                .Select(d => d.Str("Name") ?? "").ToList();
            return (true, VersionTpm(noms));
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or COMException or UnauthorizedAccessException)
        {
            return (false, null);
        }
    }

    /// <summary>« Trusted Platform Module 2.0 » ou « Module de plateforme sécurisée 2.0 » → 2.0 ; 1.2 de même ; TPM sans version visible → « ? ».</summary>
    public static string? VersionTpm(System.Collections.Generic.IEnumerable<string> nomsPeripheriques)
    {
        foreach (var nom in nomsPeripheriques)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(nom, @"Trusted Platform Module|plateforme sécurisée|TPM", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
            if (nom.Contains("2.0")) return "2.0";
            if (nom.Contains("1.2")) return "1.2";
            return "?";
        }
        return null;
    }
}
