using System;
using System.Linq;
using Microsoft.Win32;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Clés de licence Windows et Office, à noter avant une réinstallation (comme ProduKey).</summary>
public sealed class LicenseCollector : ICollector
{
    private const string WindowsAppId = "55c92734-d682-4d71-983e-d6ec3f16059f";
    private const string KeyChars = "BCDFGHJKMPQRTVWXY2346789";

    // Clés génériques installées par Windows quand la licence est numérique (liée à la carte mère ou au compte).
    private static readonly (string Key, string Edition)[] GenericKeys =
    {
        ("YTMG3-N6DKC-DKB77-7M9GH-8HVX7", "Famille"),
        ("4CPRK-NM3K3-X6XXQ-RXX86-WXCHW", "Famille N"),
        ("BT79Q-G7N6G-PGBYW-4YWX6-6F4BT", "Famille langue unique"),
        ("VK7JG-NPHTM-C97JM-9MPGT-3V66T", "Professionnel"),
        ("2B87N-8KFHP-DKV6R-Y2C8J-PKCKT", "Professionnel N"),
        ("8PTT6-RNW4C-6V7J2-C2D3X-MHBPB", "Professionnel Éducation"),
        ("DXG7C-N36C4-C4HTG-X4T3X-2YV77", "Professionnel pour stations de travail"),
        ("YNMGQ-8RYV3-4PGQ3-C8XTP-7CFBY", "Éducation"),
        ("XGVPP-NMH47-7TTHJ-W3FW7-8HV2C", "Entreprise"),
    };

    // Office 2010 et plus ancien gardent la clé complète dans le registre, à cet emplacement du DigitalProductId.
    private static readonly (string Version, string Name, int Offset)[] OldOffice =
    {
        ("14.0", "Office 2010", 808),
        ("12.0", "Office 2007", 52),
        ("11.0", "Office 2003", 52),
    };

