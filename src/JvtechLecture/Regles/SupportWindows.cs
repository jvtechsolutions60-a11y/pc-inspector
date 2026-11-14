using System;
using System.Linq;

namespace Jvtech.Lecture.Regles;

/// <summary>Résultat de la recherche de fin de support : date connue ou raison du doute.</summary>
public sealed record SupportWindowsInfo(string Version, DateTime? Fin, string Programme, string? Remarque = null);

/// <summary>
/// Fin du support des versions de Windows selon l'édition (cahier § 4.2, AU-03) : Famille/Pro et Entreprise/Éducation
/// n'ont pas la même échéance ; LTSC a la sienne ; les mises à jour étendues (ESU) de Windows 10 ne sont pas
/// détectables de façon fiable et restent « à vérifier ». Tables relevées sur les pages « cycle de vie » de Microsoft.
/// </summary>
public static class SupportWindows
{
    public static readonly DateTime Releve = new(2026, 11, 13);
    public static string Libelle => $"dates de fin de support relevées le {Releve:dd/MM/yyyy}";

    // (build minimum, version, fin Famille/Pro, fin Entreprise/Éducation)
    private static readonly (int Build, string Version, DateTime FinGrandPublic, DateTime FinEntreprise)[] Windows11 =
    {
        (26200, "Windows 11 25H2", new DateTime(2027, 10, 12), new DateTime(2028, 10, 10)),
        (26100, "Windows 11 24H2", new DateTime(2026, 10, 13), new DateTime(2027, 10, 12)),
        (22631, "Windows 11 23H2", new DateTime(2025, 11, 11), new DateTime(2026, 11, 10)),
        (22621, "Windows 11 22H2", new DateTime(2024, 10, 8), new DateTime(2025, 10, 14)),
        (22000, "Windows 11 21H2", new DateTime(2023, 10, 10), new DateTime(2024, 10, 8)),
    };

    /// <summary>Plus haut build connu de la table : au-delà, la date n'est pas connue (jamais devinée).</summary>
    public const int BuildMaxConnu = 26299;

    // LTSC : (build, version, fin standard, fin IoT)
    private static readonly (int Build, string Version, DateTime Fin, DateTime FinIoT)[] Ltsc =
    {
        (26100, "Windows 11 LTSC 2024", new DateTime(2029, 10, 9), new DateTime(2034, 10, 10)),
        (19044, "Windows 10 LTSC 2021", new DateTime(2027, 1, 12), new DateTime(2032, 1, 13)),
        (17763, "Windows 10 LTSC 2019", new DateTime(2029, 1, 9), new DateTime(2029, 1, 9)),
        (14393, "Windows 10 LTSB 2016", new DateTime(2026, 10, 13), new DateTime(2026, 10, 13)),
    };

    public static readonly DateTime FinWindows10 = new(2025, 10, 14);

    /// <summary>Éditions avec le support long de Windows 11 (36 mois).</summary>
    public static bool EstEntreprise(string? editionId) => editionId is not null &&
        (editionId.StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase)
         || editionId.StartsWith("Education", StringComparison.OrdinalIgnoreCase)
         || editionId.StartsWith("IoTEnterprise", StringComparison.OrdinalIgnoreCase)
         || editionId.StartsWith("ServerRdsh", StringComparison.OrdinalIgnoreCase));

    /// <summary>LTSC / LTSB : EditionID se termine par « S » (EnterpriseS, IoTEnterpriseS…).</summary>
    public static bool EstLtsc(string? editionId) => editionId is not null &&
        (editionId.Equals("EnterpriseS", StringComparison.OrdinalIgnoreCase) || editionId.Equals("EnterpriseSN", StringComparison.OrdinalIgnoreCase)
         || editionId.Equals("IoTEnterpriseS", StringComparison.OrdinalIgnoreCase));

    /// <summary>Windows Server (ServerStandard, ServerDatacenter…), hors ServerRdsh (Windows Entreprise multisession).</summary>
    public static bool EstServeur(string? editionId) => editionId is not null
        && editionId.StartsWith("Server", StringComparison.OrdinalIgnoreCase)
        && !editionId.StartsWith("ServerRdsh", StringComparison.OrdinalIgnoreCase);

    public static SupportWindowsInfo? Trouver(int? build, string? editionId)
    {
        if (build is not { } b || b <= 0) return null;
        // Windows Server : cycle de vie différent, absent de ces tables (relevé pour les postes) : jamais de date devinée.
        if (EstServeur(editionId))
            return new SupportWindowsInfo($"Windows Server (build {b})", null, "Windows Server",
                "cycle de support de Windows Server non couvert par cet audit (conçu pour les postes) : date de fin à vérifier");
        if (EstLtsc(editionId))
        {
            var iot = editionId!.StartsWith("IoT", StringComparison.OrdinalIgnoreCase);
            var l = Ltsc.FirstOrDefault(x => x.Build == b);
            return l.Version is null
                ? new SupportWindowsInfo($"LTSC (build {b})", null, "LTSC", "version LTSC absente de la table : date à vérifier")
                : new SupportWindowsInfo(l.Version + (iot ? " IoT" : ""), iot ? l.FinIoT : l.Fin, "LTSC");
        }
        if (b < 22000)
            return new SupportWindowsInfo("Windows 10", FinWindows10, "standard",
                "mises à jour étendues (ESU) éventuelles non détectées : à vérifier (programme payant pour les entreprises jusqu'en 2028)");
        if (b > BuildMaxConnu)
            return new SupportWindowsInfo($"Windows 11 (build {b})", null, "standard", "version plus récente que la table : date de fin non vérifiée");
        var v = Windows11.First(x => b >= x.Build);
        var entreprise = EstEntreprise(editionId);
        return new SupportWindowsInfo(v.Version, entreprise ? v.FinEntreprise : v.FinGrandPublic, entreprise ? "Entreprise/Éducation" : "Famille/Pro");
    }

    /// <summary>Nom français court de l'édition (EditionID ne dépend pas de la langue de Windows).</summary>
    public static string? Edition(string? editionId) => editionId switch
    {
        null or "" => null,
        "Core" or "CoreN" or "CoreSingleLanguage" or "CoreCountrySpecific" => "Famille",
        "Professional" or "ProfessionalN" => "Professionnel",
        "ProfessionalWorkstation" => "Professionnel pour les stations de travail",
        "ProfessionalEducation" => "Professionnel Éducation",
        "Enterprise" or "EnterpriseN" => "Entreprise",
        "EnterpriseS" or "EnterpriseSN" => "Entreprise LTSC",
        "IoTEnterprise" => "IoT Entreprise",
        "IoTEnterpriseS" => "IoT Entreprise LTSC",
        "Education" or "EducationN" => "Éducation",
        "ServerStandard" or "ServerStandardEval" => "Standard",
        "ServerDatacenter" or "ServerDatacenterEval" => "Datacenter",
        _ => editionId,
    };
}
