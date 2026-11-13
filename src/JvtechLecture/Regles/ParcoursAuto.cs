namespace Jvtech.Lecture.Regles;

/// <summary>Ce que « Tout lancer » fait ensuite.</summary>
public enum EtapeAuto
{
    MisesAJour,
    /// <summary>Redémarrage demandé par Windows Update : fait seul, avec reprise programmée (limite de passes respectée).</summary>
    Redemarrer,
    Logiciels,
    Controles,
    /// <summary>Arrêt : le technicien fait les tests manuels (écran, clavier, son…), personne ne peut les faire à sa place.</summary>
    TestsManuels,
    Fiche,
    /// <summary>Tout est fait. Sysprep reste une confirmation à part (mode audit, n° V… tapé).</summary>
    Termine,
}

/// <summary>
/// « Tout lancer » (demande du 13/11/2026 : « avant on cliquait et basta fini ») : un clic enchaîne la préparation,
/// les contrôles et la fiche. Règle sans accès au PC, testée : chaque étape n'est tentée qu'une fois par lancement
/// (pas de boucle), un échec passe à la suite et reste visible dans le statut avant vente.
/// Ne lance jamais Sysprep et ne remplit jamais un test manuel.
/// </summary>
public static class ParcoursAuto
{
    public static EtapeAuto Prochaine(EtatPreparation? prep, bool logicielsTentes, bool controlesFaits, bool testsFaits, bool ficheAJour)
    {
        switch (prep?.DerniereSuite)
        {
            case null:
            case SuitePasse.NouvellePasse:
                return EtapeAuto.MisesAJour;
            case SuitePasse.Redemarrer:
                return EtapeAuto.Redemarrer;
        }
        // Terminee, Bloquee ou ArretLimite : on passe à la suite (un blocage reste « À vérifier » avant vente).
        if (prep!.PackReussi != true && !logicielsTentes) return EtapeAuto.Logiciels;
        if (!controlesFaits) return EtapeAuto.Controles;
        if (!testsFaits) return EtapeAuto.TestsManuels;
        if (!ficheAJour) return EtapeAuto.Fiche;
        return EtapeAuto.Termine;
    }

    public static string Libelle(EtapeAuto e) => e switch
    {
        EtapeAuto.MisesAJour => "Mises à jour Windows",
        EtapeAuto.Redemarrer => "Redémarrage pour finir les mises à jour",
        EtapeAuto.Logiciels => "Installation de 7-Zip et VLC (et des options cochées)",
        EtapeAuto.Controles => "Contrôles automatiques (disques, mémoire, batterie, pilotes)",
        EtapeAuto.TestsManuels => "À toi : tests manuels",
        EtapeAuto.Fiche => "Fiche, annonce et rapport",
        _ => "Terminé",
    };
}
