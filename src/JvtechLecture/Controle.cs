namespace Jvtech.Lecture;

/// <summary>
/// Résultat d'un contrôle : ce qui a été constaté, d'où vient l'information et ce que cela implique pour la vente.
/// </summary>
/// <param name="Id">Identifiant stable (« disque-sante », « test-clavier »…), utilisé par les règles.</param>
/// <param name="Titre">Libellé affiché.</param>
/// <param name="Statut">Conforme, Anomalie, À vérifier ou Non applicable.</param>
/// <param name="Constat">Ce qui a été vu, en clair (« SMART illisible : contrôleur RAID »).</param>
public sealed record Controle(string Id, string Titre, Statut Statut, string Constat)
{
    /// <summary>Effet d'une anomalie sur la vente. Ignoré si le statut n'est pas <see cref="Statut.Anomalie"/>.</summary>
    public Portee Portee { get; init; } = Portee.Information;

    /// <summary>Source de l'information (classe WMI, journal Windows, test manuel…), pour la fiche technicien.</summary>
    public string? Source { get; init; }

    /// <summary>Contrôle non réalisé : statut À vérifier, constat explicite.</summary>
    public static Controle NonRealise(string id, string titre, string raison = "Contrôle non réalisé") =>
        new(id, titre, Statut.AVerifier, raison);
}
