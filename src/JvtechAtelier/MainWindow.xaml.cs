using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Jvtech.Atelier.Dossier;
using Jvtech.Lecture;
using Jvtech.Lecture.Collecte;
using Jvtech.Lecture.Regles;
using PcInspector;

namespace JvtechAtelier;

public partial class MainWindow : Window
{
    private static readonly string[] StepNames =
        { "1 · Identification", "2 · Préparation", "3 · Tests", "4 · Fiche", "5 · Annonce", "6 · Finalisation" };
    private static readonly int[] StepsAvailable = { 0, 2, 3, 4 };

    private readonly ModeWindows _mode = Installation.Lire();
    private readonly ReglagesAtelier _reglages = ReglagesAtelier.Lire(AppPaths.ExeFolder);
    private readonly string _racine;
    private List<LigneChamp> _lignes = new();
    private List<LigneTest> _tests = new();
    private List<Controle> _auto = new();
    private MesureBatterie? _batterie;
    private BilanVente? _bilan;
    private DossierPc? _dossier;
    private List<Controle> _controles = new();
    private bool _portable;
    private int _step;

    public MainWindow()
    {
        InitializeComponent();
        _racine = _reglages.Racine(AppPaths.ExeFolder);
        Title = "JVTECH Atelier " + AppInfo.Version;
        VersionText.Text = $"{AppInfo.Version} · {Installation.Libelle(_mode)}";
        OperatorBox.Text = _reglages.Technicien ?? "";
        FinalizeText.Text = Installation.FinalisationPermise(_mode)
            ? "Sysprep pourra être proposé à la fin (Windows en mode audit), après confirmation à part."
            : "Sysprep ne sera pas proposé : Windows n'est pas en mode audit. La finalisation depuis une session normale est à l'étude.";
        ShowStep(0);
        ShowSaleStatus();
    }

    // ---------- Étapes ----------

    private void ShowStep(int step)
    {
        _step = step;
        var accent = (Brush)FindResource("AccentBrush");
        var border = (Brush)FindResource("BorderBrush");
        Steps.ItemsSource = StepNames.Select((n, i) => new
        {
            Index = i,
            Title = n.ToUpperInvariant(),
            State = i == step ? "● Affichée" : StepsAvailable.Contains(i) ? "○ Ouvrir" : "À venir",
            Enabled = StepsAvailable.Contains(i),
            Border = i == step ? accent : border,
            Thickness = new Thickness(i == step ? 2 : 1),
        }).ToList();
        IdentPanel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        TestsPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        ListingPanel.Visibility = step is 3 or 4 ? Visibility.Visible : Visibility.Collapsed;
        if (step is 3 or 4 && ListingText.Text.Length == 0) Compose();
        LaterPanel.Visibility = StepsAvailable.Contains(step) ? Visibility.Collapsed : Visibility.Visible;
        LaterTitle.Text = StepNames[step];
    }

