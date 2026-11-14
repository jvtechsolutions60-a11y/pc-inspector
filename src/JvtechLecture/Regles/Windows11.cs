using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jvtech.Lecture.Regles;

/// <summary>Une ligne de la base : un modèle ou une série de processeurs pris en charge par Windows 11.</summary>
public sealed record RegleProcesseur(string Fabricant, string Libelle, string Source, Regex Motif);

/// <summary>
/// Base de compatibilité Windows 11 (ajustement 4) : officielle (listes Microsoft recopiées), datée, actualisable
/// par un fichier win11-processeurs.txt posé à côté du .exe (utilisé seulement s'il est plus récent et lisible).
/// </summary>
public sealed class BaseWindows11
{
    public const string NomFichier = "win11-processeurs.txt";

    private BaseWindows11(DateTime date, DateTime? releve, IReadOnlyList<RegleProcesseur> regles, string origine)
    {
        Date = date;
        Releve = releve;
        Regles = regles;
        Origine = origine;
    }

    /// <summary>Date de la liste officielle la plus récente reprise dans la base.</summary>
    public DateTime Date { get; }
    /// <summary>Date à laquelle les listes ont été recopiées.</summary>
    public DateTime? Releve { get; }
    public IReadOnlyList<RegleProcesseur> Regles { get; }
    public string Origine { get; }

    public string Libelle => $"base des processeurs du {Date:dd/MM/yyyy} ({Origine})";

    /// <summary>Lit une base. Lève FormatException si elle est incomplète ou si une ligne est illisible.</summary>
    public static BaseWindows11 Lire(string texte, string origine)
    {
        DateTime? date = null, releve = null;
        var regles = new List<RegleProcesseur>();
        var n = 0;
        foreach (var brut in texte.Replace("\r\n", "\n").Split('\n'))
        {
            n++;
            var ligne = brut.Trim();
            if (ligne.Length == 0 || ligne.StartsWith("#")) continue;
            if (ligne.StartsWith("date=", StringComparison.OrdinalIgnoreCase)) { date = Jour(ligne.Substring(5), n); continue; }
            if (ligne.StartsWith("releve=", StringComparison.OrdinalIgnoreCase)) { releve = Jour(ligne.Substring(7), n); continue; }
            var parties = ligne.Split(new[] { " | " }, 4, StringSplitOptions.None);
            if (parties.Length != 4 || parties.Any(p => p.Trim().Length == 0))
                throw new FormatException($"Ligne {n} illisible : « Fabricant | Libellé | Source | Motif » attendu.");
            Regex motif;
            try { motif = new Regex(parties[3].Trim(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)); }
            catch (ArgumentException ex) { throw new FormatException($"Ligne {n} : motif invalide ({ex.Message})."); }
            regles.Add(new RegleProcesseur(parties[0].Trim(), parties[1].Trim(), parties[2].Trim(), motif));
        }
        if (date is null) throw new FormatException("Date de la base absente (ligne « date=AAAA-MM-JJ »).");
        if (regles.Count == 0) throw new FormatException("Aucun processeur dans la base.");
        return new BaseWindows11(date.Value, releve, regles, origine);
    }

