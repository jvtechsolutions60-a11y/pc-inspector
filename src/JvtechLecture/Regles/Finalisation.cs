using System;
using System.Collections.Generic;

namespace Jvtech.Lecture.Regles;

/// <summary>Décision de finalisation (Sysprep) : permise ou non, et pourquoi.</summary>
public sealed record DecisionFinalisation(bool Permise, IReadOnlyList<string> Raisons);

/// <summary>
/// Garde-fou de la finalisation (Sysprep, premier démarrage et extinction) : le PC repart sur l'écran de premier démarrage de Windows
/// pour l'acheteur. Ne sert jamais à effacer des données. Toutes les conditions sont exigées :
/// 1. Windows en mode audit (relu juste avant le lancement) ; hors mode audit, jamais (ajustement 1).
/// 2. Statut avant vente « Prêt » ou « Prêt avec réserve » (ajustement 3).
/// 3. Fiche interne et annonce enregistrées avec le statut ET les réserves actuels (<see cref="BilanVente.Empreinte"/>).
/// 4. Confirmation à part : le technicien tape le numéro d'inventaire du PC (V…), qui doit être celui du dossier.
/// La décision ne lit rien sur le PC : elle est testée par des tests automatisés.
/// </summary>
public static class Finalisation
{
    public static DecisionFinalisation Decider(
        ModeWindows mode,
        BilanVente bilan,
        string? inventaireDossier,
        string? empreinteFicheEnregistree,
        string? saisieTechnicien)
    {
        var raisons = new List<string>();
        if (!Installation.FinalisationPermise(mode))
            raisons.Add($"Windows n'est pas en mode audit ({Installation.Libelle(mode)}) : la finalisation n'est pas proposée.");
        if (bilan.Statut is not (StatutVente.Pret or StatutVente.PretAvecReserve))
            raisons.Add($"Statut avant vente : {bilan.Libelle}. Il faut « Prêt » ou « Prêt avec réserve ».");

        var inventaire = IdentifiantAtelier.NormaliserInventaire(inventaireDossier);
        if (inventaire is null)
            raisons.Add("Numéro d'inventaire V… non enregistré dans le dossier.");

        if (empreinteFicheEnregistree is null)
            raisons.Add("Fiche interne et annonce pas encore enregistrées dans le dossier.");
        else if (!string.Equals(empreinteFicheEnregistree, bilan.Empreinte, StringComparison.Ordinal))
            raisons.Add($"Fiche enregistrée avec « {empreinteFicheEnregistree} », le statut ou les réserves ont changé depuis (« {bilan.Empreinte} ») : l'enregistrer à nouveau.");

        if (inventaire is not null && !string.Equals(IdentifiantAtelier.NormaliserInventaire(saisieTechnicien), inventaire, StringComparison.Ordinal))
            raisons.Add($"Confirmation : taper le numéro d'inventaire {inventaire} pour lancer la finalisation.");

        return new DecisionFinalisation(raisons.Count == 0, raisons);
    }
}
