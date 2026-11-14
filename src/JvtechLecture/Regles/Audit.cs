using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jvtech.Lecture.Regles;

/// <summary>Urgence d'une alerte ou d'une recommandation (cahier § 4.4), de la plus grave à la moins grave.</summary>
public enum Urgence { Critique = 0, Important, AVerifier, Amelioration, Information }

/// <summary>Catégorie d'une recommandation (cahier § 4.4).</summary>
public enum Categorie { Conserver, Optimiser, Reparer, Remplacer }

public enum Confiance { Elevee, Moyenne, Faible }

/// <summary>Une ligne d'un bloc : un statut et un constat.</summary>
public sealed record PointAudit(Statut Statut, string Texte);

/// <summary>Un bloc du tableau de bord (cahier § 4.1).</summary>
public sealed record BlocAudit(string Id, string Titre, Statut Statut, string Valeur, string Detail, string Interpretation)
{
    public IReadOnlyList<PointAudit> Points { get; init; } = Array.Empty<PointAudit>();
    public string? Source { get; init; }
}

public sealed record AlerteAudit(Urgence Urgence, string Titre, string Constat, string Bloc);

/// <summary>Recommandation : constat vérifiable, solution, conditions et confiance. Jamais de prix (devis à renseigner).</summary>
public sealed record RecommandationAudit(Categorie Categorie, Urgence Urgence, string Titre, string Constat, string Solution, string Conditions, Confiance Confiance);

public sealed record ResultatAudit(
    IReadOnlyList<BlocAudit> Blocs,
    IReadOnlyList<AlerteAudit> Alertes,
    IReadOnlyList<RecommandationAudit> Recommandations,
    BilanWindows11? Windows11,
    IReadOnlyList<string> Limites)
{
    /// <summary>3 à 5 alertes prioritaires ; toutes les critiques restent affichées, même au-delà de 5.</summary>
    public IReadOnlyList<AlerteAudit> Prioritaires
    {
        get
        {
            var triees = Alertes.Where(a => a.Urgence != Urgence.Information).OrderBy(a => a.Urgence).ToList();
            var critiques = triees.Count(a => a.Urgence == Urgence.Critique);
            return triees.Take(Math.Max(5, critiques)).ToList();
        }
    }

    public string Synthese
    {
        get
        {
            int N(Urgence u) => Alertes.Count(a => a.Urgence == u);
            var parties = new List<string>();
            if (N(Urgence.Critique) > 0) parties.Add($"{N(Urgence.Critique)} critique(s)");
            if (N(Urgence.Important) > 0) parties.Add($"{N(Urgence.Important)} important(s)");
            if (N(Urgence.AVerifier) > 0) parties.Add($"{N(Urgence.AVerifier)} à vérifier");
            if (N(Urgence.Amelioration) > 0) parties.Add($"{N(Urgence.Amelioration)} amélioration(s)");
            return parties.Count == 0 ? "Synthèse : aucun point à signaler parmi les contrôles faits" : "Synthèse : " + string.Join(", ", parties);
        }
    }
}

/// <summary>
/// Règles de JVTECH Audit : transforment les données lues en blocs, alertes et recommandations.
/// Pas de note globale : un défaut grave n'est jamais compensé par le reste. Rien n'est lu sur le PC ici.
/// </summary>
public static class RegleAudit
{
    private const ulong Go = 1024UL * 1024 * 1024;

    public static string Libelle(Urgence u) => u switch
    {
        Urgence.Critique => "Critique",
        Urgence.Important => "Important",
        Urgence.AVerifier => "À vérifier",
        Urgence.Amelioration => "Amélioration",
        _ => "Information",
    };

    public static string Libelle(Categorie c) => c switch
    {
        Categorie.Conserver => "Conserver",
        Categorie.Optimiser => "Optimiser",
        Categorie.Reparer => "Réparer",
        _ => "Remplacer",
    };

    public static string Libelle(Confiance c) => c switch
    {
        Confiance.Elevee => "confiance élevée",
        Confiance.Moyenne => "confiance moyenne",
        _ => "confiance faible",
    };

    /// <summary>Le plus grave d'abord : Anomalie, À vérifier, Conforme, Non applicable.</summary>
    public static Statut Pire(IEnumerable<Statut> statuts)
    {
        var liste = statuts.ToList();
        if (liste.Contains(Statut.Anomalie)) return Statut.Anomalie;
        if (liste.Contains(Statut.AVerifier)) return Statut.AVerifier;
        if (liste.Contains(Statut.Conforme)) return Statut.Conforme;
        return liste.Count == 0 ? Statut.AVerifier : Statut.NonApplicable;
    }

    public static string Taille(ulong octets) => Collecte.Inventaire.FormatCapacite(octets);

    public static ResultatAudit Evaluer(DonneesAudit d, BaseWindows11 baseW11)
    {
        var alertes = new List<AlerteAudit>();
        var recos = new List<RecommandationAudit>();
        void A(Urgence u, string titre, string constat, string bloc) => alertes.Add(new AlerteAudit(u, titre, constat, bloc));
        void R(Categorie c, Urgence u, string titre, string constat, string solution, string conditions, Confiance conf) =>
            recos.Add(new RecommandationAudit(c, u, titre, constat, solution, conditions, conf));

        var aujourdhui = d.Date == default ? DateTime.Today : d.Date.Date;
        var bilanW11 = d.Windows11 is null ? null : CompatibiliteWindows11.Evaluer(d.Windows11, baseW11);
        var blocs = new List<BlocAudit>
        {
            Identification(d, A, R),
            Windows(d, aujourdhui, bilanW11, A, R),
            Windows11Bloc(d, bilanW11, A, R),
            Processeur(d, baseW11),
            Memoire(d, A, R),
            Stockage(d, A, R),
            Batterie(d, A, R),
            Securite(d, aujourdhui, A, R),
            EnvironnementPro(d),
            Sauvegarde(d, aujourdhui, A, R),
            Reseau(d, A),
        };
        return new ResultatAudit(blocs, alertes, recos.OrderBy(r => r.Urgence).ToList(), bilanW11, Limites(d));
    }

