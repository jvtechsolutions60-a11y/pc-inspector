using System;
using System.Collections.Generic;
using System.Linq;

namespace Jvtech.Lecture.Regles;

public enum ResultatTest
{
    NonTeste = 0,
    Reussi,
    Echec,
    NonApplicable,
}

/// <summary>Un test fait à la main par le technicien (écran, clavier…).</summary>
public sealed record TestManuel(string Id, string Libelle, bool PortableSeulement = false);

/// <summary>Résultat enregistré d'un test manuel, avec sa preuve : qui, quand, et la remarque.</summary>
public sealed record ResultatTestManuel(string Id, ResultatTest Resultat, Portee PorteeEchec, string? Remarque, string? Technicien, DateTime? Le);

/// <summary>Tests manuels de l'atelier (cahier § 3.5) et leur traduction en contrôles.</summary>
public static class TestsManuels
{
    public static IReadOnlyList<TestManuel> Liste { get; } = new[]
    {
        new TestManuel("ecran", "Écran (pixels, rétroéclairage, charnières)"),
        new TestManuel("clavier", "Clavier (toutes les touches)"),
        new TestManuel("pave-tactile", "Pavé tactile et clics", PortableSeulement: true),
        new TestManuel("son", "Haut-parleurs gauche et droite"),
        new TestManuel("micro", "Micro"),
        new TestManuel("webcam", "Webcam"),
        new TestManuel("usb", "Ports USB"),
        new TestManuel("wifi", "Wi-Fi"),
        new TestManuel("chargeur", "Chargeur et charge de la batterie", PortableSeulement: true),
        new TestManuel("etat", "État extérieur (coque, rayures, vis)"),
    };

    public static IEnumerable<TestManuel> Applicables(bool portable) => Liste.Where(t => portable || !t.PortableSeulement);

    public static string IdControle(string idTest) => "test-" + idTest;

    /// <summary>
    /// Réussi = Conforme ; Échec = Anomalie avec la portée choisie par le technicien (bloquante par défaut) ;
    /// Non testé = À vérifier ; Non applicable = Non applicable (par exemple pas de webcam sur ce modèle).
    /// </summary>
    public static Controle EnControle(TestManuel test, ResultatTestManuel? resultat)
    {
        var id = IdControle(test.Id);
        if (resultat is null || resultat.Resultat == ResultatTest.NonTeste)
            return new Controle(id, test.Libelle, Statut.AVerifier, "Non testé") { Source = "Test manuel" };

        var preuve = $"{resultat.Technicien ?? "?"}, {resultat.Le:dd/MM/yyyy HH:mm}";
        var remarque = string.IsNullOrWhiteSpace(resultat.Remarque) ? "" : " : " + resultat.Remarque!.Trim();
        return resultat.Resultat switch
        {
            ResultatTest.Reussi => new Controle(id, test.Libelle, Statut.Conforme, $"Réussi{remarque} ({preuve})") { Source = "Test manuel" },
            ResultatTest.NonApplicable => new Controle(id, test.Libelle, Statut.NonApplicable, $"Non applicable{remarque} ({preuve})") { Source = "Test manuel" },
            _ => new Controle(id, test.Libelle, Statut.Anomalie, $"Échec{remarque} ({preuve})")
                { Portee = resultat.PorteeEchec == Portee.Information ? Portee.Bloquante : resultat.PorteeEchec, Source = "Test manuel" },
        };
    }

    /// <summary>Contrôles obligatoires pour le statut avant vente : disque, mémoire rapide, batterie et tous les tests applicables.</summary>
    public static IReadOnlyList<string> Obligatoires(bool portable) =>
        new[] { RegleControles.IdDisque, RegleControles.IdMemoire, RegleControles.IdBatterie }
            .Concat(Applicables(portable).Select(t => IdControle(t.Id)))
            .ToList();
}