    private static DateTime Jour(string texte, int n) =>
        DateTime.TryParseExact(texte.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : throw new FormatException($"Ligne {n} : date « {texte.Trim()} » illisible (AAAA-MM-JJ attendu).");

    public static BaseWindows11 Integree { get; } = Lire(Donnees.BaseWindows11Donnees.Texte, "intégrée au logiciel");

    /// <summary>
    /// Base retenue : le fichier fourni s'il est lisible et plus récent que la base intégrée, sinon la base intégrée.
    /// <paramref name="avertissement"/> explique pourquoi un fichier présent n'a pas été retenu.
    /// </summary>
    public static BaseWindows11 Choisir(string? texteFichier, out string? avertissement)
    {
        avertissement = null;
        if (texteFichier is null) return Integree;
        try
        {
            var fichier = Lire(texteFichier, "fichier " + NomFichier);
            if (fichier.Date > Integree.Date) return fichier;
            avertissement = $"{NomFichier} ignoré : sa date ({fichier.Date:dd/MM/yyyy}) n'est pas plus récente que la base intégrée ({Integree.Date:dd/MM/yyyy}).";
        }
        catch (FormatException ex)
        {
            avertissement = $"{NomFichier} ignoré : {ex.Message}";
        }
        return Integree;
    }

    public RegleProcesseur? Trouver(string? nomProcesseur)
    {
        if (string.IsNullOrWhiteSpace(nomProcesseur)) return null;
        var nom = Regex.Replace(nomProcesseur, @"\s+", " ").Trim();
        foreach (var r in Regles)
        {
            try { if (r.Motif.IsMatch(nom)) return r; }
            catch (RegexMatchTimeoutException) { }
        }
        return null;
    }
}

/// <summary>Verdict Windows 11, du plus grave au plus favorable dans l'ordre de priorité d'affichage.</summary>
public enum VerdictWindows11
{
    /// <summary>Une exigence matérielle n'est pas remplie (processeur trop ancien, TPM 1.2, mémoire, disque).</summary>
    Incompatible = 0,
    /// <summary>Une information manque ou le processeur n'est pas dans la base : on ne conclut pas.</summary>
    Indetermine,
    /// <summary>Matériel conforme, mais un réglage du BIOS/UEFI est à corriger (Secure Boot, TPM, mode UEFI).</summary>
    ReglageUefi,
    Compatible,
}

public enum NaturePoint { Conforme, ReglageUefi, Incompatible, Indetermine }

public sealed record PointWindows11(string Nom, NaturePoint Nature, string Detail);

/// <summary>Ce qui est lu sur le PC pour juger la compatibilité. Null = non lu.</summary>
public sealed record MesuresWindows11
{
    public string? Processeur { get; init; }
    public int? Coeurs { get; init; }
    public int? FrequenceMhz { get; init; }
    public bool? Processeur64Bits { get; init; }
    public ulong? MemoireOctets { get; init; }
    public ulong? DisqueSystemeOctets { get; init; }
    /// <summary>Démarrage UEFI (true) ou BIOS hérité (false).</summary>
    public bool? Uefi { get; init; }
    /// <summary>Secure Boot : true activé, false désactivé, null non pris en charge ou non lu.</summary>
    public bool? SecureBoot { get; init; }
    /// <summary>Le TPM a pu être recherché (liste des périphériques ou WMI).</summary>
    public bool TpmLu { get; init; }
    /// <summary>« 2.0 », « 1.2 », « ? » (TPM présent, version non lue) ou null si aucun TPM visible.</summary>
    public string? TpmVersion { get; init; }
    public int? BuildWindows { get; init; }
}

public sealed record BilanWindows11(VerdictWindows11 Verdict, IReadOnlyList<PointWindows11> Points, string Base)
{
    public string Libelle => Verdict switch
    {
        VerdictWindows11.Compatible => "Compatible",
        VerdictWindows11.ReglageUefi => "Compatible après réglage du BIOS/UEFI",
        VerdictWindows11.Incompatible => "Incompatible (matériel)",
        _ => "Impossible de conclure",
    };
}

/// <summary>Règles de compatibilité Windows 11 (exigences officielles de Microsoft). Ne lit rien sur le PC.</summary>
public static class CompatibiliteWindows11
{
    private const ulong Go = 1024UL * 1024 * 1024;