    private delegate void Alerte(Urgence u, string titre, string constat, string bloc);
    private delegate void Reco(Categorie c, Urgence u, string titre, string constat, string solution, string conditions, Confiance conf);

    private static BlocAudit Identification(DonneesAudit d, Alerte A, Reco R)
    {
        var nom = Annonce.NomPc(d.Fabricant, d.Modele) ?? "PC non identifié";
        var points = new List<PointAudit>();
        if (d.PeripheriquesEnErreur is null) points.Add(new(Statut.AVerifier, "Périphériques en erreur : liste non lue"));
        else if (d.PeripheriquesEnErreur.Count == 0) points.Add(new(Statut.Conforme, "Aucun périphérique en erreur"));
        else
        {
            var liste = string.Join(", ", d.PeripheriquesEnErreur.Take(4)) + (d.PeripheriquesEnErreur.Count > 4 ? ", …" : "");
            points.Add(new(Statut.Anomalie, $"{d.PeripheriquesEnErreur.Count} périphérique(s) en erreur : {liste}"));
            A(Urgence.Amelioration, "Périphériques en erreur", liste, "identification");
            R(Categorie.Reparer, Urgence.Amelioration, "Pilotes à installer ou périphérique à contrôler", $"Gestionnaire de périphériques : {liste}.",
                "Installer le pilote du fabricant du PC ou par Windows Update, puis vérifier le périphérique.", "Selon l'usage du périphérique par le client.", Confiance.Elevee);
        }
        var detail = string.Join(" · ", new[]
        {
            d.NumeroSerie is null ? "N° de série non lu" : "N° de série " + d.NumeroSerie,
            d.Bios is null ? null : "BIOS " + d.Bios,
        }.Where(x => x is not null));
        return new BlocAudit("identification", "Identification", Pire(points.Select(p => p.Statut)), $"{nom} · {(d.Portable ? "Portable" : "PC fixe")}", detail,
            "Identification du poste") { Points = points, Source = "Win32_ComputerSystem, Win32_BIOS, Win32_PnPEntity" };
    }

    private static BlocAudit Windows(DonneesAudit d, DateTime aujourdhui, BilanWindows11? w11, Alerte A, Reco R)
    {
        const string id = "windows";
        var edition = SupportWindows.Edition(d.EditionId);
        var famille = SupportWindows.EstServeur(d.EditionId) && d.Build is > 0 ? "Windows Server"
            : d.Build is >= 22000 ? "Windows 11" : d.Build is > 0 ? "Windows 10" : null;
        var valeur = famille is null ? (d.WindowsNom ?? "Version non lue") : $"{famille} {edition ?? ""}".Trim();
        var activation = d.Active switch { true => "activé", false => "non activé", _ => "activation non lue" };
        var detail = string.Join(" · ", new[] { d.VersionAffichee, d.Build is { } b ? $"build {b}" : null, activation }.Where(x => x is not null));
        var points = new List<PointAudit>();

        var support = SupportWindows.Trouver(d.Build, d.EditionId);
        if (support is null) points.Add(new(Statut.AVerifier, "Version non lue : fin du support inconnue"));
        else if (support.Fin is not { } fin) points.Add(new(Statut.AVerifier, $"{support.Version} : {support.Remarque}"));
        else if (aujourdhui > fin)
        {
            var remarque = support.Remarque is null ? "" : $" ; {support.Remarque}";
            points.Add(new(Statut.Anomalie, $"{support.Version} ({support.Programme}) : hors support depuis le {fin:dd/MM/yyyy}{remarque}"));
            if (d.Build is >= 22000)
            {
                A(Urgence.Important, $"{support.Version} hors support", $"depuis le {fin:dd/MM/yyyy} : installer la version suivante de Windows 11.", id);
                R(Categorie.Reparer, Urgence.Important, "Mise à jour de version de Windows 11", $"{support.Version} n'a plus de correctifs de sécurité depuis le {fin:dd/MM/yyyy}.",
                    "Installer la dernière version de Windows 11 par Windows Update (« mise à jour des fonctionnalités »).", "Sauvegarde vérifiée avant la mise à jour ; espace disque suffisant.", Confiance.Elevee);
            }
            else
            {
                A(Urgence.Important, $"{valeur} hors support", $"depuis le {fin:dd/MM/yyyy}, extension (ESU) non détectée.", id);
                RecommandationWindows10(w11, R);
            }
        }
        else if ((fin - aujourdhui).TotalDays <= 90)
        {
            points.Add(new(Statut.Anomalie, $"{support.Version} : fin du support le {fin:dd/MM/yyyy}"));
            A(Urgence.Amelioration, "Fin du support de Windows proche", $"{support.Version} : le {fin:dd/MM/yyyy}.", id);
        }
        else if (aujourdhui - SupportWindows.Releve > TimeSpan.FromDays(365))
            points.Add(new(Statut.AVerifier, $"{support.Version} : supporté jusqu'au {fin:dd/MM/yyyy} d'après des {SupportWindows.Libelle}, trop anciennes pour l'affirmer"));
        else points.Add(new(Statut.Conforme, $"{support.Version} ({support.Programme}) : supporté jusqu'au {fin:dd/MM/yyyy}"));

        switch (d.Active)
        {
            case true: points.Add(new(Statut.Conforme, "Windows activé")); break;
            case false:
                points.Add(new(Statut.Anomalie, "Windows non activé"));
                A(Urgence.Amelioration, "Windows non activé", "licence à vérifier.", id);
                R(Categorie.Reparer, Urgence.Amelioration, "Activation de Windows", "Windows n'est pas activé.",
                    "Vérifier la licence (clé du BIOS, licence numérique liée au compte, licence en volume).", "Licence valide du client ; aucune activation contournée.", Confiance.Elevee);
                break;
            default: points.Add(new(Statut.AVerifier, "Activation non lue")); break;
        }
        return new BlocAudit(id, "Windows", Pire(points.Select(p => p.Statut)), valeur, detail, "Risque de support et conformité")
            { Points = points, Source = "Registre CurrentVersion (EditionID, build), SoftwareLicensingProduct ; " + SupportWindows.Libelle };
    }

