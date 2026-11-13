using Jvtech.Lecture.Regles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JvtechTests;

/// <summary>« Tout lancer » d'Atelier : ordre des étapes, arrêt pour les tests manuels, jamais de Sysprep automatique.</summary>
[TestClass]
public class V2ParcoursAutoTests
{
    private static readonly EtatPreparation MajFinies = new() { Passes = 3, DerniereSuite = SuitePasse.Terminee };

    [TestMethod]
    public void Ordre_MisesAJour_Logiciels_Controles_Tests_Fiche()
    {
        Assert.AreEqual(EtapeAuto.MisesAJour, ParcoursAuto.Prochaine(null, false, false, false, false));
        Assert.AreEqual(EtapeAuto.MisesAJour, ParcoursAuto.Prochaine(new EtatPreparation { DerniereSuite = SuitePasse.NouvellePasse }, false, false, false, false));
        Assert.AreEqual(EtapeAuto.Redemarrer, ParcoursAuto.Prochaine(new EtatPreparation { DerniereSuite = SuitePasse.Redemarrer }, false, false, false, false));
        Assert.AreEqual(EtapeAuto.Logiciels, ParcoursAuto.Prochaine(MajFinies, false, false, false, false));
        Assert.AreEqual(EtapeAuto.Controles, ParcoursAuto.Prochaine(MajFinies with { PackReussi = true }, false, false, false, false));
        Assert.AreEqual(EtapeAuto.TestsManuels, ParcoursAuto.Prochaine(MajFinies with { PackReussi = true }, false, true, false, false));
        Assert.AreEqual(EtapeAuto.Fiche, ParcoursAuto.Prochaine(MajFinies with { PackReussi = true }, false, true, true, false));
        Assert.AreEqual(EtapeAuto.Termine, ParcoursAuto.Prochaine(MajFinies with { PackReussi = true }, false, true, true, true));
    }

    [DataTestMethod]
    [DataRow(SuitePasse.Bloquee)]
    [DataRow(SuitePasse.ArretLimite)]
    public void MisesAJourBloquees_OnPasseALaSuite(SuitePasse suite)
    {
        // Le blocage reste « À vérifier » dans le statut avant vente ; « Tout lancer » ne boucle pas dessus.
        Assert.AreEqual(EtapeAuto.Logiciels, ParcoursAuto.Prochaine(new EtatPreparation { DerniereSuite = suite }, false, false, false, false));
    }

    [TestMethod]
    public void LogicielsEnEchec_TentesUneSeuleFois()
    {
        var echec = MajFinies with { PackReussi = false };
        Assert.AreEqual(EtapeAuto.Logiciels, ParcoursAuto.Prochaine(echec, false, false, false, false));
        Assert.AreEqual(EtapeAuto.Controles, ParcoursAuto.Prochaine(echec, true, false, false, false));
    }

    [TestMethod]
    public void JamaisDeSysprepAutomatique()
    {
        foreach (var nom in System.Enum.GetNames(typeof(EtapeAuto)))
            Assert.IsFalse(nom.Contains("Sysprep") || nom.Contains("Finalis"), nom);
    }

    [TestMethod]
    public void ToutLancer_GardeApresRedemarrage()
    {
        var e = new EtatPreparation { DerniereSuite = SuitePasse.Redemarrer, RepriseProgrammee = true, ToutLancer = true };
        Assert.IsTrue(EtatPreparation.Lire(e.Ecrire()).ToutLancer);
        Assert.IsFalse(EtatPreparation.Lire("passes=1").ToutLancer);
    }
}
