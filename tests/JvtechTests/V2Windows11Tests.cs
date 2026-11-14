using System;
using System.IO;
using System.Linq;
using Jvtech.Lecture.Collecte;
using Jvtech.Lecture.Regles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JvtechTests;

/// <summary>
/// Ajustement 4 : base Windows 11 officielle, datée, actualisable ; trois cas distincts (incompatibilité matérielle,
/// réglage BIOS/UEFI, impossibilité de conclure). Les noms de processeurs sont ceux affichés par Windows.
/// </summary>
[TestClass]
public class Windows11Tests
{
    private static readonly BaseWindows11 B = BaseWindows11.Integree;

    private static MesuresWindows11 PcConforme(string cpu = "Intel(R) Core(TM) i5-8350U CPU @ 1.70GHz") => new()
    {
        Processeur = cpu, Coeurs = 4, FrequenceMhz = 1900, Processeur64Bits = true,
        MemoireOctets = 8UL << 30, DisqueSystemeOctets = 256_000_000_000UL, Uefi = true, SecureBoot = true,
        TpmLu = true, TpmVersion = "2.0", BuildWindows = 19045,
    };

    [TestMethod]
    public void BaseIntegree_DateeEtIdentiqueAuFichierTexte()
    {
        Assert.AreEqual(new DateTime(2025, 10, 16), B.Date);
        Assert.AreEqual(new DateTime(2026, 11, 13), B.Releve);
        Assert.IsTrue(B.Regles.Count > 300);
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PcInspector.sln"))) dir = dir.Parent;
        var texte = File.ReadAllText(Path.Combine(dir!.FullName, "src", "JvtechLecture", "Donnees", BaseWindows11.NomFichier));
        Assert.AreEqual(texte.Replace("\r\n", "\n"), Jvtech.Lecture.Donnees.BaseWindows11Donnees.Texte.Replace("\r\n", "\n"),
            "BaseWindows11Donnees.cs doit reprendre exactement win11-processeurs.txt");
    }

    [DataTestMethod]
    [DataRow("Intel(R) Core(TM) i5-8350U CPU @ 1.70GHz", "8th Generation Core i5 Processors")]
    [DataRow("Intel(R) Core(TM) i7-10700 CPU @ 2.90GHz", "10th Generation Core i7 Processors")]
    [DataRow("Intel(R) Core(TM) i5-1035G1 CPU @ 1.00GHz", "10th Generation Core i5 Processors")]
    [DataRow("11th Gen Intel(R) Core(TM) i5-1135G7 @ 2.40GHz", "11th Generation Core i5 Processors")]
    [DataRow("12th Gen Intel(R) Core(TM) i7-1260P", "12th Generation Core i7 Processors")]
    [DataRow("Intel(R) Core(TM) Ultra 7 155H", "Core Ultra Processors (Series 1)")]
    [DataRow("Intel(R) Celeron(R) N4020 CPU @ 1.10GHz", "Celeron N4000 Series")]
    [DataRow("Intel(R) N100", "N100 Series")]
    [DataRow("AMD Ryzen 5 3500U with Radeon Vega Mobile Gfx", "Ryzen 5 3500U")]
    [DataRow("AMD Ryzen 7 PRO 4750U with Radeon Graphics", "Ryzen 7 PRO 4750U")]
    [DataRow("AMD Ryzen 5 2600 Six-Core Processor", "Ryzen 5 2600")]
    [DataRow("AMD Ryzen 5 9600X 6-Core Processor", "Ryzen 9000 Series")]
    [DataRow("AMD Ryzen AI 9 HX 370 w/ Radeon 890M", "Ryzen AI 300 Series")]
    [DataRow("Snapdragon(R) X Elite - X1E78100 - Qualcomm(R) Oryon(TM) CPU", "Snapdragon X (X1, X1E, X1P)")]
    public void Processeurs_DansLaListeOfficielle(string cpu, string libelle)
    {
        Assert.AreEqual(libelle, B.Trouver(cpu)?.Libelle);
    }

    [DataTestMethod]
    [DataRow("Intel(R) Core(TM) i7-7700 CPU @ 3.60GHz")]
    [DataRow("Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz")]
    [DataRow("Intel(R) Core(TM) i5 CPU 750 @ 2.67GHz")]
    [DataRow("Intel(R) Core(TM)2 Duo CPU E8400 @ 3.00GHz")]
    [DataRow("AMD Ryzen 5 1600 Six-Core Processor")]
    [DataRow("AMD FX-8350 Eight-Core Processor")]
    [DataRow("Intel(R) Celeron(R) CPU N3060 @ 1.60GHz")]
    public void ProcesseurAncien_Incompatible(string cpu)
    {
        Assert.IsNull(B.Trouver(cpu));
        var bilan = CompatibiliteWindows11.Evaluer(PcConforme(cpu), B);
        Assert.AreEqual(VerdictWindows11.Incompatible, bilan.Verdict);
    }

    [TestMethod]
    public void ProcesseurAbsentMaisPasAncien_ImpossibleDeConclure()
    {
        // Le nom Windows d'un Pentium Gold ne contient pas « Gold » : on ne devine pas.
        var bilan = CompatibiliteWindows11.Evaluer(PcConforme("Intel(R) Pentium(R) CPU 4415U @ 2.30GHz"), B);
        Assert.AreEqual(VerdictWindows11.Indetermine, bilan.Verdict);
        StringAssert.Contains(bilan.Points.First().Detail, "16/10/2025");
    }