    private static void RecommandationWindows10(BilanWindows11? w11, Reco R)
    {
        const string conditions = "Sauvegarde vérifiée, logiciels métier compatibles, besoins du client.";
        switch (w11?.Verdict)
        {
            case VerdictWindows11.Compatible:
                R(Categorie.Optimiser, Urgence.Important, "Passage à Windows 11", "Windows 10 hors support ; matériel conforme à la liste Microsoft.",
                    "Mise à niveau gratuite vers Windows 11.", conditions, Confiance.Elevee);
                break;
            case VerdictWindows11.ReglageUefi:
                var reglages = string.Join(", ", w11.Points.Where(p => p.Nature == NaturePoint.ReglageUefi).Select(p => p.Nom));
                R(Categorie.Optimiser, Urgence.Important, "Passage à Windows 11 après réglage de l'UEFI", $"Windows 10 hors support ; matériel conforme, réglage à faire : {reglages}.",
                    $"Régler l'UEFI ({reglages}), puis mettre à niveau vers Windows 11.", conditions + " Réglage fait par le technicien, jamais par JVTECH Audit.", Confiance.Elevee);
                break;
            case VerdictWindows11.Incompatible:
                var causes = string.Join(", ", w11.Points.Where(p => p.Nature == NaturePoint.Incompatible).Select(p => p.Nom));
                var materiel = w11.Points.Any(p => p.Nature == NaturePoint.Incompatible && p.Nom is "Mémoire" or "Disque système") && !w11.Points.Any(p => p.Nature == NaturePoint.Incompatible && p.Nom is "Processeur" or "TPM 2.0" or "64 bits" or "Cœurs et fréquence");
                if (materiel)
                    R(Categorie.Optimiser, Urgence.Important, "Mise à niveau matérielle pour Windows 11", $"Windows 10 hors support ; exigence non remplie : {causes}.",
                        "Ajouter de la mémoire ou remplacer le disque, puis mettre à niveau vers Windows 11.", conditions + " Faisabilité à vérifier sur ce modèle.", Confiance.Moyenne);
                else
                    R(Categorie.Remplacer, Urgence.Important, "Renouvellement du PC à étudier", $"Windows 10 hors support et matériel non pris en charge par Windows 11 ({causes}).",
                        "Étudier avec le client un PC compatible Windows 11, ou une extension de support si elle est disponible pour son contrat.", conditions, Confiance.Elevee);
                break;
            default:
                R(Categorie.Optimiser, Urgence.AVerifier, "Compatibilité Windows 11 à confirmer", "Windows 10 hors support ; compatibilité Windows 11 impossible à conclure automatiquement.",
                    "Vérifier les points « impossible de conclure » (liste Microsoft, TPM) avant de proposer une solution.", conditions, Confiance.Faible);
                break;
        }
    }

    private static BlocAudit Windows11Bloc(DonneesAudit d, BilanWindows11? b, Alerte A, Reco R)
    {
        const string id = "windows11";
        const string interpretation = "Causes exactes : processeur de la liste officielle, TPM, UEFI/Secure Boot, mémoire, stockage";
        if (b is null)
            return new BlocAudit(id, "Compatibilité Windows 11", Statut.AVerifier, "Impossible de conclure", "Exigences non lues", interpretation);
        var points = b.Points.Select(p => new PointAudit(p.Nature switch
        {
            NaturePoint.Conforme => Statut.Conforme,
            NaturePoint.Incompatible => Statut.Anomalie,
            _ => Statut.AVerifier,
        }, $"{p.Nom} : {p.Detail}")).ToList();
        var serveur = SupportWindows.EstServeur(d.EditionId);
        var dejaW11 = d.Build is >= 22000 || serveur;
        var statut = b.Verdict switch
        {
            VerdictWindows11.Compatible => Statut.Conforme,
            VerdictWindows11.Incompatible => Statut.Anomalie,
            _ => Statut.AVerifier,
        };
        var valeur = b.Verdict == VerdictWindows11.ReglageUefi ? "Configuration à vérifier" : b.Libelle;
        if (dejaW11)
        {
            valeur = (serveur ? "Windows Server installé" : "Windows 11 installé") + " · matériel " + b.Verdict switch
            {
                VerdictWindows11.Compatible => "compatible",
                VerdictWindows11.ReglageUefi => "compatible, réglage UEFI à vérifier",
                VerdictWindows11.Incompatible => "non pris en charge",
                _ => "impossible à conclure",
            };
            if (b.Verdict == VerdictWindows11.Incompatible)
            {
                A(Urgence.Important, "Windows 11 sur un matériel non pris en charge",
                    "Microsoft ne garantit pas les mises à jour sur ce matériel.", id);
                R(Categorie.Remplacer, Urgence.Important, "Matériel non pris en charge par Windows 11",
                    "Windows 11 a été installé sur un matériel hors exigences (" + string.Join(", ", b.Points.Where(p => p.Nature == NaturePoint.Incompatible).Select(p => p.Nom)) + ").",
                    "Informer le client ; étudier un renouvellement si les mises à jour cessent.", "Selon l'usage et les données du client.", Confiance.Moyenne);
            }
        }
        else if (b.Verdict == VerdictWindows11.ReglageUefi)
            A(Urgence.AVerifier, "Windows 11 : réglage de l'UEFI à faire",
                string.Join(", ", b.Points.Where(p => p.Nature == NaturePoint.ReglageUefi).Select(p => p.Nom)) + " (jamais modifié par l'audit).", id);
        return new BlocAudit(id, "Compatibilité Windows 11", statut, valeur, b.Base, interpretation) { Points = points, Source = b.Base };
    }

