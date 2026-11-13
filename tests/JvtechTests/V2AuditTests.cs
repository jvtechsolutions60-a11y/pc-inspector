using System;
using System.Collections.Generic;
using System.Linq;
using Jvtech.Lecture;
using Jvtech.Lecture.Collecte;
using Jvtech.Lecture.Regles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JvtechTests;

/// <summary>
/// JVTECH Audit (lot 2) : règles testées avec des données simulées, scénarios de recette T01 à T11 du cahier.
/// Ces tests ne remplacent pas la recette sur PC réels (docs/v2/recette-audit.md).
/// </summary>
[TestClass]
public class V2AuditTests
{
    private const ulong Go = 1024UL * 1024 * 1024;
    private static readonly DateTime Le = new(2026, 11, 13, 10, 0, 0);
    private const string Cpu8 = "Intel(R) Core(TM) i5-8350U CPU @ 1.70GHz";

    /// <summary>Dell Latitude 5490 de la maquette : Windows 10 Pro, i5-8350U, 8 Go, HDD 500 Go, Secure Boot désactivé.</summary>
    private static DonneesAudit Maquette => new()
    {
        Fabricant = "Dell Inc.", Modele = "Latitude 5490", NumeroSerie = "7XK2Q32", NomPc = "PORTABLE-COMPTA", Portable = true, Bios = "1.32.0 (2023)",
        PeripheriquesEnErreur = Array.Empty<string>(),
        WindowsNom = "Windows 10 Pro", EditionId = "Professional", VersionAffichee = "22H2", Build = 19045, Active = true,
        Windows11 = new MesuresWindows11
        {
            Processeur = Cpu8, Coeurs = 4, FrequenceMhz = 1900, Processeur64Bits = true, MemoireOctets = 8 * Go,
            DisqueSystemeOctets = 500_000_000_000, Uefi = true, SecureBoot = false, TpmLu = true, TpmVersion = "2.0", BuildWindows = 19045,
        },
        Processeur = Cpu8, Coeurs = 4, Threads = 8,
        MemoireOctets = 8 * Go, TypeMemoire = "DDR4", Barrettes = 1, EmplacementsAnnonces = 2,
        Disques = new[] { new DisqueAudit("ST500LM034", TypeDisque.Hdd, "SATA", 500_000_000_000, false, new EtatDisque("ST500LM034", true, Array.Empty<string>(), Array.Empty<string>())) },
        VolumeSystemeOctets = 499_000_000_000, VolumeSystemeLibreOctets = 212_000_000_000,
        Batterie = new MesureBatterie(true, 1, 68000, 42600, 312),
        Antivirus = new[] { new AntivirusAudit("Windows Defender", true, true) },
        PareFeuTiers = Array.Empty<string>(), ProfilsPareFeuCoupes = Array.Empty<string>(),
        DerniereMiseAJour = Le.AddDays(-9), BitLocker = false, UacActif = true, SmartScreenActif = true, BureauADistanceOuvert = false,
        Administrateurs = null,
        Domaine = false, EntraId = false, Mdm = false, CompteProfessionnel = false,
        Sauvegardes = new[] { "OneDrive (Documents)" }, DernierPointRestauration = null,
        Reseau = new[] { new CarteReseau("Intel Wireless-AC 8265", "Wi-Fi", true, 433_000_000, true, new[] { "192.168.1.24" }, "192.168.1.1"),
                         new CarteReseau("Intel Ethernet I219-LM", "Ethernet", false, null, true, Array.Empty<string>(), null) },
        Administrateur = false, LangueWindows = "fr-FR", Date = Le, Duree = TimeSpan.FromSeconds(38),
    };

    private static ResultatAudit Evaluer(DonneesAudit d) => RegleAudit.Evaluer(d, BaseWindows11.Integree);
    private static BlocAudit Bloc(ResultatAudit r, string id) => r.Blocs.Single(b => b.Id == id);

    [TestMethod]
    public void OnzeBlocs_DansLOrdreDuCahier()
    {
        var r = Evaluer(Maquette);
        CollectionAssert.AreEqual(new[] { "identification", "windows", "windows11", "processeur", "memoire", "stockage", "batterie", "securite", "pro", "sauvegarde", "reseau" },
            r.Blocs.Select(b => b.Id).ToList());
    }

