using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;
using PcInspector.Collectors;

namespace Jvtech.Lecture.Collecte;

/// <summary>
/// Inventaire du PC pour la fiche et l'annonce (lecture seule : WMI et registre).
/// Chaque information garde sa source ; ce qui n'est pas lu reste « Non détecté » (ajustement 6).
/// La mise en forme est faite par des fonctions sans accès au PC, testées automatiquement.
/// </summary>
public static class Inventaire
{
    public static IReadOnlyList<Champ> Lire()
    {
        var cs = Wmi.QueryOptional("SELECT Manufacturer, Model, SystemSKUNumber FROM Win32_ComputerSystem").FirstOrDefault();
        var produit = Wmi.QueryOptional("SELECT Version FROM Win32_ComputerSystemProduct").FirstOrDefault();
        var fabricant = cs?.Str("Manufacturer");
        var (modele, reference) = ChoisirModele(fabricant, cs?.Str("Model"), produit?.Str("Version"), cs?.Str("SystemSKUNumber"));

        var cpu = Wmi.QueryOptional("SELECT Name FROM Win32_Processor").Select(p => p.Str("Name")).FirstOrDefault();

        var barrettes = Wmi.QueryOptional("SELECT Capacity, SMBIOSMemoryType FROM Win32_PhysicalMemory");
        var memoire = FormatMemoire(
            barrettes.Aggregate(0UL, (s, b) => s + (b.ULong("Capacity") ?? 0)),
            barrettes.Count,
            barrettes.Select(b => TypeMemoire(b.ULong("SMBIOSMemoryType"))).FirstOrDefault(t => t is not null));

        var disques = Wmi.QueryOptional("SELECT FriendlyName, Size, MediaType, BusType FROM MSFT_PhysicalDisk", Wmi.StorageScope)
            .Select(d => FormatDisque(d.ULong("MediaType"), d.ULong("BusType"), d.ULong("Size"), d.Str("FriendlyName")))
            .Where(d => d is not null).ToList();

        var cartes = Wmi.QueryOptional("SELECT Name, CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController");
        var graphique = string.Join(" + ", cartes.Select(c => c.Str("Name")).Where(EstVraieCarte).Select(n => n!.Trim()).Distinct());
        var (largeur, hauteur) = cartes.Select(c => (c.ULong("CurrentHorizontalResolution"), c.ULong("CurrentVerticalResolution")))
            .FirstOrDefault(r => r.Item1 is > 0 && r.Item2 is > 0);
        var edid = Wmi.QueryOptional("SELECT MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams", Wmi.WmiScope).FirstOrDefault();
        var ecran = FormatEcran(edid?.ULong("MaxHorizontalImageSize"), edid?.ULong("MaxVerticalImageSize"), largeur, hauteur);

        var os = Wmi.QueryOptional("SELECT Caption, BuildNumber FROM Win32_OperatingSystem").FirstOrDefault();
        var windows = FormatWindows(os?.Str("Caption"), LireVersionAffichee(), os?.Str("BuildNumber"));

        return new List<Champ>
        {
            new("Fabricant", fabricant, "Win32_ComputerSystem") { Id = "fabricant" },
            new("Modèle", modele, "Win32_ComputerSystem / Win32_ComputerSystemProduct") { Id = "modele" },
            new("Référence", reference, "Win32_ComputerSystem") { Id = "reference" },
            new("N° de série", Wmi.QueryOptional("SELECT SerialNumber FROM Win32_BIOS").FirstOrDefault()?.Str("SerialNumber"), "Win32_BIOS") { Id = "serie" },
            new("Processeur", cpu, "Win32_Processor") { Id = "processeur" },
            new("Mémoire", memoire, "Win32_PhysicalMemory") { Id = "memoire" },
            new("Stockage", disques.Count == 0 ? null : string.Join(" + ", disques), "MSFT_PhysicalDisk") { Id = "stockage" },
            new("Carte graphique", graphique, "Win32_VideoController") { Id = "graphique" },
            new("Écran", ecran, "WmiMonitorBasicDisplayParams (EDID) / Win32_VideoController") { Id = "ecran" },
            new("Windows", windows, "Win32_OperatingSystem") { Id = "windows" },
        };
    }

