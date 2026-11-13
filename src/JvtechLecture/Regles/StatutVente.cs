using System;
using System.Collections.Generic;
using System.Linq;

namespace Jvtech.Lecture.Regles;

/// <summary>Statut d'un PC avant la vente (JVTECH Atelier, cahier § 3.1 étape 4).</summary>
public enum StatutVente
{
    /// <summary>Un contrôle obligatoire manque, n'a pas été fait ou n'a pas permis de conclure.</summary>
    Incomplet = 0,
    /// <summary>Au moins une anomalie bloquante : pas de vente en l'état.</summary>
    Bloque,
    /// <summary>Vendable, avec des réserves commerciales écrites dans la fiche et l'annonce.</summary>
    PretAvecReserve,
    Pret,
}

/// <summary>Le statut et ce qui l'explique, séparé en trois listes distinctes.</summary>
public sealed record BilanVente(
    StatutVente Statut,
    IReadOnlyList<Controle> Bloquants,
    IReadOnlyList<string> Manquants,
    IReadOnlyList<Controle> Reserves)
{
    public string Libelle => Statut switch
    {
        StatutVente.Pret => "Prêt",
        StatutVente.PretAvecReserve => "Prêt avec réserve",
        StatutVente.Bloque => "Bloqué",
        _ => "Incomplet",
    };
}

/// <summary>
/// Règles du statut avant vente (ajustement 3 du 13/11/2026) :
/// « Prêt » est impossible si un contrôle obligatoire est en échec ou n'a pas été fait ;
/// une anomalie bloquante et une réserve commerciale sont deux choses distinctes.
/// </summary>
public static class RegleVente
{
    /// <param name="controles">Résultats disponibles (un même Id ne doit apparaître qu'une fois).</param>
    /// <param name="obligatoires">Identifiants des contrôles obligatoires pour ce type de PC.</param>
    public static BilanVente Evaluer(IEnumerable<Controle> controles, IEnumerable<string> obligatoires)
    {
        var liste = controles.ToList();
        var doublon = liste.GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (doublon is not null) throw new ArgumentException($"Contrôle en double : {doublon.Key}", nameof(controles));

        var requis = new HashSet<string>(obligatoires, StringComparer.OrdinalIgnoreCase);
        var parId = liste.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

        var manquants = new List<string>();
        // Sans liste d'obligatoires, on ne peut rien affirmer : jamais « Prêt » par défaut.
        if (requis.Count == 0) manquants.Add("Aucun contrôle obligatoire défini");
        foreach (var id in requis.OrderBy(i => i, StringComparer.OrdinalIgnoreCase))
            if (!parId.TryGetValue(id, out var c) || c.Statut == Statut.AVerifier)
                manquants.Add(c?.Titre ?? id);

        // Une anomalie sur un contrôle obligatoire bloque, sauf si elle est explicitement une réserve commerciale.
        var bloquants = liste.Where(c => c.Statut == Statut.Anomalie
                                         && (c.Portee == Portee.Bloquante
                                             || (requis.Contains(c.Id) && c.Portee != Portee.ReserveCommerciale)))
                             .ToList();
        var reserves = liste.Where(c => c.Statut == Statut.Anomalie && c.Portee == Portee.ReserveCommerciale).ToList();

        var statut = bloquants.Count > 0 ? StatutVente.Bloque
            : manquants.Count > 0 ? StatutVente.Incomplet
            : reserves.Count > 0 ? StatutVente.PretAvecReserve
            : StatutVente.Pret;
        return new BilanVente(statut, bloquants, manquants, reserves);
    }
}