    [TestMethod]
    public void RienDeLu_JamaisConforme()
    {
        // Pas de faux « OK » : WMI muet, registre illisible, droits insuffisants.
        var r = Evaluer(new DonneesAudit { Date = Le });
        foreach (var b in r.Blocs)
            Assert.IsTrue(b.Statut is Statut.AVerifier or Statut.NonApplicable, $"{b.Titre} : {b.Statut}");
        Assert.AreEqual(VerdictWindows11.Indetermine, r.Windows11?.Verdict ?? VerdictWindows11.Indetermine);
    }

    [TestMethod]
    public void T01_Windows10Pro_CpuCompatible_ReglageUefi()
    {
        var r = Evaluer(Maquette);
        var w = Bloc(r, "windows");
        Assert.AreEqual(Statut.Anomalie, w.Statut);
        Assert.AreEqual("Windows 10 Professionnel", w.Valeur);
        Assert.IsTrue(w.Points.Any(p => p.Texte.Contains("14/10/2025") && p.Texte.Contains("ESU")));
        var w11 = Bloc(r, "windows11");
        Assert.AreEqual("Configuration à vérifier", w11.Valeur);
        Assert.AreEqual(Statut.AVerifier, w11.Statut);
        StringAssert.Contains(w11.Detail, "base des processeurs du");
        var reco = r.Recommandations.Single(x => x.Titre.StartsWith("Passage à Windows 11"));
        StringAssert.Contains(reco.Constat, "Secure Boot");
        Assert.AreEqual(Confiance.Elevee, reco.Confiance);
        StringAssert.Contains(reco.Conditions, "jamais par JVTECH Audit");
    }

    [TestMethod]
    public void T01_Windows10_ToutConforme_PassageWindows11()
    {
        var d = Maquette with { Windows11 = Maquette.Windows11! with { SecureBoot = true } };
        var r = Evaluer(d);
        Assert.AreEqual(Statut.Conforme, Bloc(r, "windows11").Statut);
        Assert.IsTrue(r.Recommandations.Any(x => x.Titre == "Passage à Windows 11" && x.Categorie == Categorie.Optimiser));
    }

    [TestMethod]
    public void T02_Windows10_CpuNonPrisEnCharge_RaisonExacte()
    {
        const string ancien = "Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz";
        var d = Maquette with { Processeur = ancien, Windows11 = Maquette.Windows11! with { Processeur = ancien } };
        var r = Evaluer(d);
        var w11 = Bloc(r, "windows11");
        Assert.AreEqual(Statut.Anomalie, w11.Statut);
        Assert.IsTrue(w11.Points.Any(p => p.Statut == Statut.Anomalie && p.Texte.StartsWith("Processeur")));
        var reco = r.Recommandations.Single(x => x.Categorie == Categorie.Remplacer && x.Titre.StartsWith("Renouvellement"));
        StringAssert.Contains(reco.Constat, "Processeur");
        Assert.AreEqual(Statut.Anomalie, Bloc(r, "processeur").Statut);
    }

    [TestMethod]
    public void T03_TpmNonLu_JamaisCompatible()
    {
        var d = Maquette with { Windows11 = Maquette.Windows11! with { SecureBoot = true, TpmLu = false, TpmVersion = null } };
        var r = Evaluer(d);
        Assert.AreEqual(VerdictWindows11.Indetermine, r.Windows11!.Verdict);
        Assert.AreEqual(Statut.AVerifier, Bloc(r, "windows11").Statut);
        Assert.IsTrue(r.Recommandations.Any(x => x.Titre.Contains("à confirmer") && x.Confiance == Confiance.Faible));
    }

    [TestMethod]
    public void T04_AntivirusTiers_Reconnu_OuAbsent_Critique()
    {
        var tiers = Evaluer(Maquette with { Antivirus = new[] { new AntivirusAudit("Windows Defender", false, true), new AntivirusAudit("ESET Security", true, true) } });
        Assert.IsTrue(Bloc(tiers, "securite").Points.Any(p => p.Statut == Statut.Conforme && p.Texte.StartsWith("ESET Security actif")));

        var aucun = Evaluer(Maquette with { Antivirus = Array.Empty<AntivirusAudit>() });
        Assert.AreEqual(Urgence.Critique, aucun.Prioritaires.First().Urgence);
        Assert.AreEqual("Pas d'antivirus actif", aucun.Prioritaires.First().Titre);

        var illisible = Evaluer(Maquette with { Antivirus = null });
        Assert.IsTrue(Bloc(illisible, "securite").Points.Any(p => p.Statut == Statut.AVerifier && p.Texte.StartsWith("Antivirus")));
    }

