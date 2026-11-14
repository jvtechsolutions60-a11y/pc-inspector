using System;
using System.Collections.Generic;

namespace Jvtech.Lecture.Regles;

// Données brutes lues par JVTECH Audit. Null = non lu (droits, WMI muet, matériel absent) : jamais deviné.
// Les règles (RegleAudit) ne lisent rien sur le PC : elles sont testées avec des données simulées.

public enum TypeDisque { Inconnu, Hdd, Ssd }

public sealed record DisqueAudit(string Nom, TypeDisque Type, string? Bus, ulong? TailleOctets, bool Usb, EtatDisque? Sante);

public sealed record AntivirusAudit(string Nom, bool Actif, bool AJour);

public sealed record CarteReseau(string Nom, string Type, bool Connectee, ulong? VitesseBits, bool? Dhcp, IReadOnlyList<string> Ipv4, string? Passerelle);

public sealed record DonneesAudit
{
    // Identification
    public string? Fabricant { get; init; }
    public string? Modele { get; init; }
    public string? NumeroSerie { get; init; }
    public string? NomPc { get; init; }
    public bool Portable { get; init; }
    public string? Bios { get; init; }
    public IReadOnlyList<string>? PeripheriquesEnErreur { get; init; }

    // Windows
    public string? WindowsNom { get; init; }
    public string? EditionId { get; init; }
    public string? VersionAffichee { get; init; }
    public int? Build { get; init; }
    public bool? Active { get; init; }
    public MesuresWindows11? Windows11 { get; init; }

    // Processeur et mémoire
    public string? Processeur { get; init; }
    public int? Coeurs { get; init; }
    public int? Threads { get; init; }
    public ulong? MemoireOctets { get; init; }
    public string? TypeMemoire { get; init; }
    /// <summary>Barrettes lues (Win32_PhysicalMemory) : null si la liste n'a pas pu être lue.</summary>
    public int? Barrettes { get; init; }
    /// <summary>Emplacements annoncés par le BIOS (SMBIOS), souvent faux : jamais présenté comme sûr.</summary>
    public int? EmplacementsAnnonces { get; init; }
    public ulong? MemoireMaxAnnonceeOctets { get; init; }

    // Stockage et batterie
    public IReadOnlyList<DisqueAudit>? Disques { get; init; }
    public ulong? VolumeSystemeOctets { get; init; }
    public ulong? VolumeSystemeLibreOctets { get; init; }
    public MesureBatterie? Batterie { get; init; }

    // Sécurité
    public IReadOnlyList<AntivirusAudit>? Antivirus { get; init; }
    public IReadOnlyList<string>? PareFeuTiers { get; init; }
    /// <summary>Profils du pare-feu Windows désactivés (domaine, privé, public) ; null si non lus.</summary>
    public IReadOnlyList<string>? ProfilsPareFeuCoupes { get; init; }
    /// <summary>Le pare-feu est réglé par une stratégie (GPO) : décision de l'administrateur, pas une anomalie en soi.</summary>
    public bool PareFeuParStrategie { get; init; }
    public DateTime? DerniereMiseAJour { get; init; }
    public bool RedemarrageEnAttente { get; init; }
    /// <summary>État BitLocker du disque système : true protégé, false non chiffré, null non lu.</summary>
    public bool? BitLocker { get; init; }
    public string? BitLockerDetail { get; init; }
    public bool? UacActif { get; init; }
    public bool? SmartScreenActif { get; init; }
    public bool? BureauADistanceOuvert { get; init; }
    /// <summary>Membres du groupe Administrateurs local ; null si non lus (droits).</summary>
    public IReadOnlyList<string>? Administrateurs { get; init; }
    public IReadOnlyList<string> PriseEnMainADistance { get; init; } = Array.Empty<string>();

    // Environnement professionnel
    public bool? Domaine { get; init; }
    public string? NomDomaine { get; init; }
    public bool? EntraId { get; init; }
    public bool? CompteProfessionnel { get; init; }
    public bool? Mdm { get; init; }
    public string? FournisseurMdm { get; init; }

    // Sauvegarde
    public IReadOnlyList<string>? Sauvegardes { get; init; }
    public DateTime? DernierPointRestauration { get; init; }

    // Réseau
    public IReadOnlyList<CarteReseau>? Reseau { get; init; }

    // Contexte de l'audit
    public bool Administrateur { get; init; }
    public string? LangueWindows { get; init; }
    public DateTime Date { get; init; }
    public TimeSpan? Duree { get; init; }
    /// <summary>Ce qui n'a pas pu être lu, avec la raison : repris dans « Limites de cet audit ».</summary>
    public IReadOnlyList<string> NonLus { get; init; } = Array.Empty<string>();

    /// <summary>Poste géré par une entreprise : certains réglages relèvent de l'administrateur (T05).</summary>
    public bool Administre => Domaine == true || EntraId == true || Mdm == true;
}