    [TestMethod]
    public void ToutConforme_Compatible()
    {
        Assert.AreEqual(VerdictWindows11.Compatible, CompatibiliteWindows11.Evaluer(PcConforme(), B).Verdict);
    }

    [TestMethod]
    public void SecureBootDesactive_OuTpmCache_ReglageUefi()
    {
        Assert.AreEqual(VerdictWindows11.ReglageUefi, CompatibiliteWindows11.Evaluer(PcConforme() with { SecureBoot = false }, B).Verdict);
        Assert.AreEqual(VerdictWindows11.ReglageUefi, CompatibiliteWindows11.Evaluer(PcConforme() with { TpmVersion = null }, B).Verdict);
        var legacy = CompatibiliteWindows11.Evaluer(PcConforme() with { Uefi = false, SecureBoot = null }, B);
        Assert.AreEqual(VerdictWindows11.ReglageUefi, legacy.Verdict);
        StringAssert.Contains(legacy.Points.Single(p => p.Nom == "Démarrage UEFI").Detail, "MBR2GPT");
    }

    [TestMethod]
    public void Materiel_Incompatible()
    {
        Assert.AreEqual(VerdictWindows11.Incompatible, CompatibiliteWindows11.Evaluer(PcConforme() with { TpmVersion = "1.2" }, B).Verdict);
        Assert.AreEqual(VerdictWindows11.Incompatible, CompatibiliteWindows11.Evaluer(PcConforme() with { MemoireOctets = 2UL << 30 }, B).Verdict);
        Assert.AreEqual(VerdictWindows11.Incompatible, CompatibiliteWindows11.Evaluer(PcConforme() with { DisqueSystemeOctets = 32_000_000_000UL }, B).Verdict);
        // Incompatible l'emporte sur un réglage UEFI.
        Assert.AreEqual(VerdictWindows11.Incompatible, CompatibiliteWindows11.Evaluer(PcConforme() with { TpmVersion = "1.2", SecureBoot = false }, B).Verdict);
    }

    [TestMethod]
    public void NonLu_ImpossibleDeConclure_JamaisCompatible()
    {
        Assert.AreEqual(VerdictWindows11.Indetermine, CompatibiliteWindows11.Evaluer(PcConforme() with { TpmLu = false }, B).Verdict);
        Assert.AreEqual(VerdictWindows11.Indetermine, CompatibiliteWindows11.Evaluer(PcConforme() with { TpmVersion = "?" }, B).Verdict);
        Assert.AreEqual(VerdictWindows11.Indetermine, CompatibiliteWindows11.Evaluer(PcConforme() with { Uefi = null }, B).Verdict);
        // Un doute l'emporte sur un réglage : on ne promet pas « compatible après réglage ».
        Assert.AreEqual(VerdictWindows11.Indetermine, CompatibiliteWindows11.Evaluer(PcConforme() with { MemoireOctets = null, SecureBoot = false }, B).Verdict);
        Assert.AreEqual(VerdictWindows11.Indetermine, CompatibiliteWindows11.Evaluer(new MesuresWindows11(), B).Verdict);
    }

    [TestMethod]
    public void Actualisation_FichierPlusRecentSeulement()
    {
        var recent = "date=2026-06-01\nIntel | Test | X | \\bi5-7\\d{3}\n";
        var b = BaseWindows11.Choisir(recent, out var avert);
        Assert.IsNull(avert);
        Assert.AreEqual(new DateTime(2026, 6, 1), b.Date);
        StringAssert.Contains(b.Libelle, BaseWindows11.NomFichier);

        Assert.AreSame(BaseWindows11.Integree, BaseWindows11.Choisir("date=2024-01-01\nIntel | T | X | i5", out avert));
        StringAssert.Contains(avert, "pas plus récente");
        Assert.AreSame(BaseWindows11.Integree, BaseWindows11.Choisir("date=2027-01-01\nIntel | T | X | (", out avert));
        StringAssert.Contains(avert, "motif invalide");
        Assert.AreSame(BaseWindows11.Integree, BaseWindows11.Choisir("Intel | T | X | i5", out avert));
        StringAssert.Contains(avert, "Date");
        Assert.AreSame(BaseWindows11.Integree, BaseWindows11.Choisir(null, out avert));
        Assert.IsNull(avert);
    }

    [TestMethod]
    public void Tpm_DansLaListeDesPeripheriques()
    {
        Assert.AreEqual("2.0", LectureWindows11.VersionTpm(new[] { "Périphérique audio", "Module de plateforme sécurisée 2.0" }));
        Assert.AreEqual("2.0", LectureWindows11.VersionTpm(new[] { "Trusted Platform Module 2.0" }));
        Assert.AreEqual("1.2", LectureWindows11.VersionTpm(new[] { "Trusted Platform Module 1.2" }));
        Assert.AreEqual("?", LectureWindows11.VersionTpm(new[] { "Trusted Platform Module" }));
        Assert.IsNull(LectureWindows11.VersionTpm(new[] { "Lecteur d'empreintes" }));
    }
}