    [TestMethod]
    public void T05_PosteAdministre_PasDeFaussesAlertes()
    {
        var d = Maquette with
        {
            Domaine = true, NomDomaine = "corp.exemple.fr", Mdm = true, FournisseurMdm = "Microsoft Intune",
            ProfilsPareFeuCoupes = new[] { "domaine" }, PareFeuParStrategie = true, UacActif = false, BureauADistanceOuvert = true,
            Administrateurs = new[] { "PORTABLE-COMPTA\\Administrateur", "CORP\\Admins du domaine", "CORP\\support" },
        };
        var r = Evaluer(d);
        Assert.AreEqual("Poste administré", Bloc(r, "pro").Valeur);
        var secu = Bloc(r, "securite");
        Assert.IsFalse(secu.Points.Any(p => p.Statut == Statut.Anomalie && (p.Texte.Contains("Pare-feu") || p.Texte.Contains("UAC") || p.Texte.Contains("Bureau"))),
            string.Join(" | ", secu.Points.Select(p => p.Texte)));
        Assert.IsFalse(r.Alertes.Any(a => a.Titre is "Pare-feu désactivé" or "Contrôle de compte désactivé"));
    }

    [TestMethod]
    public void T06_HddSain_RemplacementSsdPropose_SansDireSain()
    {
        var r = Evaluer(Maquette);
        var s = Bloc(r, "stockage");
        Assert.AreEqual("HDD 500 Go SATA", s.Valeur);
        Assert.AreEqual(Statut.Anomalie, s.Statut);
        Assert.IsTrue(s.Points.Any(p => p.Texte.Contains("ne garantit pas")));
        Assert.IsTrue(r.Recommandations.Any(x => x.Titre == "HDD par un SSD SATA" && x.Solution.Contains("Devis : à renseigner")));

        var ssd = Evaluer(Maquette with { Disques = new[] { new DisqueAudit("Samsung PM981", TypeDisque.Ssd, "NVMe", 256_000_000_000, false, new EtatDisque("PM981", true, Array.Empty<string>(), Array.Empty<string>())) } });
        Assert.AreEqual(Statut.Conforme, Bloc(ssd, "stockage").Statut);
        Assert.AreEqual("SSD 256 Go NVMe", Bloc(ssd, "stockage").Valeur);
    }

    [TestMethod]
    public void T07_SmartIllisible_OuDisqueSuspect()
    {
        var illisible = Evaluer(Maquette with { Disques = new[] { new DisqueAudit("SSD", TypeDisque.Ssd, "NVMe", 512_000_000_000, false, new EtatDisque("SSD", false, Array.Empty<string>(), Array.Empty<string>(), "droits administrateur nécessaires")) } });
        Assert.AreEqual(Statut.AVerifier, Bloc(illisible, "stockage").Statut);
        StringAssert.Contains(Bloc(illisible, "stockage").Points[0].Texte, "santé non vérifiée");

        var sansSante = Evaluer(Maquette with { Disques = new[] { new DisqueAudit("SSD", TypeDisque.Ssd, null, 512_000_000_000, false, null) } });
        Assert.AreEqual(Statut.AVerifier, Bloc(sansSante, "stockage").Statut);

        var critique = Evaluer(Maquette with { Disques = new[] { new DisqueAudit("SSD", TypeDisque.Ssd, "SATA", 512_000_000_000, false, new EtatDisque("SSD", true, new[] { "1 200 secteurs réalloués" }, Array.Empty<string>())) } });
        Assert.AreEqual(Urgence.Critique, critique.Prioritaires.First().Urgence);
        Assert.IsTrue(critique.Recommandations.First().Titre == "Disque à remplacer");

        var usb = Evaluer(Maquette with { Disques = new[] { new DisqueAudit("Clé", TypeDisque.Inconnu, "USB", 32_000_000_000, true, null) } });
        Assert.AreEqual("Disques non lus", Bloc(usb, "stockage").Valeur);
    }

