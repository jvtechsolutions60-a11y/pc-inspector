using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jvtech.Lecture.Regles;
using Microsoft.Win32;
using PcInspector.Collectors;
using PcInspector.Models;

namespace Jvtech.Lecture.Collecte;

/// <summary>
/// Collecte de JVTECH Audit, en LECTURE SEULE et sans droits administrateur : WMI, registre ouvert en lecture,
/// journaux, propriétés de l'Explorateur. Aucun fichier écrit, aucun programme lancé, aucune connexion réseau.
/// Chaque partie est isolée : une erreur ou un délai dépassé donne « non lu » (À vérifier), jamais un plantage ni une
/// valeur devinée. Les décisions sont dans <see cref="RegleAudit"/>.
/// </summary>
public static class LectureAudit
{
    private static readonly TimeSpan DelaiPartie = TimeSpan.FromSeconds(20);

    public static DonneesAudit Lire(Action<string>? progression = null)
    {
        var chrono = Stopwatch.StartNew();
        var nonLus = new List<string>();
        T Partie<T>(string nom, Func<T> lecture, T siEchec)
        {
            progression?.Invoke(nom + "…");
            var tache = Task.Run(lecture);
            try
            {
                if (tache.Wait(DelaiPartie)) return tache.Result;
                nonLus.Add($"{nom} : pas de réponse de Windows en {DelaiPartie.TotalSeconds:0} s.");
            }
            catch (AggregateException ex)
            {
                nonLus.Add($"{nom} : lecture impossible ({ex.InnerException?.Message ?? ex.Message}).");
            }
            return siEchec;
        }

        var admin = EstAdministrateur();
        var ident = Partie("Identification", Identification, new Identite());
        var portable = Partie("Type de PC", LectureControles.EstPortable, false);
        var peripheriques = Partie<IReadOnlyList<string>?>("Périphériques", LectureControles.PeripheriquesEnErreur, null);
        var windows = Partie("Windows", Windows, new Systeme());
        var activation = Partie<bool?>("Activation de Windows", Activation, null);
        var w11 = Partie<MesuresWindows11?>("Exigences Windows 11", LectureWindows11.Lire, null);
        var cpu = Partie<(string?, int?, int?)>("Processeur", Processeur, (null, null, null));
        var memoire = Partie("Mémoire", Memoire, new Ram());
        var disques = Partie<IReadOnlyList<DisqueAudit>?>("Disques", Disques, null);
        var volume = Partie<(ulong?, ulong?)>("Espace disque", VolumeSysteme, (null, null));
        var batterie = Partie<MesureBatterie?>("Batterie", () => LectureControles.Batterie(portable), null);
        var securite = Partie("Sécurité", () => Securite(admin), new Protection());
        var admins = Partie<IReadOnlyList<string>?>("Comptes administrateurs", Administrateurs, null);
        var programmes = Partie("Logiciels installés", ProgrammesInstalles, new List<string>());
        var pro = Partie("Environnement professionnel", EnvironnementPro, new Pro());
        var sauvegardes = Partie<IReadOnlyList<string>?>("Sauvegarde", () => Sauvegardes(programmes), null);
        var restauration = Partie<DateTime?>("Points de restauration", DernierPointRestauration, null);
        var reseau = Partie<IReadOnlyList<CarteReseau>?>("Réseau", Reseau, null);

        if (disques is not null && disques.Any(x => !x.Usb && x.Sante is { Lu: false }))
            nonLus.Add("Santé SMART : un ou plusieurs disques non lus" + (admin ? "." : " (souvent faute de droits administrateur)."));
        if (securite.BitLocker is null) nonLus.Add("BitLocker : état du disque système non lu.");
        if (admins is null) nonLus.Add("Comptes administrateurs : liste non lue" + (admin ? "." : " (droits insuffisants)."));
        if (restauration is null && !nonLus.Any(x => x.StartsWith("Points de restauration", StringComparison.Ordinal))) nonLus.Add("Points de restauration : non lus" + (admin ? "." : " (droits administrateur nécessaires)."));
        if (activation is null) nonLus.Add("Activation de Windows : non lue.");

        progression?.Invoke("Terminé");
        return new DonneesAudit
        {
            Fabricant = ident.Fabricant, Modele = ident.Modele, NumeroSerie = ident.Serie, NomPc = ident.NomPc, Bios = ident.Bios,
            Portable = portable, PeripheriquesEnErreur = peripheriques,
            WindowsNom = windows.Nom, EditionId = windows.EditionId, VersionAffichee = windows.VersionAffichee, Build = windows.Build,
            Active = activation, Windows11 = w11,
            Processeur = cpu.Item1, Coeurs = cpu.Item2, Threads = cpu.Item3,
            MemoireOctets = memoire.Total, TypeMemoire = memoire.Type, Barrettes = memoire.Barrettes,
            EmplacementsAnnonces = memoire.Emplacements, MemoireMaxAnnonceeOctets = memoire.Max,
            Disques = disques, VolumeSystemeOctets = volume.Item1, VolumeSystemeLibreOctets = volume.Item2, Batterie = batterie,
            Antivirus = securite.Antivirus, PareFeuTiers = securite.PareFeuTiers, ProfilsPareFeuCoupes = securite.ProfilsCoupes,
            PareFeuParStrategie = securite.PareFeuParStrategie, DerniereMiseAJour = securite.DerniereMiseAJour,
            RedemarrageEnAttente = securite.RedemarrageEnAttente, BitLocker = securite.BitLocker, BitLockerDetail = securite.BitLockerDetail,
            UacActif = securite.Uac, SmartScreenActif = securite.SmartScreen, BureauADistanceOuvert = securite.Rdp,
            Administrateurs = admins, PriseEnMainADistance = PriseEnMain(programmes),
            Domaine = ident.Domaine, NomDomaine = ident.NomDomaine, EntraId = pro.EntraId, CompteProfessionnel = pro.ComptePro,
            Mdm = pro.Mdm, FournisseurMdm = pro.Fournisseur,
            Sauvegardes = sauvegardes, DernierPointRestauration = restauration,
            Reseau = reseau,
            Administrateur = admin, LangueWindows = CultureInfo.InstalledUICulture.Name,
            Date = DateTime.Now, Duree = chrono.Elapsed, NonLus = nonLus,
        };
    }

