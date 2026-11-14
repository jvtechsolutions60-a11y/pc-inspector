using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jvtech.Lecture.Regles;

/// <summary>Seuils commerciaux de la batterie (ajustement 5 : réglables dans atelier.ini).</summary>
public sealed record SeuilsBatterie(int SansMention = 80, int Reserve = 60)
{
    public static SeuilsBatterie ParDefaut { get; } = new();

    /// <summary>Seuils incohérents (hors 1-100 ou réserve au-dessus de « sans mention ») : valeurs par défaut.</summary>
    public SeuilsBatterie Valides() =>
        SansMention is > 0 and <= 100 && Reserve is > 0 and <= 100 && Reserve <= SansMention ? this : ParDefaut;
}

/// <summary>Ce que Windows dit de la batterie. Chaque valeur peut manquer (null) : rien n'est estimé.</summary>
public sealed record MesureBatterie(bool Portable, int NombreBatteries, ulong? NominaleMWh, ulong? ActuelleMWh, ulong? Cycles)
{
    /// <summary>Capacité actuelle en % de la capacité nominale (plafonnée à 100), ou null.</summary>
    public int? SantePourcent => NominaleMWh is > 0 && ActuelleMWh is > 0
        ? (int)Math.Min(100, Math.Round(ActuelleMWh.Value * 100.0 / NominaleMWh.Value))
        : null;

    public int? UsurePourcent => SantePourcent is { } s ? 100 - s : null;

    /// <summary>Lignes détaillées pour la fiche : nominale, actuelle, usure, cycles, ou « non disponible ».</summary>
    public IReadOnlyList<string> Details()
    {
        string Mwh(ulong? v) => v is > 0 ? v.Value.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")) + " mWh" : "non disponible";
        return new[]
        {
            "Capacité nominale : " + Mwh(NominaleMWh),
            "Capacité actuelle : " + Mwh(ActuelleMWh),
            "Usure : " + (UsurePourcent is { } u ? u + " %" : "non disponible"),
            "Cycles : " + (Cycles is > 0 ? Cycles.Value.ToString(CultureInfo.InvariantCulture) : "non disponible"),
        };
    }
}

/// <summary>Résultat d'un diagnostic mémoire de Windows lu dans le journal Système.</summary>
public sealed record DiagnosticMemoire(DateTime Date, bool ErreursTrouvees);

/// <summary>État de santé d'un disque interne tel que lu (SMART / journal NVMe).</summary>
public sealed record EtatDisque(string Nom, bool Lu, IReadOnlyList<string> Critiques, IReadOnlyList<string> Avertissements, string? Raison = null);

/// <summary>Règles des contrôles d'Atelier. Pures : aucune lecture du PC, testées automatiquement.</summary>
public static class RegleControles
{
    public const string IdBatterie = "batterie";
    public const string IdDisque = "disque";
    public const string IdMemoire = "memoire";
    public const string IdMemoireApprofondi = "memoire-approfondi";

    public static Controle Batterie(MesureBatterie m, SeuilsBatterie? seuils = null)
    {
        var s = (seuils ?? SeuilsBatterie.ParDefaut).Valides();
        const string titre = "Batterie";
        if (m.NombreBatteries == 0)
            return m.Portable
                ? new Controle(IdBatterie, titre, Statut.AVerifier, "Aucune batterie détectée sur un portable : batterie absente ou débranchée ?") { Source = "Win32_Battery" }
                : new Controle(IdBatterie, titre, Statut.NonApplicable, "PC fixe, sans batterie") { Source = "Win32_Battery" };

        var details = string.Join(" · ", m.Details());
        if (m.SantePourcent is not { } sante)
            return new Controle(IdBatterie, titre, Statut.AVerifier, "Capacité non fournie par Windows : état à vérifier autrement. " + details) { Source = "root\\WMI Battery*" };

        if (sante >= s.SansMention)
            return new Controle(IdBatterie, titre, Statut.Conforme, $"{sante} % de la capacité d'origine. {details}") { Source = "root\\WMI Battery*" };
        if (sante >= s.Reserve)
            return new Controle(IdBatterie, titre, Statut.Anomalie, $"{sante} % de la capacité d'origine : autonomie réduite. {details}")
                { Portee = Portee.ReserveCommerciale, Source = "root\\WMI Battery*" };
        return new Controle(IdBatterie, titre, Statut.Anomalie, $"{sante} % de la capacité d'origine : autonomie réduite, remplacement conseillé. {details}")
            { Portee = Portee.ReserveCommerciale, Source = "root\\WMI Battery*" };
    }

