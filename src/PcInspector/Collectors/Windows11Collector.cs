using System.Linq;
using System.Text.RegularExpressions;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Vérifie les exigences officielles de Windows 11 et donne un verdict clair.</summary>
public sealed class Windows11Collector : ICollector
{
    private const ulong Gb = 1024UL * 1024 * 1024;

    public string Title => "Windows 11";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        var (checks, allOk, unsure) = RunChecks();

        var os = Wmi.Query("SELECT Caption, BuildNumber FROM Win32_OperatingSystem").FirstOrDefault();
        var isWin11 = int.TryParse(os?.Str("BuildNumber"), out var build) && build >= 22000;

        var verdict = new InfoGroup("Verdict")
            .Add("Système actuel", os?.Str("Caption"))
            .Add("Compatible Windows 11", allOk && !unsure ? "Oui"
                : allOk ? "Probablement : vérifier le processeur dans la liste Microsoft"
                : "Non : voir les points marqués ✘");
        if (isWin11) verdict.Add("Remarque", "Windows 11 est déjà installé.");

        section.Groups.Add(verdict);
        section.Groups.Add(checks);
        return section;
    }

    /// <summary>true si compatible, false si une exigence manque, null si le processeur n'est pas reconnu.</summary>
    internal static bool? IsCompatible()
    {
        var (_, allOk, unsure) = RunChecks();
        return !allOk ? false : unsure ? null : true;
    }

    private static (InfoGroup Checks, bool AllOk, bool Unsure) RunChecks()
    {
        var checks = new InfoGroup("Exigences");
        bool allOk = true;
        bool unsure = false;

        void Check(string label, bool? ok, string detail)
        {
            checks.Add(label, (ok switch { true => "✔ ", false => "✘ ", null => "? " }) + detail);
            if (ok == false) allOk = false;
            if (ok is null) unsure = true;
        }

        var cpu = Wmi.Query("SELECT Name, NumberOfCores, MaxClockSpeed, AddressWidth FROM Win32_Processor").FirstOrDefault();
        var cpuName = cpu?.Str("Name")?.Trim() ?? "";
        Check("Processeur", IsCpuSupported(cpuName), cpuName.Length > 0 ? cpuName : "inconnu");
        Check("Cœurs et fréquence", cpu?.ULong("NumberOfCores") >= 2 && cpu?.ULong("MaxClockSpeed") >= 1000,
            $"{cpu?.ULong("NumberOfCores")} cœurs à {cpu?.ULong("MaxClockSpeed")} MHz (min. 2 cœurs à 1 GHz)");
        Check("64 bits", cpu?.ULong("AddressWidth") == 64, cpu?.ULong("AddressWidth") == 64 ? "Oui" : "Non");

        var ram = Wmi.Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault()?.ULong("TotalPhysicalMemory") ?? 0;
        // La mémoire réservée au matériel fait parfois afficher 3,8 Go pour 4 Go installés.
        Check("Mémoire", ram >= 3.5 * Gb, $"{Wmi.FormatBytes(ram)} (min. 4 Go)");

        var systemDrive = System.Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var disk = Wmi.Query($"SELECT Size FROM Win32_LogicalDisk WHERE DeviceID = '{systemDrive}'").FirstOrDefault()?.ULong("Size") ?? 0;
        Check("Disque système", disk >= 60 * Gb, $"{Wmi.FormatBytes(disk)} (min. 64 Go)");

        Check("Démarrage UEFI", Firmware.IsUefi(), Firmware.IsUefi() ? "UEFI" : "Legacy : à passer en UEFI (disque en GPT)");
        var secureBoot = Firmware.SecureBootEnabled();
        Check("Secure Boot", secureBoot is not null, secureBoot switch
        {
            true => "Activé",
            false => "Pris en charge mais désactivé : à activer dans le BIOS",
            null => "Non pris en charge",
        });

        var tpm = Wmi.QueryOptional("SELECT SpecVersion, IsEnabled_InitialValue FROM Win32_Tpm", @"root\CIMV2\Security\MicrosoftTpm").FirstOrDefault();
        var tpmVersion = tpm?.Str("SpecVersion")?.Split(',').FirstOrDefault()?.Trim();
        var tpmEnabled = tpm?.Bool("IsEnabled_InitialValue") == true;
        Check("TPM 2.0", tpm is null ? false : tpmVersion == "2.0" && tpmEnabled,
            tpm is null ? "Aucun TPM détecté (ou désactivé dans le BIOS : fTPM / PTT)" : $"TPM {tpmVersion}" + (tpmEnabled ? "" : " désactivé"));
        return (checks, allOk, unsure);
    }

    /// <summary>
    /// Approximation de la liste Microsoft : Intel Core 8e génération et plus, AMD Ryzen 2000 et plus.
    /// null quand le modèle n'est pas reconnu.
    /// </summary>
    internal static bool? IsCpuSupported(string name)
    {
        if (Regex.IsMatch(name, @"Core\(TM\) Ultra|Core Ultra|Intel\(R\) Core\(TM\) [3579] ", RegexOptions.IgnoreCase)) return true;

        var intel = Regex.Match(name, @"\bi[3579]-(\d{4,5})", RegexOptions.IgnoreCase);
        if (intel.Success)
        {
            var digits = intel.Groups[1].Value;
            var generation = digits.Length == 5 ? int.Parse(digits.Substring(0, 2)) : int.Parse(digits.Substring(0, 1));
            return generation >= 8;
        }

        var ryzen = Regex.Match(name, @"Ryzen\s+(?:Threadripper\s+)?(?:PRO\s+)?\d\s+(?:PRO\s+)?(\d)\d{3}", RegexOptions.IgnoreCase);
        if (ryzen.Success) return ryzen.Groups[1].Value != "1";
        if (Regex.IsMatch(name, @"Ryzen\s+(AI|Z)\b", RegexOptions.IgnoreCase)) return true;

        if (Regex.IsMatch(name, @"Snapdragon|Microsoft SQ", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(name, @"Core\(TM\)2|Pentium\(R\) (D|4)|Athlon\(tm\) (64|II)|Phenom|\bFX-", RegexOptions.IgnoreCase)) return false;
        return null;
    }
}
