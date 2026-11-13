using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Jvtech.Lecture.Regles;

/// <summary>Ce qui suit une passe de Windows Update.</summary>
public enum SuitePasse
{
    /// <summary>Rien à installer et pas de redémarrage en attente : Windows est à jour.</summary>
    Terminee = 0,
    /// <summary>Des mises à jour ont été installées sans redémarrage : refaire une recherche tout de suite.</summary>
    NouvellePasse,
    /// <summary>Redémarrer, puis refaire une passe (reprise à l'ouverture de session).</summary>
    Redemarrer,
    /// <summary>Toutes les mises à jour trouvées ont échoué : arrêt, le technicien regarde (pas de boucle).</summary>
    Bloquee,
    /// <summary>Nombre maximal de passes atteint : arrêt, le technicien regarde.</summary>
    ArretLimite,
}

/// <summary>
/// Avancement de la préparation d'un PC, gardé dans preparation.ini (dossier du PC) pour reprendre après un
/// redémarrage. Texte simple « clé=valeur » : lisible et corrigeable dans le Bloc-notes.
/// </summary>
public sealed record EtatPreparation
{
    public int Passes { get; init; }
    public int MisesAJourInstallees { get; init; }
    public int MisesAJourEchouees { get; init; }
    public SuitePasse? DerniereSuite { get; init; }
    public string? DernierDetail { get; init; }

    /// <summary>Pack de base : null = pas lancé, true = tout installé (ou déjà présent), false = au moins un échec.</summary>
    public bool? PackReussi { get; init; }
    public string? PackDetail { get; init; }

    /// <summary>Atelier se relancera seul à l'ouverture de session pour faire la passe suivante.</summary>
    public bool RepriseProgrammee { get; init; }

    /// <summary>« Tout lancer » en cours : après un redémarrage, Atelier reprend toute la suite, pas seulement les mises à jour.</summary>
    public bool ToutLancer { get; init; }

    /// <summary>Le technicien accepte la préparation malgré un arrêt (mise à jour bloquée…) : nom, heure, raison.</summary>
    public string? AccepteePar { get; init; }
    public DateTime? AccepteeLe { get; init; }
    public string? RaisonAcceptation { get; init; }

    public DateTime? MisAJourLe { get; init; }

    public bool MisesAJourFinies => DerniereSuite == SuitePasse.Terminee;

    public string Ecrire()
    {
        var sb = new StringBuilder();
        sb.AppendLine("; Avancement de la préparation JVTECH Atelier (repris après un redémarrage)");
        void L(string cle, object? valeur) => sb.AppendLine(cle + "=" + Format(valeur));
        L("passes", Passes);
        L("maj_installees", MisesAJourInstallees);
        L("maj_echouees", MisesAJourEchouees);
        L("derniere_suite", DerniereSuite);
        L("dernier_detail", DernierDetail);
        L("pack_reussi", PackReussi);
        L("pack_detail", PackDetail);
        L("reprise_programmee", RepriseProgrammee);
        L("tout_lancer", ToutLancer);
        L("acceptee_par", AccepteePar);
        L("acceptee_le", AccepteeLe);
        L("raison_acceptation", RaisonAcceptation);
        L("mis_a_jour_le", MisAJourLe);
        return sb.ToString();
    }