    /// <summary>Génération d'un Intel Core (i3/i5/i7/i9-NNNN), seulement quand le nom la donne sans ambiguïté.</summary>
    public static string? Generation(string? processeur)
    {
        if (processeur is null) return null;
        var gen = Regex.Match(processeur, @"\b(\d{1,2})(?:st|nd|rd|th) Gen\b", RegexOptions.IgnoreCase);
        if (gen.Success) return $"{gen.Groups[1].Value}e génération";
        var m = Regex.Match(processeur, @"\bi[3579]-(\d{4,5})", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var chiffres = m.Groups[1].Value;
        // i5-8350U : 8e ; i7-10510U : 10e ; i5-1035G1, i5-1135G7, i7-1260P : 10e, 11e, 12e (4 chiffres commençant par 1).
        var n = chiffres.Length == 5 || chiffres[0] == '1' ? int.Parse(chiffres.Substring(0, 2), CultureInfo.InvariantCulture) : chiffres[0] - '0';
        return n is >= 2 and <= 14 ? $"{n}e génération" : null;
    }

    private static BlocAudit Processeur(DonneesAudit d, BaseWindows11 baseW11)
    {
        if (d.Processeur is null)
            return new BlocAudit("processeur", "Processeur", Statut.AVerifier, "Non lu", "", "Aptitude à l'usage, compatibilité");
        var detail = string.Join(" · ", new[]
        {
            Generation(d.Processeur),
            d.Coeurs is { } c ? $"{c} cœurs" + (d.Threads is { } t && t != c ? $", {t} threads" : "") : null,
        }.Where(x => x is not null));
        var regle = baseW11.Trouver(d.Processeur);
        var point = regle is not null ? new PointAudit(Statut.Conforme, $"Dans la liste Microsoft (« {regle.Libelle} »)")
            : CompatibiliteWindows11.ProcesseurAncien(d.Processeur) ? new PointAudit(Statut.Anomalie, "Génération antérieure aux processeurs pris en charge par Windows 11")
            : new PointAudit(Statut.AVerifier, "Absent de la base Windows 11 : à vérifier");
        return new BlocAudit("processeur", "Processeur", point.Statut, Champ.Nettoyer(d.Processeur) ?? d.Processeur, detail, "Aptitude à l'usage, compatibilité")
            { Points = new[] { point }, Source = "Win32_Processor ; " + baseW11.Libelle };
    }

    private static BlocAudit Memoire(DonneesAudit d, Alerte A, Reco R)
    {
        const string id = "memoire";
        const string interpretation = "Possibilité d'extension, sans recommandation systématique";
        if (d.MemoireOctets is not { } total)
            return new BlocAudit(id, "Mémoire", Statut.AVerifier, "Non lue", "", interpretation);
        var go = Math.Round(total / (double)Go);
        var valeur = $"{go:0} Go{(d.TypeMemoire is null ? "" : " " + d.TypeMemoire)}";
        var details = new List<string>();
        if (d.Barrettes is { } n and > 0) details.Add($"{n} barrette(s)");
        else details.Add("barrettes non lues");
        // T09 : aucun emplacement libre inventé. Le nombre d'emplacements du BIOS est souvent faux : signalé comme tel.
        int? libres = d.EmplacementsAnnonces is { } e && d.Barrettes is { } b && e >= b && e <= 8 ? e - b : null;
        if (libres is { } l) details.Add($"{l} emplacement(s) libre(s) selon le BIOS, à confirmer à l'ouverture");
        else details.Add("emplacements libres non déterminés");
        if (d.MemoireMaxAnnonceeOctets is { } max && max >= total) details.Add($"maximum annoncé par le BIOS : {Math.Round(max / (double)Go):0} Go");

        Statut statut;
        string constat;
        if (go < 4)
        {
            statut = Statut.Anomalie;
            constat = $"{go:0} Go : insuffisant pour Windows 10/11";
            A(Urgence.Important, $"Mémoire {go:0} Go", "insuffisante pour un usage courant de Windows.", id);
        }
        else if (go < 8)
        {
            statut = Statut.Anomalie;
            constat = $"{go:0} Go : juste pour Windows 11 et la bureautique";
            A(Urgence.Amelioration, $"Mémoire {go:0} Go", "lenteurs probables avec plusieurs logiciels ouverts.", id);
        }
        else
        {
            statut = Statut.Conforme;
            constat = $"{go:0} Go : suffisant pour la bureautique";
        }
        if (go < 8)
            R(Categorie.Optimiser, go < 4 ? Urgence.Important : Urgence.Amelioration, "Extension de la mémoire à étudier", $"{go:0} Go installés.",
                "Passer à 8 Go selon les besoins du client.", libres is > 0 ? "Emplacement libre à confirmer à l'ouverture ; mémoire compatible." : "Mémoire peut-être soudée : emplacements à vérifier sur la fiche du fabricant.",
                libres is > 0 ? Confiance.Moyenne : Confiance.Faible);
        return new BlocAudit(id, "Mémoire", statut, valeur, string.Join(" · ", details), interpretation)
            { Points = new[] { new PointAudit(statut, constat) }, Source = "Win32_ComputerSystem, Win32_PhysicalMemory, Win32_PhysicalMemoryArray" };
    }

    private static BlocAudit Stockage(DonneesAudit d, Alerte A, Reco R)
    {
        const string id = "stockage";
        const string interpretation = "Priorité de remplacement si disque mécanique lent ou disque suspect";
        var internes = d.Disques?.Where(x => !x.Usb).ToList();
        if (internes is null || internes.Count == 0)
            return new BlocAudit(id, "Stockage", Statut.AVerifier, "Disques non lus", "", interpretation);
        var points = new List<PointAudit>();
        string Type(DisqueAudit x) => x.Type switch { TypeDisque.Hdd => "HDD", TypeDisque.Ssd => "SSD", _ => "Disque" };
        string Nom(DisqueAudit x) => $"{Type(x)} {(x.TailleOctets is { } t ? Taille(t) : "")}{(x.Bus is null ? "" : " " + x.Bus)}".Replace("  ", " ").Trim();
        var valeur = string.Join(" + ", internes.Select(Nom));

        foreach (var x in internes)
        {
            var s = x.Sante;
            if (s is null || !s.Lu)
                points.Add(new(Statut.AVerifier, $"{Nom(x)} : santé non vérifiée ({s?.Raison ?? "compteurs SMART non lus"})"));
            else if (s.Critiques.Count > 0)
            {
                points.Add(new(Statut.Anomalie, $"{Nom(x)} : {string.Join(" ; ", s.Critiques)}"));
                A(Urgence.Critique, "Disque en défaut", $"{Nom(x)} : {string.Join(" ; ", s.Critiques)}. Sauvegarder les données en priorité.", id);
                R(Categorie.Remplacer, Urgence.Critique, "Disque à remplacer", $"{Nom(x)} : {string.Join(" ; ", s.Critiques)}.",
                    "Sauvegarder les données immédiatement, puis remplacer le disque.", "Ne pas lancer de test d'écriture sur ce disque.", Confiance.Elevee);
            }
            else if (s.Avertissements.Count > 0)
            {
                points.Add(new(Statut.AVerifier, $"{Nom(x)} : {string.Join(" ; ", s.Avertissements)}"));
                A(Urgence.AVerifier, "Disque à surveiller", $"{Nom(x)} : {string.Join(" ; ", s.Avertissements)}.", id);
            }
            else points.Add(new(Statut.Conforme, $"{Nom(x)} : aucun indicateur de panne lu (ne garantit pas l'absence de panne future)"));
        }

        var hdd = internes.FirstOrDefault(x => x.Type == TypeDisque.Hdd);
        if (hdd is not null)
        {
            points.Add(new(Statut.Anomalie, $"{Nom(hdd)} : disque mécanique, lenteurs probables"));
            A(Urgence.Important, $"Disque dur mécanique {(hdd.TailleOctets is { } t ? Taille(t) : "")}".Trim(), "lenteurs probables au démarrage et à l'ouverture des logiciels.", id);
            var ssd = hdd.Bus == "NVMe" ? "SSD NVMe" : "SSD SATA";
            R(Categorie.Remplacer, Urgence.Important, $"HDD par un {ssd}", $"{Nom(hdd)} : disque mécanique.",
                "Remplacer par un SSD et transférer le système (clonage) ou réinstaller. Devis : à renseigner.", "Format du disque (2,5 pouces, M.2) à vérifier sur ce modèle ; sauvegarde avant intervention.", Confiance.Elevee);
        }

        if (d.VolumeSystemeOctets is { } taille and > 0 && d.VolumeSystemeLibreOctets is { } libre)
        {
            var pourcent = libre * 100.0 / taille;
            var texte = $"{Taille(libre)} libres sur le disque système ({pourcent:0} %)";
            if (libre < 10 * Go || pourcent < 10)
            {
                points.Add(new(Statut.Anomalie, texte + " : presque plein"));
                A(Urgence.Important, "Disque système presque plein", texte + " : mises à jour et logiciels risquent d'échouer.", id);
                R(Categorie.Optimiser, Urgence.Important, "Libérer de l'espace disque", texte + ".",
                    "Faire le tri avec le client (téléchargements, fichiers volumineux) ou ajouter un stockage.", "Aucune suppression sans l'accord du client.", Confiance.Elevee);
            }
            else points.Add(new(Statut.Conforme, texte));
        }
        else points.Add(new(Statut.AVerifier, "Espace libre du disque système non lu"));

        return new BlocAudit(id, "Stockage", Pire(points.Select(p => p.Statut)), valeur,
            d.VolumeSystemeLibreOctets is { } lib ? $"{Taille(lib)} libres" : "", interpretation)
            { Points = points, Source = "MSFT_PhysicalDisk, SMART / journal NVMe, Win32_LogicalDisk" };
    }

    private static BlocAudit Batterie(DonneesAudit d, Alerte A, Reco R)
    {
        const string id = "batterie";
        const string interpretation = "Remplacement à envisager selon l'usage";
        if (d.Batterie is not { } m)
            return d.Portable
                ? new BlocAudit(id, "Batterie", Statut.AVerifier, "Batterie non lue", "", interpretation)
                : new BlocAudit(id, "Batterie", Statut.NonApplicable, "PC fixe", "Pas de batterie", interpretation);
        if (!d.Portable && m.NombreBatteries == 0)
            return new BlocAudit(id, "Batterie", Statut.NonApplicable, "PC fixe", "Pas de batterie", interpretation);
        var c = RegleControles.Batterie(m);
        var valeur = m.SantePourcent is { } s ? $"{s} % de la capacité d'origine" : c.Statut == Statut.NonApplicable ? "PC fixe" : "Capacité non disponible";
        var detail = string.Join(" · ", m.Details());
        if (c.Statut == Statut.Anomalie)
        {
            var remplacer = m.SantePourcent < SeuilsBatterie.ParDefaut.Reserve;
            A(remplacer ? Urgence.Important : Urgence.Amelioration, "Batterie usée", $"{m.SantePourcent} % de la capacité d'origine.", id);
            R(Categorie.Remplacer, remplacer ? Urgence.Important : Urgence.Amelioration, "Remplacement de la batterie", $"{m.SantePourcent} % de la capacité d'origine ({detail}).",
                "Proposer une batterie neuve si le client utilise le portable sur batterie.", "Selon l'usage ; référence de batterie à vérifier pour ce modèle. Devis : à renseigner.",
                m.Cycles is null ? Confiance.Moyenne : Confiance.Elevee);
        }
        return new BlocAudit(id, "Batterie", c.Statut, valeur, detail, interpretation) { Points = new[] { new PointAudit(c.Statut, c.Constat) }, Source = c.Source };
    }

    private static BlocAudit Securite(DonneesAudit d, DateTime aujourdhui, Alerte A, Reco R)
    {
        const string id = "securite";
        var p = new List<PointAudit>();
        var gere = d.Administre;

        // Antivirus (y compris tiers, par le Centre de sécurité de Windows).
        if (d.Antivirus is null) p.Add(new(Statut.AVerifier, "Antivirus : état non lu"));
        else
        {
            var actifs = d.Antivirus.Where(a => a.Actif).ToList();
            if (actifs.Count == 0)
            {
                p.Add(new(Statut.Anomalie, d.Antivirus.Count == 0 ? "Aucun antivirus détecté" : "Antivirus désactivé : " + string.Join(", ", d.Antivirus.Select(a => a.Nom))));
                A(Urgence.Critique, "Pas d'antivirus actif", "le PC n'a aucune protection en temps réel.", id);
                R(Categorie.Reparer, Urgence.Critique, "Réactiver une protection antivirus", "Aucun antivirus actif d'après le Centre de sécurité de Windows.",
                    "Réactiver Microsoft Defender ou l'antivirus du client.", gere ? "Poste administré : voir avec l'administrateur." : "Vérifier qu'aucun logiciel malveillant ne l'a coupé.", Confiance.Elevee);
            }
            else if (actifs.All(a => !a.AJour))
            {
                p.Add(new(Statut.Anomalie, string.Join(", ", actifs.Select(a => a.Nom)) + " actif, définitions périmées"));
                A(Urgence.Important, "Antivirus pas à jour", string.Join(", ", actifs.Select(a => a.Nom)) + " : définitions périmées.", id);
            }
            else if (actifs.Count > 1) p.Add(new(Statut.AVerifier, "Plusieurs antivirus actifs : " + string.Join(", ", actifs.Select(a => a.Nom))));
            else p.Add(new(Statut.Conforme, actifs[0].Nom + " actif, à jour"));
        }

        // Pare-feu par profil.
        if (d.PareFeuTiers is { Count: > 0 } tiers) p.Add(new(Statut.Conforme, "Pare-feu : " + string.Join(", ", tiers) + " actif"));
        else if (d.ProfilsPareFeuCoupes is null) p.Add(new(Statut.AVerifier, "Pare-feu : état non lu"));
        else if (d.ProfilsPareFeuCoupes.Count == 0) p.Add(new(Statut.Conforme, "Pare-feu actif sur les 3 profils"));
        else if (gere || d.PareFeuParStrategie)
            p.Add(new(Statut.AVerifier, $"Pare-feu Windows coupé (réseau {string.Join(", ", d.ProfilsPareFeuCoupes)}) sur un poste administré : à voir avec l'administrateur"));
        else
        {
            var publicCoupe = d.ProfilsPareFeuCoupes.Contains("public");
            p.Add(new(Statut.Anomalie, "Pare-feu Windows désactivé pour le réseau " + string.Join(", ", d.ProfilsPareFeuCoupes)));
            A(publicCoupe ? Urgence.Important : Urgence.Amelioration, "Pare-feu désactivé", "réseau " + string.Join(", ", d.ProfilsPareFeuCoupes) + ".", id);
            R(Categorie.Reparer, publicCoupe ? Urgence.Important : Urgence.Amelioration, "Réactiver le pare-feu Windows", "Profils coupés : " + string.Join(", ", d.ProfilsPareFeuCoupes) + ".",
                "Réactiver le pare-feu sur tous les profils.", "Vérifier avec le client qu'aucun logiciel n'en dépend.", Confiance.Elevee);
        }

        // Mises à jour.
        if (d.DerniereMiseAJour is not { } maj) p.Add(new(Statut.AVerifier, "Mises à jour : date de la dernière installation non lue"));
        else
        {
            var jours = (int)(aujourdhui - maj.Date).TotalDays;
            var texte = $"Mises à jour : dernière installée il y a {jours} jour(s) ({maj:dd/MM/yyyy})";
            if (jours > 60)
            {
                p.Add(new(Statut.Anomalie, texte));
                A(gere ? Urgence.AVerifier : Urgence.Critique, "Windows ne se met plus à jour", $"dernière mise à jour le {maj:dd/MM/yyyy}.", id);
                R(Categorie.Reparer, gere ? Urgence.AVerifier : Urgence.Critique, "Rétablir Windows Update", $"Aucune mise à jour depuis le {maj:dd/MM/yyyy}.",
                    "Lancer Windows Update, puis traiter les erreurs éventuelles.", gere ? "Poste administré : les mises à jour peuvent être gérées par l'entreprise." : "Espace disque suffisant.", Confiance.Elevee);
            }
            else if (jours > 35)
            {
                p.Add(new(Statut.Anomalie, texte));
                A(Urgence.Important, "Mises à jour en retard", $"dernière le {maj:dd/MM/yyyy}.", id);
            }
            else p.Add(new(Statut.Conforme, texte));
        }
        if (d.RedemarrageEnAttente) p.Add(new(Statut.AVerifier, "Redémarrage en attente pour finir une installation"));

        // Chiffrement (jamais de lecture ni d'export de la clé de récupération).
        switch (d.BitLocker)
        {
            case true: p.Add(new(Statut.Conforme, "Disque système chiffré (BitLocker)" + (d.BitLockerDetail is null ? "" : " : " + d.BitLockerDetail))); break;
            case false:
                p.Add(new(Statut.Anomalie, "Disque système non chiffré (BitLocker désactivé)"));
                if (d.Portable)
                {
                    A(Urgence.Important, "Portable non chiffré", "BitLocker désactivé : données lisibles en cas de vol.", id);
                    R(Categorie.Optimiser, Urgence.Important, "Chiffrement du disque", "Portable non chiffré.",
                        SupportWindows.Edition(d.EditionId) == "Famille" ? "Activer le chiffrement de l'appareil si le PC le prend en charge, sinon passer à l'édition Professionnel." : "Activer BitLocker.",
                        "Clé de récupération sauvegardée avec le client AVANT l'activation (compte Microsoft, Entra ID ou papier).", Confiance.Elevee);
                }
                break;
            default: p.Add(new(Statut.AVerifier, "BitLocker : état non lu" + (d.BitLockerDetail is null ? "" : " (" + d.BitLockerDetail + ")"))); break;
        }

        if (d.UacActif == false)
        {
            p.Add(new(gere ? Statut.AVerifier : Statut.Anomalie, "Contrôle de compte (UAC) désactivé" + (gere ? " sur un poste administré" : "")));
            if (!gere) A(Urgence.Important, "Contrôle de compte désactivé", "tout programme peut modifier Windows sans confirmation.", id);
        }
        else if (d.UacActif == true) p.Add(new(Statut.Conforme, "Contrôle de compte (UAC) actif"));
        else p.Add(new(Statut.AVerifier, "Contrôle de compte (UAC) : non lu"));

        if (d.SmartScreenActif == false) p.Add(new(gere ? Statut.AVerifier : Statut.Anomalie, "SmartScreen désactivé"));
        else if (d.SmartScreenActif == true) p.Add(new(Statut.Conforme, "SmartScreen actif"));

        if (d.BureauADistanceOuvert == true)
            p.Add(new(Statut.AVerifier, "Bureau à distance ouvert" + (gere ? " : poste administré, à voir avec son administrateur" : " : vérifier que c'est voulu")));
        if (d.PriseEnMainADistance.Count > 0)
            p.Add(new(Statut.AVerifier, "Prise en main à distance installée : " + string.Join(", ", d.PriseEnMainADistance) + " (vérifier avec le client qu'elle est connue)"));

        if (d.Administrateurs is null) p.Add(new(Statut.AVerifier, "Administrateurs : non lus" + (d.Administrateur ? "" : " (droits insuffisants)")));
        else p.Add(new(d.Administrateurs.Count > 2 && !gere ? Statut.AVerifier : Statut.Conforme, $"{d.Administrateurs.Count} compte(s) administrateur(s) local(aux)"));

        return new BlocAudit(id, "Sécurité", Pire(p.Select(x => x.Statut)), Valeur(p), "", "Alertes prioritaires")
            { Points = p, Source = "root\\SecurityCenter2, registre (pare-feu, UAC, SmartScreen, RDP), Win32_QuickFixEngineering, Win32_EncryptableVolume / Explorateur, Win32_Group" };
    }

    private static string Valeur(IReadOnlyCollection<PointAudit> points)
    {
        var a = points.Count(x => x.Statut == Statut.Anomalie);
        var v = points.Count(x => x.Statut == Statut.AVerifier);
        if (a == 0 && v == 0) return "Rien à signaler";
        return string.Join(", ", new[] { a > 0 ? $"{a} anomalie(s)" : null, v > 0 ? $"{v} à vérifier" : null }.Where(x => x is not null));
    }

    private static BlocAudit EnvironnementPro(DonneesAudit d)
    {
        const string interpretation = "Évite les fausses alertes sur un poste administré";
        var points = new List<PointAudit>();
        string Etat(bool? v, string oui, string non, string inconnu) => v switch { true => oui, false => non, _ => inconnu };
        points.Add(new(d.Domaine is null ? Statut.AVerifier : Statut.Conforme, Etat(d.Domaine, $"Domaine Active Directory{(d.NomDomaine is null ? "" : " " + d.NomDomaine)}", "Groupe de travail (pas de domaine)", "Domaine : non lu")));
        points.Add(new(d.EntraId is null ? Statut.AVerifier : Statut.Conforme, Etat(d.EntraId, "Joint à Microsoft Entra ID", "Pas joint à Entra ID", "Entra ID : non lu")));
        points.Add(new(d.Mdm is null ? Statut.AVerifier : Statut.Conforme, Etat(d.Mdm, $"Géré par MDM{(d.FournisseurMdm is null ? "" : " (" + d.FournisseurMdm + ")")}", "Pas de gestion MDM détectée", "Gestion MDM : non lue")));
        if (d.CompteProfessionnel == true) points.Add(new(Statut.Conforme, "Compte professionnel ou scolaire ajouté à la session"));
        var valeur = d.Administre ? "Poste administré" : d.Domaine is null && d.EntraId is null && d.Mdm is null ? "Non déterminé" : "Poste non administré";
        var statut = d.Administre ? Statut.Conforme : points.Any(x => x.Statut == Statut.AVerifier) ? Statut.AVerifier : Statut.NonApplicable;
        var detail = d.Administre ? "Réglages imposés par l'entreprise : à voir avec son administrateur avant toute recommandation" : "";
        return new BlocAudit("pro", "Environnement pro", statut, valeur, detail, interpretation)
            { Points = points, Source = "Win32_ComputerSystem, registre CloudDomainJoin / Enrollments / WorkplaceJoin" };
    }

    private static BlocAudit Sauvegarde(DonneesAudit d, DateTime aujourdhui, Alerte A, Reco R)
    {
        const string id = "sauvegarde";
        const string interpretation = "Risque de perte de données ; restauration jamais vérifiée par l'audit";
        var points = new List<PointAudit>();
        var restauration = d.DernierPointRestauration is { } pr
            ? $"dernier point de restauration le {pr:dd/MM/yyyy}" : "aucun point de restauration lu";
        if (d.Sauvegardes is null)
            return new BlocAudit(id, "Sauvegarde", Statut.AVerifier, "Non lue", restauration, interpretation);
        if (d.Sauvegardes.Count == 0)
        {
            points.Add(new(Statut.Anomalie, "Aucune solution de sauvegarde détectée"));
            A(Urgence.Important, "Aucune sauvegarde détectée", "risque de perte des données en cas de panne, de vol ou de rançongiciel.", id);
            R(Categorie.Optimiser, Urgence.Important, "Mise en place d'une sauvegarde testée", "Aucune solution de sauvegarde détectée sur le PC.",
                "Choisir avec le client une sauvegarde (disque externe, cloud, NAS) puis faire un essai de restauration.", "Une sauvegarde sur un autre PC ou un NAS peut exister sans être visible ici : demander au client.", Confiance.Moyenne);
        }
        else
        {
            points.Add(new(Statut.AVerifier, string.Join(", ", d.Sauvegardes) + " : restauration jamais vérifiée"));
            A(Urgence.AVerifier, "Sauvegarde", string.Join(", ", d.Sauvegardes) + " détecté(s), restauration jamais vérifiée.", id);
            R(Categorie.Optimiser, Urgence.AVerifier, "Sauvegarde testée", string.Join(", ", d.Sauvegardes) + " détecté(s).",
                "Vérifier avec le client ce qui est sauvegardé, puis faire un essai de restauration.", "La présence d'un logiciel ne prouve pas que la sauvegarde fonctionne.", Confiance.Moyenne);
        }
        points.Add(new(Statut.AVerifier, restauration[0].ToString().ToUpperInvariant() + restauration.Substring(1)));
        var valeur = d.Sauvegardes.Count == 0 ? "Aucune détectée" : string.Join(", ", d.Sauvegardes);
        return new BlocAudit(id, "Sauvegarde", d.Sauvegardes.Count == 0 ? Statut.Anomalie : Statut.AVerifier, valeur, restauration, interpretation)
            { Points = points, Source = "Logiciels installés, dossiers OneDrive, Historique des fichiers, SystemRestore" };
    }

    private static BlocAudit Reseau(DonneesAudit d, Alerte A)
    {
        const string id = "reseau";
        const string interpretation = "Diagnostic de connectivité de base (accès Internet non testé)";
        if (d.Reseau is null) return new BlocAudit(id, "Réseau", Statut.AVerifier, "Non lu", "", interpretation);
        var points = new List<PointAudit>();
        var connectees = d.Reseau.Where(c => c.Connectee).ToList();
        foreach (var c in d.Reseau)
        {
            if (!c.Connectee) { points.Add(new(Statut.NonApplicable, $"{c.Type} débranché ou déconnecté : {c.Nom}")); continue; }
            var vitesse = c.VitesseBits is { } v and > 0 ? (v >= 1_000_000_000 ? $"{v / 1_000_000_000.0:0.#} Gbit/s" : $"{v / 1_000_000} Mbit/s") : null;
            var apipa = c.Ipv4.Any(ip => ip.StartsWith("169.254.", StringComparison.Ordinal));
            if (apipa)
            {
                points.Add(new(Statut.Anomalie, $"{c.Type} {c.Nom} : adresse automatique 169.254 (pas de réponse du serveur DHCP)"));
                A(Urgence.Important, "Connexion réseau sans adresse", $"{c.Type} : pas d'adresse fournie par la box ou le serveur.", id);
            }
            else if (c.Passerelle is null) points.Add(new(Statut.AVerifier, $"{c.Type} {c.Nom} : connecté sans passerelle (pas d'accès au-delà du réseau local)"));
            else if (c.Type == "Ethernet" && c.VitesseBits is > 0 and < 1_000_000_000)
                points.Add(new(Statut.AVerifier, $"{c.Type} {c.Nom} : lien à {vitesse} seulement (câble, prise ou box à vérifier)"));
            else points.Add(new(Statut.Conforme, $"{c.Type} connecté{(vitesse is null ? "" : $" à {vitesse}")} : {c.Nom}"));
        }
        if (connectees.Count == 0) points.Add(new(Statut.AVerifier, "Aucune carte réseau connectée"));
        var valeur = connectees.Count == 0 ? "Aucune connexion" : string.Join(" + ", connectees.Select(c => c.Type + " connecté").Distinct());
        var statut = Pire(points.Where(x => x.Statut != Statut.NonApplicable).Select(x => x.Statut).DefaultIfEmpty(Statut.AVerifier));
        return new BlocAudit(id, "Réseau", statut, valeur, "Accès Internet non testé : l'audit ne se connecte à rien", interpretation)
            { Points = points, Source = "Win32_NetworkAdapter, Win32_NetworkAdapterConfiguration" };
    }

    /// <summary>« Limites de cet audit » : ce qui n'a pas été lu ou vérifié, et ce que l'audit ne fait jamais.</summary>
    public static IReadOnlyList<string> Limites(DonneesAudit d)
    {
        var l = new List<string>();
        if (!d.Administrateur) l.Add("Lancé sans droits administrateur : certains contrôles restent « à vérifier ».");
        l.AddRange(d.NonLus);
        l.Add("Aucune modification du PC : ni réglage, ni registre, ni service, ni installation. Aucun test de performances ni écriture sur le disque.");
        l.Add("Accès Internet non testé : l'audit ne se connecte à aucun service.");
        l.Add("Sauvegardes : présence détectée seulement, restauration jamais vérifiée.");
        l.Add("BitLocker : la clé de récupération n'est jamais lue ni exportée ; sa sauvegarde est à vérifier avec le client.");
        l.Add("Santé des disques : l'absence d'alerte ne garantit pas l'absence de panne future.");
        l.Add("Rapport enregistré seulement à l'export, à l'endroit choisi par le technicien.");
        return l;
    }
}
