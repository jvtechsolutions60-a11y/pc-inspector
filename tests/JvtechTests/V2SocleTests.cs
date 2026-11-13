using System;
using System.Linq;
using System.Threading;
using Jvtech.Atelier.Actions;
using Jvtech.Lecture;
using Jvtech.Lecture.Regles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JvtechTests;

/// <summary>JVTECH v2, lot 0 : statuts communs. Une information absente n'est jamais « Conforme ».</summary>
[TestClass]
public class StatutTests
{
    [TestMethod]
    public void ParDefaut_AVerifier()
    {
        Assert.AreEqual(Statut.AVerifier, default(Statut));
        Assert.AreEqual(Portee.Information, default(Portee));
        Assert.AreEqual(StatutVente.Incomplet, default(StatutVente));
    }

    [TestMethod]
    public void Libelles_DuCahierDesCharges()
    {
        CollectionAssert.AreEqual(new[] { "À vérifier", "Conforme", "Anomalie", "Non applicable" },
            Enum.GetValues(typeof(Statut)).Cast<Statut>().Select(Libelles.De).ToArray());
    }

    [TestMethod]
    public void NonRealise_EstAVerifier()
    {
        var c = Controle.NonRealise("test-clavier", "Clavier");
        Assert.AreEqual(Statut.AVerifier, c.Statut);
        StringAssert.Contains(c.Constat, "non réalisé");
    }
}

/// <summary>Ajustement 6 : correction possible sans perdre la valeur détectée ; rien d'inventé.</summary>
[TestClass]
public class ChampTests
{
    private static readonly DateTime Quand = new(2026, 11, 13, 10, 0, 0);

    [DataTestMethod]
    [DataRow((string?)null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("To Be Filled By O.E.M.")]
    [DataRow("Default string")]
    [DataRow("System Serial Number")]
    [DataRow("0000000000")]
    [DataRow("00-00-00")]
    public void Remplissage_NonDetecte(string? brut)
    {
        var champ = new Champ("N° de série", brut, "Win32_BIOS");
        Assert.IsNull(champ.Detecte);
        Assert.IsFalse(champ.EstConnu);
        Assert.AreEqual("Non détecté", champ.Affichage);
    }

    [TestMethod]
    public void ValeurReelle_Nettoyee()
    {
        var champ = new Champ("Modèle", "  ThinkPad   T480\0 ");
        Assert.AreEqual("ThinkPad T480", champ.Detecte);
        Assert.AreEqual("ThinkPad T480", champ.Affichage);
    }

    [TestMethod]
    public void Correction_GardeLaValeurDetectee()
    {
        var champ = new Champ("Modèle", "20L5CTO1WW", "Win32_ComputerSystemProduct");
        champ.Corriger("ThinkPad T480", "Julien", Quand);

        Assert.AreEqual("ThinkPad T480", champ.Valeur);
        Assert.AreEqual("20L5CTO1WW", champ.Detecte);
        Assert.IsTrue(champ.EstCorrige);
        Assert.AreEqual("Julien", champ.CorrigePar);
        Assert.AreEqual(Quand, champ.CorrigeLe);
        StringAssert.Contains(champ.LigneFicheInterne(), "détecté : 20L5CTO1WW");

        champ.AnnulerCorrection();
        Assert.AreEqual("20L5CTO1WW", champ.Valeur);
        Assert.IsFalse(champ.EstCorrige);
    }

    [TestMethod]
    public void Correction_DUneValeurNonDetectee()
    {
        var champ = new Champ("Écran", null);
        champ.Corriger("14 pouces Full HD", "Julien", Quand);
        Assert.AreEqual("14 pouces Full HD", champ.Affichage);
        StringAssert.Contains(champ.LigneFicheInterne(), "détecté : Non détecté");
    }

    [TestMethod]
    public void Correction_IdentiqueOuVide()
    {
        var champ = new Champ("Modèle", "Latitude 5490");
        champ.Corriger("Latitude 5490", "Julien", Quand);
        Assert.IsFalse(champ.EstCorrige);
        Assert.ThrowsException<ArgumentException>(() => champ.Corriger("  ", "Julien", Quand));
        Assert.ThrowsException<ArgumentException>(() => champ.Corriger("Default string", "Julien", Quand));
    }
}

/// <summary>Ajustement 3 : « Prêt » impossible si un obligatoire est en échec ou non fait ; réserve ≠ bloquant.</summary>
[TestClass]
public class RegleVenteTests
{
    private static readonly string[] Obligatoires = { "disque", "clavier", "batterie" };