    [TestMethod]
    public void T08_Batterie_UseeOuIndeterminee()
    {
        var r = Evaluer(Maquette);
        var b = Bloc(r, "batterie");
        Assert.AreEqual("63 % de la capacité d'origine", b.Valeur);
        Assert.AreEqual(Statut.Anomalie, b.Statut);
        StringAssert.Contains(b.Detail, "312");

        var inconnue = Evaluer(Maquette with { Batterie = new MesureBatterie(true, 1, null, null, null) });
        Assert.AreEqual(Statut.AVerifier, Bloc(inconnue, "batterie").Statut);
        Assert.AreEqual("Capacité non disponible", Bloc(inconnue, "batterie").Valeur);

        var fixe = Evaluer(Maquette with { Portable = false, Batterie = new MesureBatterie(false, 0, null, null, null) });
        Assert.AreEqual(Statut.NonApplicable, Bloc(fixe, "batterie").Statut);
    }

    [TestMethod]
    public void T09_Memoire_PasDEmplacementInvente()
    {
        var inconnu = Bloc(Evaluer(Maquette with { EmplacementsAnnonces = null }), "memoire");
        StringAssert.Contains(inconnu.Detail, "emplacements libres non déterminés");
        var annonce = Bloc(Evaluer(Maquette), "memoire");
        StringAssert.Contains(annonce.Detail, "1 emplacement(s) libre(s) selon le BIOS, à confirmer");
        var absurde = Bloc(Evaluer(Maquette with { Barrettes = 2, EmplacementsAnnonces = 1 }), "memoire");
        StringAssert.Contains(absurde.Detail, "non déterminés");
        Assert.AreEqual(Statut.Conforme, annonce.Statut);
        Assert.IsFalse(Evaluer(Maquette).Recommandations.Any(x => x.Titre.Contains("mémoire")), "pas de recommandation systématique à 16 Go");

        var quatre = Evaluer(Maquette with { MemoireOctets = 4 * Go });
        Assert.AreEqual(Statut.Anomalie, Bloc(quatre, "memoire").Statut);
        Assert.IsTrue(quatre.Recommandations.Any(x => x.Titre.Contains("mémoire")));
    }

    [TestMethod]
    public void T10_SansInternet_AuditUtilisable()
    {
        var r = Evaluer(Maquette with { Reseau = new[] { new CarteReseau("Wi-Fi", "Wi-Fi", false, null, true, Array.Empty<string>(), null) } });
        Assert.AreEqual("Aucune connexion", Bloc(r, "reseau").Valeur);
        Assert.IsTrue(r.Limites.Any(l => l.Contains("Accès Internet non testé")));
        var apipa = Evaluer(Maquette with { Reseau = new[] { new CarteReseau("Ethernet", "Ethernet", true, 1_000_000_000, true, new[] { "169.254.10.2" }, null) } });
        Assert.AreEqual(Statut.Anomalie, Bloc(apipa, "reseau").Statut);
    }

    [TestMethod]
    public void T11_CompteStandard_ControlesRestreintsSignales()
    {
        var r = Evaluer(Maquette with { NonLus = new[] { "Comptes administrateurs : liste non lue (droits insuffisants)." } });
        Assert.IsTrue(r.Limites[0].Contains("sans droits administrateur"));
        Assert.IsTrue(r.Limites.Any(l => l.Contains("Comptes administrateurs")));
        Assert.IsTrue(Bloc(r, "securite").Points.Any(p => p.Statut == Statut.AVerifier && p.Texte.StartsWith("Administrateurs : non lus")));
    }

    [TestMethod]
    public void Synthese_MaquetteEtPriorites()
    {
        var r = Evaluer(Maquette);
        var titres = r.Prioritaires.Select(a => a.Titre).ToList();
        CollectionAssert.Contains(titres, "Windows 10 Professionnel hors support");
        CollectionAssert.Contains(titres, "Disque dur mécanique 500 Go");
        CollectionAssert.Contains(titres, "Portable non chiffré");
        Assert.IsTrue(r.Prioritaires.Count <= 5);
        for (var i = 1; i < r.Prioritaires.Count; i++) Assert.IsTrue(r.Prioritaires[i - 1].Urgence <= r.Prioritaires[i].Urgence);
        StringAssert.StartsWith(r.Synthese, "Synthèse :");
    }

    [TestMethod]
    public void Synthese_ToutesLesCritiquesRestentAffichees()
    {
        var r = new ResultatAudit(Array.Empty<BlocAudit>(),
            Enumerable.Range(1, 7).Select(i => new AlerteAudit(Urgence.Critique, "C" + i, "", "x"))
                .Concat(new[] { new AlerteAudit(Urgence.Important, "I", "", "x") }).ToList(),
            Array.Empty<RecommandationAudit>(), null, Array.Empty<string>());
        Assert.AreEqual(7, r.Prioritaires.Count);
        Assert.IsTrue(r.Prioritaires.All(a => a.Urgence == Urgence.Critique));
    }

