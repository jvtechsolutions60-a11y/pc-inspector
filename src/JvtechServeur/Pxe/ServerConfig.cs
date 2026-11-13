using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace JvtechServeur.Pxe;

/// <summary>
/// Dossiers du serveur et réglages gardés dans C:\JVTECH-PXE\serveur.ini :
/// mot de passe du partage, jeton du suivi des postes, date de la dernière mise à jour intégrée à l'image.
/// Le mot de passe et le jeton sont chiffrés avec DPAPI (clé de ce PC) : copié ailleurs, le fichier ne les révèle pas.
/// Un ancien serveur.ini avec « MotDePasse= » en clair est relu puis réécrit chiffré.
/// </summary>
internal sealed class ServerConfig
{
    public const string ShareName = "JVTECH-Install";
    public const string ShareUser = "jvtechpxe";
    public const int StatusPort = 8081;

    public static string Root => Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "JVTECH-PXE");
    /// <summary>Contenu de l'ISO de Windows 11, partagé en lecture pour l'installation.</summary>
    public static string InstallFolder => Path.Combine(Root, "Installation");
    /// <summary>Fichiers de démarrage envoyés en TFTP : bootmgfw.efi, Boot\BCD, Boot\boot.sdi, Boot\boot.wim.</summary>
    public static string TftpFolder => Path.Combine(Root, "TFTP");
    public static string WorkFolder => Path.Combine(Root, "Travail");
    public static string ReportsFolder => Path.Combine(Root, "Rapports");
    public static string HistoryPath => Path.Combine(Root, "historique.csv");
    private static string FilePath => Path.Combine(Root, "serveur.ini");

    public string SharePassword { get; set; } = "";
    /// <summary>Jeton secret que PC Maintenance joint à chaque remontée (voir StatusPolicy).</summary>
    public string ReportToken { get; set; } = "";
    /// <summary>Mise à jour cumulative intégrée à l'image (ex. KB5066835), vide si aucune.</summary>
    public string LastUpdate { get; set; } = "";
    public DateTime? LastUpdateDate { get; set; }
    public DateTime? LastCheck { get; set; }
    public bool AutoUpdate { get; set; } = true;
    /// <summary>Adresse choisie quand le mini PC a plusieurs cartes réseau.</summary>
    public string ServerAddress { get; set; } = "";

    /// <summary>Version du script Windows PE de l'image actuelle (0 = image d'avant le garde-fou des disques).</summary>
    public int WinPeVersion { get; set; }

    /// <summary>
    /// Image complète ET Windows PE avec le script actuel : une image plus ancienne (sans confirmation EFFACER ni refus
    /// des PC à plusieurs disques) n'est plus utilisée, il faut la refaire depuis l'ISO.
    /// </summary>
    public bool ImageReady => WinPeVersion >= ImageBuilder.WinPeScriptVersion &&
        File.Exists(Path.Combine(TftpFolder, "bootmgfw.efi")) && File.Exists(Path.Combine(TftpFolder, @"Boot\BCD"))
        && File.Exists(Path.Combine(TftpFolder, @"Boot\boot.wim")) && File.Exists(Path.Combine(InstallFolder, "setup.exe"));

    public static ServerConfig Load()
    {
        var config = new ServerConfig();
        var loadedForMigration = false;
        try
        {
            var values = (File.Exists(FilePath) ? File.ReadAllLines(FilePath) : Array.Empty<string>())
                .Select(l => l.Split(new[] { '=' }, 2))
                .Where(p => p.Length == 2)
                .GroupBy(p => p[0].Trim())
                .ToDictionary(g => g.Key, g => g.First()[1].Trim());
            config.SharePassword = values.TryGetValue("MotDePasseProtege", out var protectedPw) ? Unprotect(protectedPw)
                : values.TryGetValue("MotDePasse", out var pw) ? pw : "";
            config.ReportToken = values.TryGetValue("JetonProtege", out var token) ? Unprotect(token) : "";
            var legacy = values.ContainsKey("MotDePasse");
            if (config.ReportToken.Length == 0 || legacy)
            {
                if (config.ReportToken.Length == 0) config.ReportToken = StatusPolicy.NewToken();
                loadedForMigration = true;
            }
            config.LastUpdate = values.TryGetValue("DerniereMiseAJour", out var kb) ? kb : "";
            config.LastUpdateDate = values.TryGetValue("DateMiseAJour", out var d) && DateTime.TryParse(d, out var date) ? date : null;
            config.LastCheck = values.TryGetValue("DerniereVerification", out var c) && DateTime.TryParse(c, out var check) ? check : null;
            config.AutoUpdate = !values.TryGetValue("MiseAJourAuto", out var auto) || auto != "0";
            config.ServerAddress = values.TryGetValue("Adresse", out var ip) ? ip : "";
            config.WinPeVersion = values.TryGetValue("VersionWinPE", out var pe) && int.TryParse(pe, out var peVersion) ? peVersion : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (config.ReportToken.Length == 0) { config.ReportToken = StatusPolicy.NewToken(); loadedForMigration = true; }
        // Mot de passe en clair d'une ancienne version, ou jeton créé : on réécrit le fichier chiffré tout de suite.
        if (loadedForMigration)
        {
            try { config.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return config;
    }

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("JVTECH-PXE serveur.ini");

    private static string Protect(string value) => value.Length == 0 ? "" :
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.LocalMachine));

    private static string Unprotect(string value)
    {
        try { return value.Length == 0 ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.LocalMachine)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return ""; }
    }

    public void Save()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllLines(FilePath, new[]
        {
            "MotDePasseProtege=" + Protect(SharePassword),
            "JetonProtege=" + Protect(ReportToken),
            "DerniereMiseAJour=" + LastUpdate,
            "DateMiseAJour=" + (LastUpdateDate?.ToString("yyyy-MM-dd") ?? ""),
            "DerniereVerification=" + (LastCheck?.ToString("yyyy-MM-dd HH:mm") ?? ""),
            "MiseAJourAuto=" + (AutoUpdate ? "1" : "0"),
            "Adresse=" + ServerAddress,
            "VersionWinPE=" + WinPeVersion,
        });
    }

    /// <summary>Mot de passe du compte de partage : lettres et chiffres seulement, pour le script de Windows PE.</summary>
    public static string NewPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        var bytes = new byte[20];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return "Jv" + new string(bytes.Select(b => chars[b % chars.Length]).ToArray()) + "7";
    }

    /// <summary>Adresses IPv4 des cartes réseau actives (câble de préférence), la passerelle en tête.</summary>
    public static List<(IPAddress Address, IPAddress Mask, string Name)> Addresses()
    {
        var list = new List<(IPAddress, IPAddress, string, bool)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var props = nic.GetIPProperties();
            var hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            foreach (var unicast in props.UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork))
                list.Add((unicast.Address, unicast.IPv4Mask, nic.Name, hasGateway));
        }
        return list.OrderByDescending(a => a.Item4).Select(a => (a.Item1, a.Item2, a.Item3)).ToList();
    }
}