    private static Controle Ok(string id) => new(id, id, Statut.Conforme, "ok");

    [TestMethod]
    public void ToutConforme_Pret()
    {
        var bilan = RegleVente.Evaluer(Obligatoires.Select(Ok), Obligatoires);
        Assert.AreEqual(StatutVente.Pret, bilan.Statut);
        Assert.AreEqual("Prêt", bilan.Libelle);
    }

    [TestMethod]
    public void ObligatoireAbsent_Incomplet()
    {
        var bilan = RegleVente.Evaluer(new[] { Ok("disque"), Ok("batterie") }, Obligatoires);
        Assert.AreEqual(StatutVente.Incomplet, bilan.Statut);
        CollectionAssert.AreEqual(new[] { "clavier" }, bilan.Manquants.ToArray());
    }

    [TestMethod]
    public void ObligatoireNonRealise_Incomplet()
    {
        var bilan = RegleVente.Evaluer(new[] { Ok("disque"), Ok("batterie"), Controle.NonRealise("clavier", "Clavier") }, Obligatoires);
        Assert.AreEqual(StatutVente.Incomplet, bilan.Statut);
        CollectionAssert.AreEqual(new[] { "Clavier" }, bilan.Manquants.ToArray());
    }

    [TestMethod]
    public void AnomalieSurObligatoire_SansPortee_Bloque()
    {
        var disque = new Controle("disque", "Disque", Statut.Anomalie, "secteurs réalloués");
        var bilan = RegleVente.Evaluer(new[] { disque, Ok("clavier"), Ok("batterie") }, Obligatoires);
        Assert.AreEqual(StatutVente.Bloque, bilan.Statut);
        Assert.AreSame(disque, bilan.Bloquants.Single());
    }

    [TestMethod]
    public void ReserveCommerciale_PretAvecReserve()
    {
        var batterie = new Controle("batterie", "Batterie", Statut.Anomalie, "71 %") { Portee = Portee.ReserveCommerciale };
        var bilan = RegleVente.Evaluer(new[] { Ok("disque"), Ok("clavier"), batterie }, Obligatoires);
        Assert.AreEqual(StatutVente.PretAvecReserve, bilan.Statut);
        Assert.AreEqual(0, bilan.Bloquants.Count);
        Assert.AreSame(batterie, bilan.Reserves.Single());
    }

    [TestMethod]
    public void BloquantFacultatif_Bloque()
    {
        var ecran = new Controle("ecran", "Écran", Statut.Anomalie, "dalle fêlée") { Portee = Portee.Bloquante };
        var bilan = RegleVente.Evaluer(Obligatoires.Select(Ok).Append(ecran), Obligatoires);
        Assert.AreEqual(StatutVente.Bloque, bilan.Statut);
    }

    [TestMethod]
    public void BloquantEtManquant_LesDeuxListesRemplies()
    {
        var disque = new Controle("disque", "Disque", Statut.Anomalie, "en défaut") { Portee = Portee.Bloquante };
        var bilan = RegleVente.Evaluer(new[] { disque }, Obligatoires);
        Assert.AreEqual(StatutVente.Bloque, bilan.Statut);
        Assert.AreEqual(2, bilan.Manquants.Count);
    }

    [TestMethod]
    public void NonApplicable_Accepte()
    {
        var batterie = new Controle("batterie", "Batterie", Statut.NonApplicable, "PC fixe");
        var bilan = RegleVente.Evaluer(new[] { Ok("disque"), Ok("clavier"), batterie }, Obligatoires);
        Assert.AreEqual(StatutVente.Pret, bilan.Statut);
    }