    [TestMethod]
    public void Recommandations_ConstatSolutionConditions_SansPrix()
    {
        var d = Maquette with { Antivirus = Array.Empty<AntivirusAudit>(), MemoireOctets = 4 * Go, Sauvegardes = Array.Empty<string>(), Active = false };
        foreach (var x in Evaluer(d).Recommandations)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(x.Constat), x.Titre);
            Assert.IsFalse(string.IsNullOrWhiteSpace(x.Solution), x.Titre);
            Assert.IsFalse(string.IsNullOrWhiteSpace(x.Conditions), x.Titre);
            Assert.IsFalse((x.Solution + x.Conditions).Contains("€"), x.Titre);
        }
    }

    [TestMethod]
    public void Sauvegarde_JamaisConforme()
    {
        Assert.AreEqual(Statut.AVerifier, Bloc(Evaluer(Maquette), "sauvegarde").Statut);
        var aucune = Evaluer(Maquette with { Sauvegardes = Array.Empty<string>() });
        Assert.AreEqual(Statut.Anomalie, Bloc(aucune, "sauvegarde").Statut);
        Assert.IsTrue(aucune.Alertes.Any(a => a.Titre == "Aucune sauvegarde détectée" && a.Urgence == Urgence.Important));
    }

    [TestMethod]
    public void Windows11_InstalleSurMaterielNonPrisEnCharge()
    {
        const string ancien = "Intel(R) Core(TM) i7-7700 CPU @ 3.60GHz";
        var d = Maquette with { Build = 26200, EditionId = "Core", Processeur = ancien, Windows11 = Maquette.Windows11! with { Processeur = ancien, BuildWindows = 26200 } };
        var r = Evaluer(d);
        StringAssert.Contains(Bloc(r, "windows11").Valeur, "Windows 11 installé");
        Assert.IsTrue(r.Alertes.Any(a => a.Titre == "Windows 11 sur un matériel non pris en charge"));
        Assert.AreEqual(Statut.Conforme, Bloc(r, "windows").Points[0].Statut, "25H2 Famille supporté jusqu'au 12/10/2027");
    }

    [DataTestMethod]
    [DataRow(19045, "Professional", "2025-10-14")]
    [DataRow(26100, "Core", "2026-10-13")]
    [DataRow(26100, "Professional", "2026-10-13")]
    [DataRow(26100, "Enterprise", "2027-10-12")]
    [DataRow(26200, "Education", "2028-10-10")]
    [DataRow(22631, "Enterprise", "2026-11-10")]
    [DataRow(19044, "EnterpriseS", "2027-01-12")]
    [DataRow(19044, "IoTEnterpriseS", "2032-01-13")]
    [DataRow(26100, "EnterpriseS", "2029-10-09")]
    public void SupportWindows_SelonEdition(int build, string edition, string fin)
    {
        Assert.AreEqual(DateTime.Parse(fin, System.Globalization.CultureInfo.InvariantCulture), SupportWindows.Trouver(build, edition)!.Fin);
    }

    [TestMethod]
    public void SupportWindows_InconnuJamaisDevine()
    {
        Assert.IsNull(SupportWindows.Trouver(null, "Professional"));
        Assert.IsNull(SupportWindows.Trouver(28000, "Professional")!.Fin);
        Assert.IsNull(SupportWindows.Trouver(20348, "EnterpriseS")!.Fin);
        StringAssert.Contains(SupportWindows.Trouver(19045, "Core")!.Remarque, "ESU");
    }

    [TestMethod]
    public void Windows11_24H2Famille_HorsSupport_MiseAJourDeVersion()
    {
        var d = Maquette with { Build = 26100, EditionId = "Core", Windows11 = Maquette.Windows11! with { BuildWindows = 26100, SecureBoot = true } };
        var r = Evaluer(d);
        Assert.AreEqual(Statut.Anomalie, Bloc(r, "windows").Statut);
        Assert.IsTrue(r.Recommandations.Any(x => x.Titre == "Mise à jour de version de Windows 11"));
    }

    [DataTestMethod]
    [DataRow(Cpu8, "8e génération")]
    [DataRow("Intel(R) Core(TM) i7-10510U CPU @ 1.80GHz", "10e génération")]
    [DataRow("Intel(R) Core(TM) i5-1035G1 CPU @ 1.00GHz", "10e génération")]
    [DataRow("11th Gen Intel(R) Core(TM) i5-1135G7 @ 2.40GHz", "11e génération")]
    [DataRow("12th Gen Intel(R) Core(TM) i7-1260P", "12e génération")]
    [DataRow("Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz", "4e génération")]
    [DataRow("AMD Ryzen 5 3500U with Radeon Vega Mobile Gfx", (string?)null)]
    [DataRow("Intel(R) Celeron(R) N4020 CPU @ 1.10GHz", (string?)null)]
    public void Generation_SeulementSiFiable(string cpu, string? attendu)
    {
        Assert.AreEqual(attendu, RegleAudit.Generation(cpu));
    }

    [DataTestMethod]
    [DataRow(0x061100UL, true, true)]
    [DataRow(0x061110UL, true, false)]
    [DataRow(0x060100UL, false, true)]
    [DataRow(0x041000UL, true, true)]
    public void CentreDeSecurite_EtatProduit(ulong etat, bool actif, bool aJour)
    {
        Assert.AreEqual((actif, aJour), LectureAudit.EtatProduit(etat));
    }
}