    private void Step_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int step }) ShowStep(step);
    }

    // ---------- Identification ----------

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await ReadPcAsync();

    private async void Reload_Click(object sender, RoutedEventArgs e) => await ReadPcAsync();

    private async Task ReadPcAsync()
    {
        ReloadButton.IsEnabled = SaveButton.IsEnabled = false;
        StatusText.Text = "Lecture du PC…";
        try
        {
            var champs = await Task.Run(() => Inventaire.Lire());
            _portable = await Task.Run(() => LectureControles.EstPortable());
            var serie = champs.FirstOrDefault(c => c.Id == "serie")?.Detecte;
            _dossier = await Task.Run(() => DossierPc.Trouver(_racine, serie));
            Dictionary<string, ResultatTestManuel> resultats = new();
            if (_dossier is not null)
            {
                var (inventaire, corrections) = _dossier.LireInventaire();
                DossierPc.AppliquerCorrections(champs, corrections);
                if (string.IsNullOrWhiteSpace(InventoryBox.Text)) InventoryBox.Text = inventaire ?? "";
                resultats = _dossier.LireTests();
            }
            _lignes = champs.Select(c => new LigneChamp(c)).ToList();
            FieldsGrid.ItemsSource = _lignes;
            _tests = TestsManuels.Applicables(_portable)
                .Select(t => new LigneTest(t, resultats.TryGetValue(t.Id, out var r) ? r : null)).ToList();
            ManualTests.ItemsSource = _tests;
            ShowHeader();
            ShowSaleStatus();
            StatusText.Text = _dossier is null
                ? "Nouveau PC : le dossier sera créé à l'enregistrement."
                : $"Dossier existant repris : {_dossier.Chemin}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Lecture impossible : " + ex.Message;
        }
        finally
        {
            ReloadButton.IsEnabled = SaveButton.IsEnabled = true;
        }
    }

    private void ShowHeader()
    {
        string Valeur(string id) => _lignes.FirstOrDefault(l => l.Champ.Id == id)?.Champ.Valeur ?? "";
        var inventaire = IdentifiantAtelier.NormaliserInventaire(InventoryBox.Text);
        var modele = $"{Valeur("fabricant")} {Valeur("modele")}".Trim();
        PcTitle.Text = string.Join(" · ", new[] { inventaire, modele.Length > 0 ? modele : Champ.NonDetecte }.Where(t => !string.IsNullOrEmpty(t)));
        FolderText.Text = (_dossier is null ? $"Dossiers : {_racine}" : $"Dossier : {_dossier.Chemin}") + (_portable ? " · portable" : " · PC fixe");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        FieldsGrid.CommitEdit();
        FieldsGrid.CommitEdit();
        if (!IdentifiantAtelier.InventaireValide(InventoryBox.Text))
        {
            StatusText.Text = "Numéro d'inventaire non reconnu : il doit commencer par V suivi de chiffres (ex. V1042).";
            return;
        }
        var technicien = OperatorBox.Text.Trim();
        var corrige = _lignes.Any(l => Champ.Nettoyer(l.Retenu) != l.Champ.Valeur);
        if (corrige && technicien.Length == 0)
        {
            StatusText.Text = "Indiquer le nom du technicien : il est noté à côté de chaque correction.";
            return;
        }

        try
        {
            var maintenant = DateTime.Now;
            var changements = new List<string>();
            foreach (var l in _lignes)
            {
                var avant = l.Champ.Valeur;
                var saisi = Champ.Nettoyer(l.Retenu);
                if (saisi is null || saisi == l.Champ.Detecte) l.Champ.AnnulerCorrection();
                else if (saisi != l.Champ.Corrige) l.Champ.Corriger(saisi, technicien, maintenant);
                if (avant != l.Champ.Valeur)
                    changements.Add($"Correction {l.Champ.Id} : « {avant ?? Champ.NonDetecte} » → « {l.Champ.Affichage} » (détecté : « {l.Champ.Detecte ?? Champ.NonDetecte} », {technicien})");
            }

            EnsureFolder(maintenant);
            foreach (var ligne in changements) _dossier!.Journaliser(ligne, maintenant);
            var inventaire = IdentifiantAtelier.NormaliserInventaire(InventoryBox.Text);
            _dossier!.EnregistrerInventaire(inventaire, _lignes.Select(l => l.Champ));
            _lignes.ForEach(l => l.Retenu = l.Champ.Valeur ?? "");
            InventoryBox.Text = inventaire ?? "";
            ShowHeader();
            StatusText.Text = $"Dossier enregistré : {_dossier.Chemin}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusText.Text = "Enregistrement impossible : " + ex.Message;
        }
    }

    /// <summary>Crée le dossier du PC s'il n'existe pas, et le renomme en V… si le numéro vient d'être saisi.</summary>
    private void EnsureFolder(DateTime maintenant)
    {
        var inventaire = IdentifiantAtelier.NormaliserInventaire(InventoryBox.Text);
        var serie = _lignes.FirstOrDefault(l => l.Champ.Id == "serie")?.Champ.Detecte;
        if (_dossier is null)
        {
            _dossier = DossierPc.OuvrirOuCreer(_racine, IdentifiantAtelier.Calculer(inventaire, serie, maintenant));
            _dossier.Journaliser($"Dossier créé ({Installation.Libelle(_mode)}, {AppInfo.Product} {AppInfo.Version})", maintenant);
            _dossier.EnregistrerInventaire(inventaire, _lignes.Select(l => l.Champ));
        }
        else if (inventaire is not null && !string.Equals(_dossier.Identifiant, inventaire, StringComparison.OrdinalIgnoreCase))
        {
            _dossier.Renommer(inventaire);
        }
    }

    // ---------- Tests ----------

    private async void RunChecks_Click(object sender, RoutedEventArgs e)
    {
        RunChecksButton.IsEnabled = false;
        TestsStatus.Text = "Contrôles en cours (disques, mémoire, batterie)…";
        try
        {
            var seuils = _reglages.Seuils;
            var portable = _portable;
            _auto = await Task.Run(() =>
            {
                var memoire = LectureControles.DernierDiagnosticMemoire();
                return new List<Controle>
                {
                    RegleControles.Disques(LectureControles.Disques()),
                    RegleControles.MemoireRapide(LectureControles.NombreBarrettes(), memoire, LectureControles.ErreursMemoireMaterielles()),
                    RegleControles.MemoireApprofondi(memoire),
                    RegleControles.Batterie(_batterie = LectureControles.Batterie(portable), seuils),
                };
            });
            AutoChecks.ItemsSource = _auto.Select(c => new LigneControle(c)).ToList();
            if (_dossier is not null)
                foreach (var c in _auto) _dossier.Journaliser($"Contrôle {c.Id} : {Libelles.De(c.Statut)} : {c.Constat}");
            TestsStatus.Text = $"Contrôles faits à {DateTime.Now:HH:mm}." + (_dossier is null ? " Enregistrer l'identification pour les garder dans le dossier." : "");
            ShowSaleStatus();
        }
        catch (Exception ex)
        {
            TestsStatus.Text = "Contrôles impossibles : " + ex.Message;
        }
        finally
        {
            RunChecksButton.IsEnabled = true;
        }
    }

    private void OpenPeripheralTest_Click(object sender, RoutedEventArgs e) =>
        new PeripheralTestWindow { Owner = this }.Show();

    private void SaveTests_Click(object sender, RoutedEventArgs e)
    {
        var technicien = OperatorBox.Text.Trim();
        if (technicien.Length == 0)
        {
            TestsStatus.Text = "Indiquer le nom du technicien (étape Identification) : il est noté avec chaque test.";
            return;
        }
        try
        {
            var maintenant = DateTime.Now;
            EnsureFolder(maintenant);
            foreach (var t in _tests)
            {
                var remarque = string.IsNullOrWhiteSpace(t.Remarque) ? null : t.Remarque.Trim();
                var avant = t.Enregistre;
                var inchange = avant is not null && avant.Resultat == t.Resultat && avant.PorteeEchec == t.PorteeEchec && avant.Remarque == remarque;
                if (inchange) continue;
                t.Enregistre = new ResultatTestManuel(t.Test.Id, t.Resultat, t.PorteeEchec, remarque, technicien, maintenant);
                _dossier!.Journaliser($"Test {t.Test.Id} : {t.Resultat}" + (t.Resultat == ResultatTest.Echec ? $" ({t.PorteeEchec})" : "") + (remarque is null ? "" : $" : {remarque}") + $" ({technicien})", maintenant);
            }
            _dossier!.EnregistrerTests(_tests.Where(t => t.Enregistre is not null).Select(t => t.Enregistre!));
            ShowHeader();
            ShowSaleStatus();
            TestsStatus.Text = $"Tests enregistrés dans {_dossier.Chemin}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            TestsStatus.Text = "Enregistrement impossible : " + ex.Message;
        }
    }

    // ---------- Statut avant vente ----------

    private void ShowSaleStatus()
    {
        var controles = new List<Controle>(_auto);
        if (_auto.Count == 0)
            controles.AddRange(new[]
            {
                Controle.NonRealise(RegleControles.IdDisque, "Santé des disques"),
                Controle.NonRealise(RegleControles.IdMemoire, "Mémoire (contrôle rapide)"),
                Controle.NonRealise(RegleControles.IdBatterie, "Batterie"),
            });
        controles.AddRange(_tests.Select(t => TestsManuels.EnControle(t.Test, t.Enregistre)));
        var bilan = _bilan = RegleVente.Evaluer(controles, TestsManuels.Obligatoires(_portable));
        _controles = controles;
        SaleStatus.Text = bilan.Libelle;
        SaleCard.BorderBrush = (Brush)FindResource(bilan.Statut switch
        {
            StatutVente.Pret => "OkBrush",
            StatutVente.Bloque => "DangerBrush",
            _ => "WarningBrush",
        });
        var lignes = bilan.Bloquants.Select(b => $"✕ {b.Titre} : {b.Constat}")
            .Concat(bilan.Manquants.Select(m => $"○ {m} : non fait ou à vérifier"))
            .Concat(bilan.Reserves.Select(r => $"? Réserve, {r.Titre} : {r.Constat}"));
        SaleDetail.Text = string.Join(Environment.NewLine, lignes);
    }

    // ---------- Fiche et annonce ----------

    private DonneesFiche Donnees() => new(
        IdentifiantAtelier.NormaliserInventaire(InventoryBox.Text),
        _lignes.Select(l => l.Champ).ToList(),
        _controles,
        _bilan ?? RegleVente.Evaluer(_controles, TestsManuels.Obligatoires(_portable)),
        _batterie,
        ConditionBox.Text,
        NotesBox.Text);

    private void Compose()
    {
        ShowSaleStatus();
        var texte = Annonce.Composer(Donnees());
        ListingTitle.Text = texte.Titre;
        ListingText.Text = texte.Description;
        ListingWarnings.Text = string.Join(Environment.NewLine, texte.Avertissements.Select(a => "! " + a));
        ListingStatus.Text = "Relire et corriger le texte si besoin avant de le publier.";
    }

    private void Compose_Click(object sender, RoutedEventArgs e) => Compose();

    private void ListingTitle_Changed(object sender, TextChangedEventArgs e) =>
        TitleLength.Text = $"{ListingTitle.Text.Length} / {Annonce.LongueurTitreMax}";

    private void CopyTitle_Click(object sender, RoutedEventArgs e) => Copy(ListingTitle.Text, "Titre copié.");

    private void CopyDescription_Click(object sender, RoutedEventArgs e) => Copy(ListingText.Text, "Descriptif copié.");

    private void Copy(string text, string done)
    {
        try
        {
            Clipboard.SetText(text);
            ListingStatus.Text = done;
        }
        catch (COMException)
        {
            ListingStatus.Text = "Le presse-papiers est occupé : réessayer.";
        }
    }

    private void SaveListing_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var maintenant = DateTime.Now;
            EnsureFolder(maintenant);
            var fiche = Annonce.FicheInterne(Donnees(), Installation.Libelle(_mode), maintenant);
            _dossier!.EnregistrerTexte("fiche-interne.txt", fiche);
            // L'annonce enregistrée est celle affichée, avec les retouches du technicien.
            _dossier.EnregistrerTexte("annonce.txt", ListingTitle.Text.Trim() + Environment.NewLine + Environment.NewLine + ListingText.Text.Trim() + Environment.NewLine);
            _dossier.Journaliser($"Fiche interne et annonce enregistrées (statut : {_bilan?.Libelle ?? "?"})", maintenant);
            ShowHeader();
            ListingStatus.Text = "Enregistrées dans " + _dossier.Chemin;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ListingStatus.Text = "Enregistrement impossible : " + ex.Message;
        }
    }
}