    [TestMethod]
    public void SansObligatoires_JamaisPret()
    {
        Assert.AreEqual(StatutVente.Incomplet, RegleVente.Evaluer(Array.Empty<Controle>(), Array.Empty<string>()).Statut);
        Assert.AreEqual(StatutVente.Incomplet, RegleVente.Evaluer(new[] { Ok("disque") }, Array.Empty<string>()).Statut);
    }

    [TestMethod]
    public void Doublon_Refuse()
    {
        Assert.ThrowsException<ArgumentException>(() => RegleVente.Evaluer(new[] { Ok("disque"), Ok("DISQUE") }, Obligatoires));
    }
}

/// <summary>Chaque action d'Atelier est tracée : état avant, état après, code retour, durée.</summary>
[TestClass]
public class ActionJournalTests
{
    private sealed class Fausse : IActionAtelier
    {
        public string Etat = "VLC absent";
        public Func<CancellationToken, SortieAction> Faire = _ => new SortieAction(true, 0, "installé");
        public Func<string>? Lire;
        public string Id => "pack-vlc";
        public string Libelle => "VLC";
        public bool DemandeAdministrateur => true;
        public string LireEtat() => Lire?.Invoke() ?? Etat;
        public SortieAction Executer(CancellationToken annulation) => Faire(annulation);
    }

    private static readonly Func<DateTime> Horloge = () => new DateTime(2026, 11, 13, 10, 0, 0);

    [TestMethod]
    public void Reussite_AvantApres()
    {
        var a = new Fausse();
        a.Faire = _ => { a.Etat = "VLC 3.0.23"; return new SortieAction(true, 0, "installé"); };
        var r = ActionJournal.Lancer(a, CancellationToken.None, Horloge);
        Assert.AreEqual(ResultatAction.Reussi, r.Resultat);
        Assert.AreEqual("VLC absent", r.EtatAvant);
        Assert.AreEqual("VLC 3.0.23", r.EtatApres);
        Assert.AreEqual(0, r.CodeRetour);
        StringAssert.Contains(r.Ligne(), "pack-vlc | Reussi | code 0");
    }

    [TestMethod]
    public void CodeRetourEchec_Echec()
    {
        var a = new Fausse { Faire = _ => new SortieAction(false, 1603, "refusé par Windows") };
        var r = ActionJournal.Lancer(a, CancellationToken.None, Horloge);
        Assert.AreEqual(ResultatAction.Echec, r.Resultat);
        Assert.AreEqual(1603, r.CodeRetour);
    }

    [TestMethod]
    public void Exception_EchecTrace()
    {
        var a = new Fausse { Faire = _ => throw new InvalidOperationException("boum") };
        var r = ActionJournal.Lancer(a, CancellationToken.None, Horloge);
        Assert.AreEqual(ResultatAction.Echec, r.Resultat);
        StringAssert.Contains(r.Detail, "boum");
    }

    [TestMethod]
    public void Annulation_PendantOuAvant()
    {
        var a = new Fausse { Faire = t => throw new OperationCanceledException(t) };
        Assert.AreEqual(ResultatAction.Annule, ActionJournal.Lancer(a, CancellationToken.None, Horloge).Resultat);

        var lance = false;
        var b = new Fausse { Faire = _ => { lance = true; return new SortieAction(true, 0, ""); } };
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = ActionJournal.Lancer(b, cts.Token, Horloge);
        Assert.AreEqual(ResultatAction.NonLance, r.Resultat);
        Assert.IsFalse(lance);
    }

    [TestMethod]
    public void EtatIllisible_NoteSansPlanter()
    {
        var a = new Fausse { Lire = () => throw new UnauthorizedAccessException("refusé") };
        var r = ActionJournal.Lancer(a, CancellationToken.None, Horloge);
        StringAssert.StartsWith(r.EtatAvant, "illisible");
        Assert.AreEqual(ResultatAction.Reussi, r.Resultat);
    }
}