/// <summary>Rapport d'audit : synthèse client, annexe, anonymisation, aucune ressource externe.</summary>
[TestClass]
public class V2RapportAuditTests
{
    private static DonneesAudit Donnees => new()
    {
        Fabricant = "HP", Modele = "ProBook 450 G7", NumeroSerie = "5CD1234XYZ", NomPc = "PC-ACCUEIL", NomDomaine = "cabinet.local", Domaine = true,
        Administrateurs = new[] { "PC-ACCUEIL\\marie.dupont" },
        Reseau = new[] { new CarteReseau("Ethernet <Intel>", "Ethernet", true, 1_000_000_000, true, new[] { "10.0.0.15" }, "10.0.0.1") },
        Build = 19045, EditionId = "Professional", Date = new DateTime(2026, 11, 13, 14, 5, 0),
    };

    private static string Html(OptionsRapportAudit o)
    {
        var d = Donnees;
        return RapportAudit.Html(d, RegleAudit.Evaluer(d, BaseWindows11.Integree), o, "v2.0.120");
    }

    [TestMethod]
    public void Autonome_EtEchappe()
    {
        var h = Html(new OptionsRapportAudit());
        Assert.IsFalse(h.Contains("<script"));
        Assert.IsFalse(h.Contains("http://") || h.Contains("https://") || h.Contains("src="));
        Assert.IsFalse(h.Contains("<Intel>"));
        StringAssert.Contains(h, "&lt;Intel&gt;");
        StringAssert.Contains(h, "Devis : à établir séparément");
        StringAssert.Contains(h, "Limites de ce diagnostic");
        StringAssert.Contains(h, "Annexe technicien");
    }

    [TestMethod]
    public void SansAnnexe()
    {
        var h = Html(new OptionsRapportAudit(Annexe: false));
        Assert.IsFalse(h.Contains("Annexe technicien"));
        Assert.IsFalse(h.Contains("marie.dupont"));
    }

    [TestMethod]
    public void Anonymise_SerieNomDomaineComptesIp()
    {
        var h = Html(new OptionsRapportAudit(Annexe: true, Anonymiser: true));
        foreach (var secret in new[] { "5CD1234XYZ", "PC-ACCUEIL", "cabinet.local", "marie.dupont", "10.0.0.15" })
            Assert.IsFalse(h.Contains(secret), secret);
        StringAssert.Contains(h, RapportAudit.Masque);
        StringAssert.Contains(h, "ProBook 450 G7");

        var clair = Html(new OptionsRapportAudit(Annexe: true));
        StringAssert.Contains(clair, "5CD1234XYZ");
    }

    [TestMethod]
    public void Anonymiser_NeMasquePasLesMotsCourants()
    {
        var d = Donnees with { NomPc = "BUREAU", Administrateurs = new[] { "BUREAU\\Administrateur" } };
        Assert.AreEqual("Bureau à distance ouvert", RapportAudit.Anonymiser("Bureau à distance ouvert", d));
        Assert.AreEqual("droits administrateur", RapportAudit.Anonymiser("droits administrateur", d));
    }
}
