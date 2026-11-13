namespace Jvtech.Lecture;

/// <summary>
/// Les quatre statuts communs à JVTECH Atelier et JVTECH Audit (cahier des charges v2, § 1.1).
/// La valeur par défaut est <see cref="AVerifier"/> : une information manquante ou illisible n'est jamais « Conforme ».
/// </summary>
public enum Statut
{
    /// <summary>Contrôle non fait, information illisible ou impossible de conclure.</summary>
    AVerifier = 0,
    Conforme,
    Anomalie,
    /// <summary>Le contrôle ne concerne pas ce PC (batterie d'un PC fixe…).</summary>
    NonApplicable,
}

/// <summary>Ce qu'implique une anomalie pour la vente (ajustement 3 du 13/11/2026).</summary>
public enum Portee
{
    /// <summary>Information pour le technicien, sans effet sur la vente.</summary>
    Information = 0,
    /// <summary>Le PC peut être vendu, avec une réserve écrite dans la fiche et l'annonce (batterie usée, rayure…).</summary>
    ReserveCommerciale,
    /// <summary>Le PC ne peut pas être vendu en l'état (disque en défaut, écran cassé…).</summary>
    Bloquante,
}

public static class Libelles
{
    public static string De(Statut statut) => statut switch
    {
        Statut.Conforme => "Conforme",
        Statut.Anomalie => "Anomalie",
        Statut.NonApplicable => "Non applicable",
        _ => "À vérifier",
    };

    public static string De(Portee portee) => portee switch
    {
        Portee.ReserveCommerciale => "Réserve commerciale",
        Portee.Bloquante => "Anomalie bloquante",
        _ => "Information",
    };
}
