using System;
using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Recommandations à proposer au client, déduites des autres informations.</summary>
public sealed class AdviceCollector : ICollector
{
    private const ulong Gb = 1024UL * 1024 * 1024;

    public string Title => "Conseils";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        var advice = new InfoGroup("Recommandations");

        // Mémoire
        var ram = Wmi.Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault()?.ULong("TotalPhysicalMemory") ?? 0;
        var used = (ulong)Wmi.Query("SELECT Capacity FROM Win32_PhysicalMemory").Count;
        var slots = Wmi.QueryOptional("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray").FirstOrDefault()?.ULong("MemoryDevices");
        if (ram > 0 && ram < 7.5 * Gb)
        {
            var free = slots is { } s && s > used ? $"{s - used} emplacement(s) libre(s)" : "aucun emplacement libre, remplacer les barrettes";
            advice.Add("Mémoire", $"{Wmi.FormatBytes(ram)} seulement : passer à 8 ou 16 Go ({free}).");
        }

        // Disque système
        var systemLetter = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd(':');
        var partition = Wmi.QueryOptional($"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '{systemLetter}'", Wmi.StorageScope).FirstOrDefault();
        var diskNumber = partition?.Str("DiskNumber");
        foreach (var disk in Wmi.QueryOptional("SELECT DeviceId, FriendlyName, MediaType, HealthStatus FROM MSFT_PhysicalDisk", Wmi.StorageScope))
        {
            var name = disk.Str("FriendlyName") ?? "Disque";
            if (disk.Str("DeviceId") == diskNumber && disk.ULong("MediaType") == 3)
                advice.Add("Disque système", $"{name} est un disque dur mécanique : le remplacer par un SSD rendra le PC beaucoup plus rapide.");
            switch (disk.ULong("HealthStatus"))
            {
                case 1: advice.Add("Santé disque", $"{name} signale un avertissement : sauvegarder les données et prévoir son remplacement."); break;
                case 2: advice.Add("Santé disque", $"{name} est en mauvais état : sauvegarder immédiatement et le remplacer."); break;
            }
        }

        var volume = Wmi.Query($"SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DeviceID = '{systemLetter}:'").FirstOrDefault();
        if (volume?.ULong("Size") is > 0 and var size && volume.ULong("FreeSpace") is { } freeSpace && freeSpace * 100 / size < 15)
            advice.Add("Espace disque", $"Il ne reste que {Wmi.FormatBytes(freeSpace)} sur {systemLetter}: : faire un nettoyage ou prévoir un disque plus grand.");

        // Batterie
        if (BatteryCollector.WearPercent() is >= 40 and var wear)
            advice.Add("Batterie", $"Usure de {wear:0} % : proposer le remplacement de la batterie.");

        // Périphériques et plantages
        var errors = Wmi.Query("SELECT Name FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0").Count;
        if (errors > 0)
            advice.Add("Pilotes", $"{errors} périphérique(s) en erreur : voir l'onglet Diagnostic.");

        // Sécurité
        var antivirus = Wmi.QueryOptional("SELECT productState FROM AntiVirusProduct", @"root\SecurityCenter2");
        if (antivirus.Count > 0 && antivirus.All(av => (((av.ULong("productState") ?? 0) >> 8) & 0xFF) is not (0x10 or 0x11)))
            advice.Add("Antivirus", "Aucun antivirus actif : réactiver Windows Defender ou installer une protection.");
        if (SecurityCollector.IsRebootPending())
            advice.Add("Mises à jour", "Un redémarrage est nécessaire pour terminer des mises à jour.");

        // Démarrage
        var boot = Wmi.Query("SELECT LastBootUpTime FROM Win32_OperatingSystem").FirstOrDefault()?.Str("LastBootUpTime");
        if (!string.IsNullOrEmpty(boot) && DateTime.Now - System.Management.ManagementDateTimeConverter.ToDateTime(boot) > TimeSpan.FromDays(7))
            advice.Add("Redémarrage", "Le PC n'a pas redémarré depuis plus d'une semaine (le démarrage rapide de Windows peut en être la cause).");

        if (advice.Items.Count == 0) advice.Add("Résultat", "Rien à signaler, ce PC est en bon état.");
        section.Groups.Add(advice);
        return section;
    }
}