    public static BilanWindows11 Evaluer(MesuresWindows11 m, BaseWindows11 baseProcesseurs)
    {
        var points = new List<PointWindows11>();
        void P(string nom, NaturePoint nature, string detail) => points.Add(new PointWindows11(nom, nature, detail));

        // Processeur : base officielle d'abord, jamais « incompatible » sur un simple doute.
        var regle = baseProcesseurs.Trouver(m.Processeur);
        if (string.IsNullOrWhiteSpace(m.Processeur)) P("Processeur", NaturePoint.Indetermine, "Nom du processeur non lu.");
        else if (regle is not null) P("Processeur", NaturePoint.Conforme, $"{m.Processeur} : liste Microsoft « {regle.Libelle} » ({baseProcesseurs.Libelle}).");
        else if (ProcesseurAncien(m.Processeur!))
            P("Processeur", NaturePoint.Incompatible, $"{m.Processeur} : absent de la liste Microsoft, génération antérieure aux processeurs pris en charge.");
        else P("Processeur", NaturePoint.Indetermine, $"{m.Processeur} : absent de la {baseProcesseurs.Libelle}. À vérifier sur la liste Microsoft (base à actualiser ?).");

        if (m.Coeurs is null || m.FrequenceMhz is null) P("Cœurs et fréquence", NaturePoint.Indetermine, "Non lus.");
        else if (m.Coeurs >= 2 && m.FrequenceMhz >= 1000) P("Cœurs et fréquence", NaturePoint.Conforme, $"{m.Coeurs} cœurs à {m.FrequenceMhz} MHz.");
        else P("Cœurs et fréquence", NaturePoint.Incompatible, $"{m.Coeurs} cœur(s) à {m.FrequenceMhz} MHz : il faut au moins 2 cœurs à 1 GHz.");

        if (m.Processeur64Bits == false) P("64 bits", NaturePoint.Incompatible, "Processeur 32 bits.");

        if (m.MemoireOctets is not { } ram) P("Mémoire", NaturePoint.Indetermine, "Non lue.");
        // La mémoire réservée au matériel fait parfois afficher 3,8 Go pour 4 Go installés.
        else if (ram >= 3.5 * Go) P("Mémoire", NaturePoint.Conforme, $"{ram / (double)Go:0.#} Go.");
        else P("Mémoire", NaturePoint.Incompatible, $"{ram / (double)Go:0.#} Go : il faut 4 Go (ajout de mémoire possible selon le PC).");

        if (m.DisqueSystemeOctets is not { } disque) P("Disque système", NaturePoint.Indetermine, "Taille non lue.");
        else if (disque >= 64_000_000_000UL) P("Disque système", NaturePoint.Conforme, $"{disque / 1e9:0} Go.");
        else P("Disque système", NaturePoint.Incompatible, $"{disque / 1e9:0} Go : il faut 64 Go.");

        switch (m.Uefi)
        {
            case true: P("Démarrage UEFI", NaturePoint.Conforme, "UEFI."); break;
            case false: P("Démarrage UEFI", NaturePoint.ReglageUefi, "Démarrage en mode BIOS hérité : convertir le disque en GPT (MBR2GPT, après sauvegarde) puis passer en UEFI."); break;
            default: P("Démarrage UEFI", NaturePoint.Indetermine, "Mode de démarrage non lu."); break;
        }

        switch (m.SecureBoot)
        {
            case true: P("Secure Boot", NaturePoint.Conforme, "Activé."); break;
            case false: P("Secure Boot", NaturePoint.ReglageUefi, "Pris en charge mais désactivé : à activer dans l'UEFI."); break;
            default:
                P("Secure Boot", m.Uefi == false ? NaturePoint.ReglageUefi : NaturePoint.Indetermine,
                    m.Uefi == false ? "Disponible seulement après le passage en UEFI." : "État non lu ou non pris en charge.");
                break;
        }

        if (!m.TpmLu) P("TPM 2.0", NaturePoint.Indetermine, "TPM non lu.");
        else if (m.TpmVersion == "2.0") P("TPM 2.0", NaturePoint.Conforme, "TPM 2.0 présent.");
        else if (m.TpmVersion == "?") P("TPM 2.0", NaturePoint.Indetermine, "TPM présent, version non lue (tpm.msc ou droits administrateur).");
        else if (m.TpmVersion is { } v) P("TPM 2.0", NaturePoint.Incompatible, $"TPM {v} : Windows 11 exige un TPM 2.0.");
        // Les processeurs de la liste ont un TPM intégré (fTPM AMD, PTT Intel), souvent désactivé dans l'UEFI.
        else if (regle is not null) P("TPM 2.0", NaturePoint.ReglageUefi, "Aucun TPM visible : activer le TPM intégré (fTPM / PTT / Security Device) dans l'UEFI.");
        else P("TPM 2.0", NaturePoint.Indetermine, "Aucun TPM visible.");

        var verdict = points.Any(p => p.Nature == NaturePoint.Incompatible) ? VerdictWindows11.Incompatible
            : points.Any(p => p.Nature == NaturePoint.Indetermine) ? VerdictWindows11.Indetermine
            : points.Any(p => p.Nature == NaturePoint.ReglageUefi) ? VerdictWindows11.ReglageUefi
            : VerdictWindows11.Compatible;
        return new BilanWindows11(verdict, points, baseProcesseurs.Libelle);
    }

    /// <summary>
    /// Processeurs nettement plus anciens que la liste Microsoft (Intel Core avant la 8e génération, Ryzen 1000,
    /// Core 2, Pentium 4/D, Athlon 64/II, Phenom, FX, Celeron/Pentium N3000/J3000). Rien d'autre n'est déclaré ancien.
    /// </summary>
    public static bool ProcesseurAncien(string nom)
    {
        if (Regex.IsMatch(nom, @"Core\(TM\)2|Core 2 (Duo|Quad)|Pentium\(R\) (D|4)\b|Athlon\(tm\) (64|II)|Athlon (64|II)|Phenom|\bFX-\d|\bA[4-9]-\d{4}|\bE[12]-\d{4}", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(nom, @"Core\(TM\) i[357] CPU\s+[A-Z]?\d{3}\b", RegexOptions.IgnoreCase)) return true; // 1re génération (i5 750)
        if (Regex.IsMatch(nom, @"\b(Celeron|Pentium)\(R\).*\b[NJ]3\d{3}\b|\b(Celeron|Pentium)\(R\).*\b[NJ]2\d{3}\b", RegexOptions.IgnoreCase)) return true;
        var gen = Regex.Match(nom, @"\b(\d{1,2})(?:st|nd|rd|th) Gen\b", RegexOptions.IgnoreCase);
        if (gen.Success) return int.Parse(gen.Groups[1].Value, CultureInfo.InvariantCulture) < 8;
        var intel = Regex.Match(nom, @"\bi[3579]-(\d{3,5})", RegexOptions.IgnoreCase);
        if (intel.Success)
        {
            var chiffres = intel.Groups[1].Value;
            // i5-750, i7-920 (1re génération, 3 chiffres) ; i5-2500 à i7-7700 (2e à 7e, 4 chiffres).
            if (chiffres.Length == 3) return true;
            if (chiffres.Length == 4 && chiffres[0] != '1') return chiffres[0] < '8';
            return false;
        }
        var ryzen = Regex.Match(nom, @"Ryzen\s+(?:Threadripper\s+)?[3579]?\s*(?:PRO\s+)?(1)\d{3}", RegexOptions.IgnoreCase);
        return ryzen.Success;
    }
}