    private static string? LireVersionAffichee()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("DisplayVersion") as string;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Lenovo met le nom commercial dans Win32_ComputerSystemProduct.Version (« ThinkPad T480 ») et la référence
    /// machine dans Model (« 20L5CTO1WW ») ; les autres marques mettent le nom commercial dans Model.
    /// </summary>
    public static (string? Modele, string? Reference) ChoisirModele(string? fabricant, string? model, string? version, string? sku)
    {
        var estLenovo = (fabricant ?? "").IndexOf("lenovo", StringComparison.OrdinalIgnoreCase) >= 0;
        if (estLenovo && Champ.Nettoyer(version) is { } nom && !nom.Equals("Lenovo", StringComparison.OrdinalIgnoreCase))
            return (nom, Champ.Nettoyer(model));
        return (Champ.Nettoyer(model), Champ.Nettoyer(sku));
    }

    public static string? FormatMemoire(ulong totalOctets, int barrettes, string? type)
    {
        if (totalOctets == 0) return null;
        var go = totalOctets / (1024.0 * 1024 * 1024);
        var texte = go >= 1 ? $"{Math.Round(go):0} Go" : $"{totalOctets / (1024 * 1024)} Mo";
        if (type is not null) texte += " " + type;
        if (barrettes > 0) texte += barrettes == 1 ? " (1 barrette)" : $" ({barrettes} barrettes)";
        return texte;
    }

    public static string? TypeMemoire(ulong? smbios) => smbios switch
    {
        20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 27 => "LPDDR", 28 => "LPDDR2", 29 => "LPDDR3",
        30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5",
        _ => null,
    };

    /// <summary>
    /// « SSD NVMe 256 Go (Samsung PM981) ». Capacité en Go décimaux, comme sur l'étiquette du disque.
    /// Les disques USB (la clé d'installation, un disque externe) ne font pas partie du PC : null.
    /// Type inconnu : « Disque » sans deviner SSD ou HDD.
    /// </summary>
    public static string? FormatDisque(ulong? mediaType, ulong? busType, ulong? tailleOctets, string? nom)
    {
        if (busType == 7) return null; // USB
        var type = mediaType switch { 3 => "HDD", 4 => "SSD", _ => "Disque" };
        var bus = busType switch { 17 => " NVMe", 11 => " SATA", _ => "" };
        var taille = tailleOctets is > 0 ? " " + FormatCapacite(tailleOctets.Value) : "";
        var modele = Champ.Nettoyer(nom) is { } n ? $" ({n})" : "";
        return $"{type}{bus}{taille}{modele}";
    }

    public static string FormatCapacite(ulong octets)
    {
        var go = octets / 1e9;
        return go >= 1000 ? (go / 1000).ToString("0.#", CultureInfo.GetCultureInfo("fr-FR")) + " To" : $"{Math.Round(go):0} Go";
    }

    private static bool EstVraieCarte(string? nom) =>
        Champ.Nettoyer(nom) is { } n
        && n.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) < 0
        && n.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) < 0
        && n.IndexOf("Hyper-V", StringComparison.OrdinalIgnoreCase) < 0;

    /// <summary>
    /// Diagonale approchée d'après l'EDID (dimensions en cm entiers, donc ±0,5 pouce) et définition actuelle.
    /// Le mot « environ » reste : la taille exacte est à vérifier (et à corriger) par le technicien.
    /// </summary>
    public static string? FormatEcran(ulong? largeurCm, ulong? hauteurCm, ulong? largeurPx, ulong? hauteurPx)
    {
        var morceaux = new List<string>();
        if (largeurCm is > 0 && hauteurCm is > 0)
        {
            var pouces = Math.Sqrt((double)(largeurCm.Value * largeurCm.Value + hauteurCm.Value * hauteurCm.Value)) / 2.54;
            morceaux.Add("environ " + pouces.ToString("0.#", CultureInfo.GetCultureInfo("fr-FR")) + " pouces");
        }
        if (largeurPx is > 0 && hauteurPx is > 0) morceaux.Add($"{largeurPx} × {hauteurPx}");
        return morceaux.Count == 0 ? null : string.Join(", ", morceaux);
    }

    public static string? FormatWindows(string? caption, string? displayVersion, string? build)
    {
        var nom = Champ.Nettoyer(caption)?.Replace("Microsoft ", "");
        if (nom is null) return null;
        var details = new[] { Champ.Nettoyer(displayVersion), Champ.Nettoyer(build) is { } b ? "build " + b : null }.Where(d => d is not null);
        var suite = string.Join(", ", details);
        return suite.Length == 0 ? nom : $"{nom} ({suite})";
    }
}