    public string Title => "Licences";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        section.Groups.Add(Windows());
        section.Groups.Add(Office());
        return section;
    }

    private static InfoGroup Windows()
    {
        var group = new InfoGroup("Windows");
        var os = Wmi.Query("SELECT Caption FROM Win32_OperatingSystem").FirstOrDefault();
        group.Add("Édition", os?.Str("Caption"));

        var product = Wmi.QueryOptional(
                "SELECT LicenseStatus, PartialProductKey, Description FROM SoftwareLicensingProduct " +
                $"WHERE ApplicationID = '{WindowsAppId}' AND PartialProductKey IS NOT NULL")
            .FirstOrDefault();
        var partial = product?.Str("PartialProductKey");
        group.Add("Activation", Status(product?.ULong("LicenseStatus")));

        string? installed = null;
        using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            if (key?.GetValue("DigitalProductId") is byte[] id) installed = DecodeKey(id, 52);
        }

        if (installed is not null && partial is not null && !installed.EndsWith(partial, StringComparison.OrdinalIgnoreCase))
        {
            // Licence en volume (KMS/MAK) : le registre ne contient pas la vraie clé.
            group.Add("Clé installée", "Non lisible (licence entreprise ou en volume)");
        }
        else
        {
            group.Add("Clé installée", installed);
            var generic = GenericKeys.FirstOrDefault(g => g.Key == installed);
            if (generic.Key is not null)
                group.Add("Type de licence", $"Clé générique {generic.Edition} : licence numérique liée à la carte mère ou au compte Microsoft. " +
                                              "Pas besoin de clé pour réinstaller la même édition.");
        }

        if (partial is not null) group.Add("Fin de la clé active", "XXXXX-XXXXX-XXXXX-XXXXX-" + partial);
        group.Add("Canal", Channel(product?.Str("Description")));

        var oemKey = Wmi.QueryOptional("SELECT OA3xOriginalProductKey FROM SoftwareLicensingService").FirstOrDefault()?.Str("OA3xOriginalProductKey");
        group.Add("Clé d'origine dans le BIOS", string.IsNullOrWhiteSpace(oemKey) ? "Aucune" : oemKey);
        return group;
    }

    private static InfoGroup Office()
    {
        var group = new InfoGroup("Microsoft Office");

        // Office 2013 et plus récent : Windows ne garde que les 5 derniers caractères de la clé.
        foreach (var product in Wmi.QueryOptional(
                     "SELECT Name, LicenseStatus, PartialProductKey FROM SoftwareLicensingProduct " +
                     $"WHERE ApplicationID <> '{WindowsAppId}' AND PartialProductKey IS NOT NULL"))
        {
            var name = product.Str("Name") ?? "Office";
            if (name.IndexOf("Office", StringComparison.OrdinalIgnoreCase) < 0) continue;
            group.Add(CleanName(name), $"{Status(product.ULong("LicenseStatus"))} · clé XXXXX-XXXXX-XXXXX-XXXXX-{product.Str("PartialProductKey")}");
        }

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            foreach (var (version, name, offset) in OldOffice)
            {
                using var registration = root.OpenSubKey($@"SOFTWARE\Microsoft\Office\{version}\Registration");
                if (registration is null) continue;
                foreach (var sub in registration.GetSubKeyNames())
                {
                    using var product = registration.OpenSubKey(sub);
                    if (product?.GetValue("DigitalProductId") is not byte[] id) continue;
                    var label = product.GetValue("ProductName") as string ?? name;
                    group.Add(label, DecodeKey(id, offset) ?? "Clé illisible");
                }
            }
        }

        if (group.Items.Count == 0) group.Add("Résultat", "Aucune licence Office trouvée");
        else group.Add("À savoir", "Office 365 et les versions récentes sont liés au compte Microsoft du client : noter l'adresse du compte.");
        return group;
    }

    /// <summary>Décode une clé produit stockée en base 24 dans un DigitalProductId (format Windows 8 et suivants compris).</summary>
    private static string? DecodeKey(byte[] digitalProductId, int offset)
    {
        if (digitalProductId.Length < offset + 15) return null;
        var key = new byte[15];
        Array.Copy(digitalProductId, offset, key, 0, 15);

        var isWin8 = (key[14] / 6 & 1) == 1;
        key[14] = (byte)(key[14] & 0xF7);

        var chars = new char[25];
        var last = 0;
        for (var i = 24; i >= 0; i--)
        {
            var current = 0;
            for (var j = 14; j >= 0; j--)
            {
                current = current * 256 + key[j];
                key[j] = (byte)(current / 24);
                current %= 24;
            }
            chars[i] = KeyChars[current];
            last = current;
        }

        var raw = new string(chars);
        if (isWin8)
        {
            // Depuis Windows 8, le premier caractère est remplacé par un « N » inséré à la position indiquée.
            var rest = raw.Substring(1);
            raw = rest.Substring(0, last) + "N" + rest.Substring(last);
        }

        if (raw.All(c => c == 'B')) return null;
        return string.Join("-", Enumerable.Range(0, 5).Select(i => raw.Substring(i * 5, 5)));
    }

    private static string CleanName(string name) => name.Replace("_", " ").Replace(" edition", "");

    private static string? Status(ulong? status) => status switch
    {
        null => null,
        1 => "Activé",
        0 => "Non activé",
        2 or 3 or 4 or 6 => "Période de grâce",
        5 => "Non authentique",
        _ => "Inconnu",
    };

    private static string? Channel(string? description)
    {
        if (description is null) return null;
        if (description.IndexOf("OEM", StringComparison.OrdinalIgnoreCase) >= 0) return "OEM (fournie avec le PC)";
        if (description.IndexOf("RETAIL", StringComparison.OrdinalIgnoreCase) >= 0) return "Boîte (transférable sur un autre PC)";
        if (description.IndexOf("VOLUME", StringComparison.OrdinalIgnoreCase) >= 0) return "Volume (entreprise)";
        return description;
    }
}