    /// <summary>
    /// Ajustement 2 : contrôle rapide, sans redémarrage. Il ne prouve jamais l'absence de défaut : « aucune erreur
    /// signalée », pas « mémoire saine ».
    /// </summary>
    public static Controle MemoireRapide(int barrettes, DiagnosticMemoire? dernierDiagnostic, int erreursMaterielles)
    {
        const string titre = "Mémoire (contrôle rapide)";
        const string source = "Win32_PhysicalMemory, journal Système (WHEA, diagnostic mémoire)";
        if (barrettes == 0)
            return new Controle(IdMemoire, titre, Statut.AVerifier, "Barrettes non lues : contrôle rapide impossible.") { Source = source };
        if (dernierDiagnostic is { ErreursTrouvees: true } d)
            return new Controle(IdMemoire, titre, Statut.Anomalie, $"Le diagnostic mémoire de Windows du {d.Date:dd/MM/yyyy} a trouvé des erreurs.")
                { Portee = Portee.Bloquante, Source = source };
        if (erreursMaterielles > 0)
            return new Controle(IdMemoire, titre, Statut.Anomalie,
                    $"Windows a enregistré {erreursMaterielles} erreur(s) mémoire matérielle(s) ces 90 derniers jours : barrette suspecte.")
                { Portee = Portee.Bloquante, Source = source };
        return new Controle(IdMemoire, titre, Statut.Conforme,
                "Aucune erreur signalée par Windows. Contrôle rapide : il ne garantit pas l'absence de défaut ; le diagnostic approfondi le complète.")
            { Source = source };
    }

    /// <summary>Diagnostic approfondi (Windows, avec redémarrage) : le dernier résultat du journal, sinon « non réalisé ».</summary>
    public static Controle MemoireApprofondi(DiagnosticMemoire? dernier)
    {
        const string titre = "Mémoire (diagnostic approfondi)";
        const string source = "Journal Système, MemoryDiagnostics-Results";
        if (dernier is null)
            return new Controle(IdMemoireApprofondi, titre, Statut.AVerifier, "Non réalisé.") { Source = source };
        return dernier.ErreursTrouvees
            ? new Controle(IdMemoireApprofondi, titre, Statut.Anomalie, $"Erreurs trouvées le {dernier.Date:dd/MM/yyyy}.") { Portee = Portee.Bloquante, Source = source }
            : new Controle(IdMemoireApprofondi, titre, Statut.Conforme, $"Aucune erreur détectée le {dernier.Date:dd/MM/yyyy} (diagnostic mémoire de Windows).") { Source = source };
    }

    /// <summary>
    /// Disques internes. Défaut critique = bloquant. Avertissement ou lecture impossible = À vérifier (donc
    /// « Incomplet », ajustement 3) : « contrôle impossible » n'est jamais présenté comme « sain ».
    /// </summary>
    public static Controle Disques(IReadOnlyList<EtatDisque> disques)
    {
        const string titre = "Santé des disques";
        const string source = "SMART / journal NVMe";
        if (disques.Count == 0)
            return new Controle(IdDisque, titre, Statut.AVerifier, "Aucun disque interne lu.") { Source = source };

        var critiques = disques.SelectMany(d => d.Critiques.Select(c => $"{d.Nom} : {c}")).ToList();
        if (critiques.Count > 0)
            return new Controle(IdDisque, titre, Statut.Anomalie, string.Join(" ; ", critiques)) { Portee = Portee.Bloquante, Source = source };

        var nonLus = disques.Where(d => !d.Lu).Select(d => $"{d.Nom} : contrôle impossible ({d.Raison ?? "raison inconnue"})").ToList();
        var avertis = disques.SelectMany(d => d.Avertissements.Select(a => $"{d.Nom} : {a}")).ToList();
        if (nonLus.Count > 0 || avertis.Count > 0)
            return new Controle(IdDisque, titre, Statut.AVerifier, string.Join(" ; ", avertis.Concat(nonLus))) { Source = source };

        return new Controle(IdDisque, titre, Statut.Conforme,
            "Aucun indicateur de panne lu (" + string.Join(", ", disques.Select(d => d.Nom)) + "). Cela ne garantit pas l'absence de panne future.")
            { Source = source };
    }
}