    /// <summary>Relit le fichier. Une valeur illisible est ignorée (valeur par défaut), jamais devinée.</summary>
    public static EtatPreparation Lire(string? texte)
    {
        var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var brut in (texte ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var ligne = brut.Trim();
            if (ligne.Length == 0 || ligne.StartsWith(";") || ligne.StartsWith("#")) continue;
            var egal = ligne.IndexOf('=');
            if (egal <= 0) continue;
            v[ligne.Substring(0, egal).Trim()] = ligne.Substring(egal + 1).Trim().Replace("\\n", " ");
        }
        string? S(string cle) => v.TryGetValue(cle, out var s) && s.Length > 0 ? s : null;
        int I(string cle) => int.TryParse(S(cle), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 ? i : 0;
        bool? B(string cle) => S(cle) switch { "1" or "true" or "True" => true, "0" or "false" or "False" => false, _ => null };
        DateTime? D(string cle) => DateTime.TryParse(S(cle), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        SuitePasse? Suite() => Enum.TryParse<SuitePasse>(S("derniere_suite"), out var s) && Enum.IsDefined(typeof(SuitePasse), s) ? s : null;

        return new EtatPreparation
        {
            Passes = I("passes"),
            MisesAJourInstallees = I("maj_installees"),
            MisesAJourEchouees = I("maj_echouees"),
            DerniereSuite = Suite(),
            DernierDetail = S("dernier_detail"),
            PackReussi = B("pack_reussi"),
            PackDetail = S("pack_detail"),
            RepriseProgrammee = B("reprise_programmee") == true,
            ToutLancer = B("tout_lancer") == true,
            AccepteePar = S("acceptee_par"),
            AccepteeLe = D("acceptee_le"),
            RaisonAcceptation = S("raison_acceptation"),
            MisAJourLe = D("mis_a_jour_le"),
        };
    }

    private static string Format(object? valeur) => valeur switch
    {
        null => "",
        bool b => b ? "1" : "0",
        DateTime d => d.ToString("s", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => valeur.ToString()!.Replace("\r", " ").Replace("\n", " "),
    };
}

/// <summary>Règles de la préparation : enchaînement des passes Windows Update et contrôle « préparation » avant vente.</summary>
public static class PlanPreparation
{
    public const string IdPreparation = "preparation";
    public const string IdPilotes = "pilotes";

    /// <summary>Au-delà, la préparation s'arrête et le technicien regarde : jamais de boucle de redémarrages.</summary>
    public const int PassesMax = 6;

    /// <summary>
    /// Suite d'une passe. <paramref name="passe"/> est le numéro de la passe qui vient de se terminer (1 pour la première).
    /// </summary>
    public static SuitePasse Decider(int passe, int trouvees, int installees, bool redemarrageNecessaire)
    {
        if (trouvees == 0 && !redemarrageNecessaire) return SuitePasse.Terminee;
        if (trouvees > 0 && installees == 0) return SuitePasse.Bloquee;
        if (passe >= PassesMax) return SuitePasse.ArretLimite;
        return redemarrageNecessaire ? SuitePasse.Redemarrer : SuitePasse.NouvellePasse;
    }

    public static string Libelle(SuitePasse suite) => suite switch
    {
        SuitePasse.Terminee => "Windows est à jour",
        SuitePasse.NouvellePasse => "nouvelle recherche de mises à jour",
        SuitePasse.Redemarrer => "redémarrage nécessaire, puis passe suivante",
        SuitePasse.Bloquee => "mises à jour en échec : à vérifier (Paramètres > Windows Update)",
        SuitePasse.ArretLimite => $"arrêt après {PassesMax} passes : à vérifier",
        _ => suite.ToString(),
    };

    /// <summary>Contrôles obligatoires d'Atelier : ceux des tests (disque, mémoire, batterie, tests manuels) et la préparation.</summary>
    public static IReadOnlyList<string> Obligatoires(bool portable) =>
        TestsManuels.Obligatoires(portable).Concat(new[] { IdPreparation }).ToList();

    /// <summary>
    /// Préparation obligatoire avant vente : Windows à jour et pack de base installé. Un arrêt (mise à jour bloquée,
    /// limite de passes) reste « À vérifier » tant que le technicien ne l'a pas accepté, avec son nom et une raison.
    /// </summary>
    public static Controle EnControle(EtatPreparation? etat)
    {
        const string titre = "Préparation (mises à jour, pack de base)";
        const string source = "Journal du dossier, preparation.ini";
        if (etat is null || (etat.Passes == 0 && etat.PackReussi is null))
            return new Controle(IdPreparation, titre, Statut.AVerifier, "Préparation non lancée.") { Source = source };

        var manque = new List<string>();
        if (!etat.MisesAJourFinies)
            manque.Add(etat.DerniereSuite is { } s ? $"mises à jour : {Libelle(s)}" : "mises à jour non faites");
        if (etat.PackReussi != true)
            manque.Add(etat.PackReussi is null ? "pack de base non installé" : $"pack de base : {etat.PackDetail ?? "échec"}");

        var fait = $"{etat.Passes} passe(s) Windows Update, {etat.MisesAJourInstallees} mise(s) à jour installée(s)";
        if (manque.Count == 0)
            return new Controle(IdPreparation, titre, Statut.Conforme, fait + ", pack de base installé.") { Source = source };

        if (!string.IsNullOrWhiteSpace(etat.AccepteePar) && etat.AccepteeLe is { } le && !string.IsNullOrWhiteSpace(etat.RaisonAcceptation))
            return new Controle(IdPreparation, titre, Statut.Conforme,
                    $"Accepté en l'état par {etat.AccepteePar} le {le:dd/MM/yyyy HH:mm} ({etat.RaisonAcceptation}) malgré : {string.Join(" ; ", manque)}.")
                { Source = source };

        return new Controle(IdPreparation, titre, Statut.AVerifier, string.Join(" ; ", manque) + ".") { Source = source };
    }

    /// <summary>
    /// Périphériques en erreur dans le Gestionnaire de périphériques (pilote manquant ou en défaut). Pas obligatoire :
    /// un lecteur d'empreintes sans pilote n'empêche pas la vente, mais il devient une réserve écrite dans l'annonce.
    /// </summary>
    public static Controle Pilotes(IReadOnlyList<string>? peripheriquesEnErreur)
    {
        const string titre = "Pilotes";
        const string source = "Gestionnaire de périphériques (Win32_PnPEntity)";
        if (peripheriquesEnErreur is null)
            return new Controle(IdPilotes, titre, Statut.AVerifier, "Liste des périphériques illisible.") { Source = source };
        if (peripheriquesEnErreur.Count == 0)
            return new Controle(IdPilotes, titre, Statut.Conforme, "Aucun périphérique en erreur.") { Source = source };
        return new Controle(IdPilotes, titre, Statut.Anomalie, "Sans pilote ou en erreur : " + string.Join(", ", peripheriquesEnErreur))
            { Portee = Portee.ReserveCommerciale, Source = source };
    }
}