    public static bool EstAdministrateur()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { return false; }
    }

    // ---------- Identification ----------

    private sealed record Identite
    {
        public string? Fabricant { get; init; }
        public string? Modele { get; init; }
        public string? Serie { get; init; }
        public string? NomPc { get; init; }
        public string? Bios { get; init; }
        public bool? Domaine { get; init; }
        public string? NomDomaine { get; init; }
    }

    private static Identite Identification()
    {
        var cs = Wmi.QueryOptional("SELECT Manufacturer, Model, SystemSKUNumber, Name, PartOfDomain, Domain FROM Win32_ComputerSystem").FirstOrDefault();
        var produit = Wmi.QueryOptional("SELECT Version FROM Win32_ComputerSystemProduct").FirstOrDefault();
        var fabricant = Champ.Nettoyer(cs?.Str("Manufacturer"));
        var (modele, _) = Inventaire.ChoisirModele(fabricant, cs?.Str("Model"), produit?.Str("Version"), cs?.Str("SystemSKUNumber"));
        var bios = Wmi.QueryOptional("SELECT SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS").FirstOrDefault();
        var version = Champ.Nettoyer(bios?.Str("SMBIOSBIOSVersion"));
        var date = bios?.Str("ReleaseDate") is { Length: >= 8 } r ? r.Substring(0, 4) : null;
        return new Identite
        {
            Fabricant = fabricant,
            Modele = modele,
            Serie = Champ.Nettoyer(bios?.Str("SerialNumber")),
            NomPc = cs?.Str("Name"),
            Bios = version is null ? null : date is null ? version : $"{version} ({date})",
            Domaine = cs?.Bool("PartOfDomain"),
            NomDomaine = cs?.Bool("PartOfDomain") == true ? cs.Str("Domain") : null,
        };
    }

    // ---------- Windows ----------

    private sealed record Systeme(string? Nom = null, string? EditionId = null, string? VersionAffichee = null, int? Build = null);

    private static Systeme Windows()
    {
        using var cle = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        int? build = int.TryParse(cle?.GetValue("CurrentBuild") as string, out var b) ? b : null;
        var nom = cle?.GetValue("ProductName") as string;
        // Windows 11 garde « Windows 10 » dans ProductName : seul le build fait foi.
        if (build >= 22000 && nom is not null) nom = nom.Replace("Windows 10", "Windows 11");
        return new Systeme(nom, cle?.GetValue("EditionID") as string, cle?.GetValue("DisplayVersion") as string, build);
    }

    private const string IdWindows = "55c92734-d682-4d71-983e-d6ec3f16059f";

    private static bool? Activation()
    {
        var produit = Wmi.Query($"SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID = '{IdWindows}' AND PartialProductKey IS NOT NULL").FirstOrDefault();
        return produit?.ULong("LicenseStatus") is { } s ? s == 1 : null;
    }

    // ---------- Processeur, mémoire, stockage ----------

    private static (string?, int?, int?) Processeur()
    {
        var cpu = Wmi.QueryOptional("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor").FirstOrDefault();
        return (cpu?.Str("Name")?.Trim(), cpu?.ULong("NumberOfCores") is { } c ? (int)c : null, cpu?.ULong("NumberOfLogicalProcessors") is { } t ? (int)t : null);
    }

    private sealed record Ram(ulong? Total = null, string? Type = null, int? Barrettes = null, int? Emplacements = null, ulong? Max = null);

    private static Ram Memoire()
    {
        var barrettes = Wmi.QueryOptional("SELECT Capacity, SMBIOSMemoryType FROM Win32_PhysicalMemory").Where(m => m.ULong("Capacity") is > 0).ToList();
        var installee = barrettes.Aggregate(0UL, (s, m) => s + (m.ULong("Capacity") ?? 0));
        var total = installee > 0 ? installee : Wmi.QueryOptional("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault()?.ULong("TotalPhysicalMemory");
        var tableau = Wmi.QueryOptional("SELECT MemoryDevices, MaxCapacity, MaxCapacityEx FROM Win32_PhysicalMemoryArray WHERE Use = 3").ToList();
        int? emplacements = tableau.Count == 0 ? null : (int)tableau.Sum(t => (long)(t.ULong("MemoryDevices") ?? 0));
        var maxKo = tableau.Select(t => t.ULong("MaxCapacityEx") is > 0 and var ex ? ex : t.ULong("MaxCapacity")).Where(v => v is > 0).Aggregate(0UL, (s, v) => s + v!.Value);
        return new Ram(total is > 0 ? total : null,
            barrettes.Select(m => Inventaire.TypeMemoire(m.ULong("SMBIOSMemoryType"))).FirstOrDefault(t => t is not null),
            barrettes.Count > 0 ? barrettes.Count : null,
            emplacements is > 0 ? emplacements : null,
            maxKo > 0 ? maxKo * 1024 : null);
    }

    private static IReadOnlyList<DisqueAudit> Disques()
    {
        var sante = DiskHealth.ReadAll();
        var liste = new List<DisqueAudit>();
        foreach (var d in Wmi.Query("SELECT DeviceId, FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk", Wmi.StorageScope))
        {
            var id = d.Str("DeviceId");
            var bus = d.ULong("BusType");
            var nom = Champ.Nettoyer(d.Str("FriendlyName")) ?? "Disque " + id;
            EtatDisque? etat = null;
            if (bus != 7 && id is not null)
            {
                etat = sante.TryGetValue(id, out var r)
                    ? new EtatDisque(nom, r.Unavailable is null,
                        r.Problems.Where(p => p.Severity == Severity.Critical).Select(p => p.Message).ToList(),
                        r.Problems.Where(p => p.Severity == Severity.Warning).Select(p => p.Message).ToList(),
                        r.Unavailable)
                    : new EtatDisque(nom, false, Array.Empty<string>(), Array.Empty<string>(), "Windows ne donne pas les compteurs SMART de ce disque");
            }
            liste.Add(new DisqueAudit(nom,
                d.ULong("MediaType") switch { 3 => TypeDisque.Hdd, 4 => TypeDisque.Ssd, _ => TypeDisque.Inconnu },
                bus switch { 17 => "NVMe", 11 => "SATA", 8 => "RAID", 10 => "SAS", 7 => "USB", _ => null },
                d.ULong("Size"), bus == 7, etat));
        }
        return liste;
    }

    private static (ulong?, ulong?) VolumeSysteme()
    {
        var lecteur = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").Replace("'", "");
        var v = Wmi.Query($"SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DeviceID = '{lecteur}'").FirstOrDefault();
        return (v?.ULong("Size"), v?.ULong("FreeSpace"));
    }

    // ---------- Sécurité ----------

    private sealed record Protection
    {
        public IReadOnlyList<AntivirusAudit>? Antivirus { get; init; }
        public IReadOnlyList<string>? PareFeuTiers { get; init; }
        public IReadOnlyList<string>? ProfilsCoupes { get; init; }
        public bool PareFeuParStrategie { get; init; }
        public DateTime? DerniereMiseAJour { get; init; }
        public bool RedemarrageEnAttente { get; init; }
        public bool? BitLocker { get; init; }
        public string? BitLockerDetail { get; init; }
        public bool? Uac { get; init; }
        public bool? SmartScreen { get; init; }
        public bool? Rdp { get; init; }
    }

    /// <summary>productState du Centre de sécurité : octet du milieu 0x10/0x11 = actif, octet bas 0x00 = à jour.</summary>
    public static (bool Actif, bool AJour) EtatProduit(ulong etat) => ((((etat >> 8) & 0xFF) is 0x10 or 0x11), (etat & 0xFF) == 0x00);

    private static Protection Securite(bool admin)
    {
        IReadOnlyList<AntivirusAudit>? antivirus = null;
        IReadOnlyList<string>? pareFeuTiers = null;
        try
        {
            antivirus = Wmi.Query("SELECT displayName, productState FROM AntiVirusProduct", @"root\SecurityCenter2")
                .Select(p => { var (actif, aJour) = EtatProduit(p.ULong("productState") ?? 0); return new AntivirusAudit(p.Str("displayName") ?? "Antivirus", actif, aJour); })
                .ToList();
            pareFeuTiers = Wmi.Query("SELECT displayName, productState FROM FirewallProduct", @"root\SecurityCenter2")
                .Where(p => EtatProduit(p.ULong("productState") ?? 0).Actif).Select(p => p.Str("displayName") ?? "Pare-feu").ToList();
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException) { }

        var (bitlocker, detail) = BitLocker(admin);
        return new Protection
        {
            Antivirus = antivirus,
            PareFeuTiers = pareFeuTiers,
            ProfilsCoupes = ProfilsPareFeuCoupes(),
            PareFeuParStrategie = new[] { "DomainProfile", "StandardProfile", "PublicProfile" }
                .Any(p => LireHklm(@"SOFTWARE\Policies\Microsoft\WindowsFirewall\" + p, "EnableFirewall") is int),
            DerniereMiseAJour = DerniereMiseAJour(),
            RedemarrageEnAttente = CleExiste(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending")
                                   || CleExiste(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"),
            BitLocker = bitlocker,
            BitLockerDetail = detail,
            Uac = LireHklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA") switch { int v => v != 0, _ => null },
            SmartScreen = SmartScreen(),
            Rdp = LireHklm(@"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections") switch { int v => v == 0, _ => null },
        };
    }

    private static IReadOnlyList<string>? ProfilsPareFeuCoupes()
    {
        var coupes = new List<string>();
        foreach (var (cle, nom) in new[] { ("DomainProfile", "domaine"), ("StandardProfile", "privé"), ("PublicProfile", "public") })
        {
            var v = LireHklm($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{cle}", "EnableFirewall");
            if (v is null) return null;
            if (v is int i && i == 0) coupes.Add(nom);
        }
        return coupes;
    }

    private static DateTime? DerniereMiseAJour()
    {
        DateTime? derniere = null;
        foreach (var correctif in Wmi.Query("SELECT InstalledOn FROM Win32_QuickFixEngineering"))
        {
            // Date au format américain (M/d/yyyy), indépendant de la langue de Windows.
            if (DateTime.TryParse(correctif.Str("InstalledOn"), CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var date)
                && date <= DateTime.Now.AddDays(1) && (derniere is null || date > derniere))
                derniere = date;
        }
        return derniere;
    }

    private static bool? SmartScreen()
    {
        if (LireHklm(@"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen") is int politique) return politique != 0;
        return LireHklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled") switch
        {
            string s when s.Equals("Off", StringComparison.OrdinalIgnoreCase) => false,
            string s when s.Length > 0 => true,
            _ => null,
        };
    }

    /// <summary>
    /// BitLocker du disque système. Avec les droits : Win32_EncryptableVolume (ProtectionStatus). Sans droits : propriété
    /// « System.Volume.BitLockerProtection » de l'Explorateur. La clé de récupération n'est jamais lue.
    /// </summary>
    private static (bool?, string?) BitLocker(bool admin)
    {
        var lecteur = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        if (admin)
        {
            try
            {
                var v = Wmi.Query($"SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = '{lecteur.Replace("'", "")}'",
                    @"root\CIMV2\Security\MicrosoftVolumeEncryption").FirstOrDefault();
                switch (v?.ULong("ProtectionStatus"))
                {
                    case 1: return (true, null);
                    case 0: return (false, null);
                }
            }
            catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException) { }
        }
        var valeur = ProprieteExplorateur(lecteur, "System.Volume.BitLockerProtection");
        return valeur switch
        {
            1 => (true, null),
            3 => (true, "chiffrement en cours"),
            6 => (true, null),
            2 => (false, null),
            4 => (false, "déchiffrement en cours"),
            5 => (null, "protection suspendue"),
            null => (null, admin ? null : "lecture limitée sans droits administrateur"),
            _ => (null, null),
        };
    }

    /// <summary>Propriété d'un lecteur vue par l'Explorateur (lecture seule), sur un thread STA, avec délai.</summary>
    private static int? ProprieteExplorateur(string lecteur, string propriete)
    {
        int? resultat = null;
        var fil = new Thread(() =>
        {
            object? shell = null, dossier = null, element = null;
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type is null) return;
                shell = Activator.CreateInstance(type);
                dossier = type.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { 17 }); // Ce PC
                if (dossier is null) return;
                element = dossier.GetType().InvokeMember("ParseName", BindingFlags.InvokeMethod, null, dossier, new object[] { lecteur });
                if (element is null) return;
                var v = element.GetType().InvokeMember("ExtendedProperty", BindingFlags.InvokeMethod, null, element, new object[] { propriete });
                if (v is not null) resultat = Convert.ToInt32(v, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is TargetInvocationException or COMException or InvalidCastException or FormatException or MissingMethodException) { }
            finally
            {
                foreach (var o in new[] { element, dossier, shell })
                    if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
            }
        }) { IsBackground = true };
        fil.SetApartmentState(ApartmentState.STA);
        fil.Start();
        return fil.Join(TimeSpan.FromSeconds(10)) ? resultat : null;
    }

    /// <summary>Membres du groupe Administrateurs local (SID S-1-5-32-544, nom indépendant de la langue).</summary>
    private static IReadOnlyList<string>? Administrateurs()
    {
        var groupe = Wmi.Query("SELECT Name, Domain FROM Win32_Group WHERE LocalAccount = TRUE AND SID = 'S-1-5-32-544'").FirstOrDefault();
        if (groupe?.Str("Name") is not { } nom || groupe.Str("Domain") is not { } domaine) return null;
        string E(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");
        var requete = $"SELECT PartComponent FROM Win32_GroupUser WHERE GroupComponent = \"Win32_Group.Domain='{E(domaine)}',Name='{E(nom)}'\"";
        return Wmi.Query(requete)
            .Select(m => m.Str("PartComponent"))
            .Select(p => p is null ? null : Regex.Match(p, "Domain=\"([^\"]*)\",Name=\"([^\"]*)\""))
            .Where(m => m is { Success: true })
            .Select(m => $"{m!.Groups[1].Value}\\{m.Groups[2].Value}")
            .ToList();
    }

    // ---------- Logiciels, prise en main à distance, sauvegarde ----------

    private static List<string> ProgrammesInstalles()
    {
        var noms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string chemin = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var (ruche, vue) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            try
            {
                using var racine = RegistryKey.OpenBaseKey(ruche, vue);
                using var cle = racine.OpenSubKey(chemin);
                if (cle is null) continue;
                foreach (var sous in cle.GetSubKeyNames())
                {
                    using var s = cle.OpenSubKey(sous);
                    if (s?.GetValue("DisplayName") is string n && n.Trim().Length > 0) noms.Add(n.Trim());
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return noms.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly string[] OutilsPriseEnMain =
    {
        "TeamViewer", "AnyDesk", "RustDesk", "Splashtop", "LogMeIn", "ScreenConnect", "Chrome Remote Desktop", "UltraVNC", "TightVNC",
        "RealVNC", "VNC Server", "Supremo", "Ammyy", "Zoho Assist", "GoTo Resolve", "DWService", "Remote Utilities",
    };

    public static IReadOnlyList<string> PriseEnMain(IEnumerable<string> programmes) => Trouver(programmes, OutilsPriseEnMain);

    private static readonly string[] OutilsSauvegarde =
    {
        "Acronis", "Macrium", "Veeam Agent", "EaseUS Todo Backup", "AOMEI Backupper", "Cobian", "Duplicati", "Active Backup for Business",
        "Synology Drive", "Backblaze", "Carbonite", "iDrive", "Paragon Backup", "Iperius", "Google Drive", "Dropbox", "pCloud",
    };

    private static IReadOnlyList<string> Trouver(IEnumerable<string> programmes, IEnumerable<string> outils)
    {
        var liste = programmes.ToList();
        return outils.Where(o => liste.Any(p => p.IndexOf(o, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
    }

    private static IReadOnlyList<string> Sauvegardes(IReadOnlyList<string> programmes)
    {
        var trouvees = new List<string>();
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (documents.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0) trouvees.Add("OneDrive (Documents)");
        else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OneDrive"))) trouvees.Add("OneDrive (sans le dossier Documents)");
        var historique = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\FileHistory\Configuration");
        if (Directory.Exists(historique) && Directory.GetFiles(historique, "Config*.xml").Length > 0) trouvees.Add("Historique des fichiers");
        trouvees.AddRange(Trouver(programmes, OutilsSauvegarde));
        return trouvees;
    }

    private static DateTime? DernierPointRestauration()
    {
        DateTime? dernier = null;
        foreach (var p in Wmi.Query("SELECT CreationTime FROM SystemRestore", @"root\default"))
        {
            try
            {
                if (p.Str("CreationTime") is { Length: > 0 } brut && ManagementDateTimeConverter.ToDateTime(brut) is var d && (dernier is null || d > dernier))
                    dernier = d;
            }
            catch (ArgumentOutOfRangeException) { }
        }
        return dernier;
    }

    // ---------- Environnement professionnel ----------

    private sealed record Pro(bool? EntraId = null, bool? ComptePro = null, bool? Mdm = null, string? Fournisseur = null);

    private static Pro EnvironnementPro()
    {
        bool? entra = SousCles(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo") is { } j ? j.Count > 0 : false;
        bool? comptePro = SousCles(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WorkplaceJoin\JoinInfo") is { } w ? w.Count > 0 : false;
        bool? mdm = false;
        string? fournisseur = null;
        try
        {
            using var inscriptions = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
            if (inscriptions is not null)
            {
                foreach (var nom in inscriptions.GetSubKeyNames())
                {
                    using var e = inscriptions.OpenSubKey(nom);
                    if (e?.GetValue("ProviderID") is string id && id.Length > 0 && e.GetValue("EnrollmentState") is int etat && etat == 1)
                    {
                        mdm = true;
                        fournisseur = id == "MS DM Server" ? "Microsoft Intune" : id;
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { mdm = null; }
        return new Pro(entra, comptePro, mdm, fournisseur);
    }

    // ---------- Réseau ----------

    private static IReadOnlyList<CarteReseau> Reseau()
    {
        var configs = Wmi.QueryOptional("SELECT InterfaceIndex, IPAddress, DefaultIPGateway, DHCPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE")
            .Where(c => c.ULong("InterfaceIndex") is not null)
            .GroupBy(c => c.ULong("InterfaceIndex")!.Value).ToDictionary(g => g.Key, g => g.First());
        var cartes = new List<CarteReseau>();
        foreach (var a in Wmi.Query("SELECT InterfaceDescription, InterfaceIndex, NdisPhysicalMedium, MediaConnectState, ReceiveLinkSpeed, HardwareInterface FROM MSFT_NetAdapter", @"root\StandardCimv2"))
        {
            if (a.Bool("HardwareInterface") != true) continue;
            var type = a.ULong("NdisPhysicalMedium") switch { 9 => "Wi-Fi", 14 => "Ethernet", 8 => "Mobile (4G/5G)", 10 => null, _ => "Autre" };
            if (type is null) continue; // Bluetooth
            configs.TryGetValue(a.ULong("InterfaceIndex") ?? 0, out var c);
            var ips = (c?["IPAddress"] as string[] ?? Array.Empty<string>()).Where(ip => ip.Contains(".")).ToList();
            var passerelle = (c?["DefaultIPGateway"] as string[] ?? Array.Empty<string>()).FirstOrDefault(g => g.Contains("."));
            cartes.Add(new CarteReseau(Champ.Nettoyer(a.Str("InterfaceDescription")) ?? type, type, a.ULong("MediaConnectState") == 1,
                a.ULong("ReceiveLinkSpeed"), c?.Bool("DHCPEnabled"), ips, passerelle));
        }
        return cartes;
    }

    // ---------- Registre en lecture ----------

    private static object? LireHklm(string chemin, string valeur)
    {
        try
        {
            using var cle = Registry.LocalMachine.OpenSubKey(chemin);
            return cle?.GetValue(valeur);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    private static bool CleExiste(string chemin)
    {
        try
        {
            using var cle = Registry.LocalMachine.OpenSubKey(chemin);
            return cle is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    /// <summary>Sous-clés d'une clé : liste vide si la clé n'existe pas, null si elle n'a pas pu être lue.</summary>
    private static IReadOnlyList<string>? SousCles(RegistryKey racine, string chemin)
    {
        try
        {
            using var cle = racine.OpenSubKey(chemin);
            return cle?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }
}
