using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Jvtech.Atelier.Actions;
using Jvtech.Atelier.Actions.Finaliser;
using Jvtech.Atelier.Actions.Preparation;
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
    private static readonly int[] StepsAvailable = { 0, 1, 2, 3, 4, 5 };

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
    private EtatPreparation? _prep;
    private Controle? _pilotes;
    private readonly List<LigneLogiciel> _logiciels = PackAtelier.Logiciels.Select(l => new LigneLogiciel(l)).ToList();
    private readonly List<ActionRecord> _actions = new();
    private CancellationTokenSource? _arret;
    private string? _empreinteComposee;
    private readonly bool _reprendre = Reprise.Demandee(Environment.GetCommandLineArgs());
    private bool _autoEnCours;
    private bool _autoArret;
    private bool _logicielsTentes;

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
        SoftwareList.ItemsSource = _logiciels;
        ShowStep(0);
        ShowSaleStatus();
    }

    // ---------- Étapes ----------

    private void ShowStep(int step)
    {
        _step = step;
        RefreshSteps();
        IdentPanel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        PrepPanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        FinalPanel.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;
        if (step == 5) ShowFinalisation();
        TestsPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        ListingPanel.Visibility = step is 3 or 4 ? Visibility.Visible : Visibility.Collapsed;
        if (step is 3 or 4 && ListingText.Text.Length == 0) Compose();
        LaterPanel.Visibility = StepsAvailable.Contains(step) ? Visibility.Collapsed : Visibility.Visible;
        LaterTitle.Text = StepNames[step];
    }

    private void RefreshSteps()
    {
        var accent = (Brush)FindResource("AccentBrush");
        var border = (Brush)FindResource("BorderBrush");
        Steps.ItemsSource = StepNames.Select((n, i) => new
        {
            Index = i,
            Title = n.ToUpperInvariant(),
            State = (i == _step ? "● " : "") + EtatEtape(i),
            Enabled = StepsAvailable.Contains(i),
            Border = i == _step ? accent : border,
            Thickness = new Thickness(i == _step ? 2 : 1),
        }).ToList();
    }

    /// <summary>Où en est chaque étape, d'après ce qui est réellement enregistré dans le dossier du PC.</summary>
    private string EtatEtape(int i)
    {
        try
        {
            switch (i)
            {
                case 0:
                    return _dossier is null ? "À enregistrer" : IdentifiantAtelier.NormaliserInventaire(_dossier.LireInventaire().Inventaire) is null ? "Sans n° V…" : "✓ Enregistrée";
                case 1:
                    var prep = PlanPreparation.EnControle(_prep);
                    return prep.Statut == Statut.Conforme ? "✓ Terminée" : _prep is null ? "○ À faire" : "? En cours ou à vérifier";
                case 2:
                    var faits = _tests.Count(t => t.Enregistre is { Resultat: not ResultatTest.NonTeste });
                    return $"{faits} sur {_tests.Count} tests" + (_auto.Count == 0 ? ", contrôles à lancer" : "");
                case 3:
                case 4:
                    var empreinte = _dossier?.LireEmpreinteFiche();
                    return empreinte is null ? "○ À faire" : _bilan is not null && empreinte == _bilan.Empreinte ? "✓ Enregistrée" : "? À refaire (statut changé)";
                default:
                    return !Installation.FinalisationPermise(_mode) ? "Pas en mode audit"
                        : _bilan?.Statut is StatutVente.Pret or StatutVente.PretAvecReserve ? "Possible" : "✕ Bloquée";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Dossier illisible";
        }
    }

    private void Step_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int step }) ShowStep(step);
    }

    // ---------- Identification ----------

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await ReadPcAsync();
        // Relancé par Windows après un redémarrage demandé par la préparation : passe suivante, sans autre clic.
        if (_reprendre && _dossier is not null && _prep is { RepriseProgrammee: true })
        {
            var tout = _prep.ToutLancer;
            _prep = _prep with { RepriseProgrammee = false };
            _dossier.EnregistrerPreparation(_prep);
            _dossier.Journaliser("Reprise de la préparation après redémarrage" + (tout ? " (Tout lancer)" : ""));
            if (tout) await ToutLancerAsync();
            else
            {
                ShowStep(1);
                await RunUpdatePassAsync();
            }
        }
    }

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
                _prep = _dossier.LirePreparation();
            }
            _lignes = champs.Select(c => new LigneChamp(c)).ToList();
            FieldsGrid.ItemsSource = _lignes;
            _tests = TestsManuels.Applicables(_portable)
                .Select(t => new LigneTest(t, resultats.TryGetValue(t.Id, out var r) ? r : null)).ToList();
            ManualTests.ItemsSource = _tests;
            ShowHeader();
            ShowPreparation();
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

    private async void RunChecks_Click(object sender, RoutedEventArgs e) => await RunChecksAsync();

    private async Task RunChecksAsync()
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
                    PlanPreparation.Pilotes(LectureControles.PeripheriquesEnErreur()),
                };
            });
            _pilotes = _auto.FirstOrDefault(c => c.Id == PlanPreparation.IdPilotes);
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
            if (_prep?.ToutLancer == true && TestsFaits() && !_autoEnCours) _ = ToutLancerAsync();
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
        // Pilotes relus depuis la préparation : la lecture la plus récente remplace celle des contrôles.
        if (_pilotes is not null)
        {
            controles.RemoveAll(c => c.Id == PlanPreparation.IdPilotes);
            controles.Add(_pilotes);
        }
        controles.Add(PlanPreparation.EnControle(_prep));
        var bilan = _bilan = RegleVente.Evaluer(controles, PlanPreparation.Obligatoires(_portable));
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
        RefreshSteps();
    }

    // ---------- Fiche et annonce ----------

    private DonneesFiche Donnees() => new(
        IdentifiantAtelier.NormaliserInventaire(InventoryBox.Text),
        _lignes.Select(l => l.Champ).ToList(),
        _controles,
        _bilan ?? RegleVente.Evaluer(_controles, PlanPreparation.Obligatoires(_portable)),
        _batterie,
        ConditionBox.Text,
        NotesBox.Text);

    private void Compose()
    {
        ShowSaleStatus();
        var texte = Annonce.Composer(Donnees());
        _empreinteComposee = _bilan?.Empreinte;
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

    private void SaveListing_Click(object sender, RoutedEventArgs e) => SaveListing();

    private bool SaveListing()
    {
        try
        {
            var maintenant = DateTime.Now;
            EnsureFolder(maintenant);
            var fiche = Annonce.FicheInterne(Donnees(), Installation.Libelle(_mode), maintenant);
            _dossier!.EnregistrerTexte("fiche-interne.txt", fiche);
            // L'annonce enregistrée est celle affichée, avec les retouches du technicien.
            _dossier.EnregistrerTexte("annonce.txt", ListingTitle.Text.Trim() + Environment.NewLine + Environment.NewLine + ListingText.Text.Trim() + Environment.NewLine);
            _dossier.Journaliser("Rapport enregistré : rapport.html", maintenant);
            _dossier.EnregistrerTexte("rapport.html", Rapport.Html(Donnees(), Installation.Libelle(_mode), AppInfo.Version, maintenant, _dossier.LireJournal()));
            // Statut et réserves avec lesquels l'annonce affichée a été composée : vérifiés avant Sysprep.
            if (_empreinteComposee is not null) _dossier.EnregistrerEmpreinteFiche(_empreinteComposee, maintenant);
            _dossier.Journaliser($"Fiche interne et annonce enregistrées (composées avec : {_empreinteComposee ?? "?"} ; statut actuel : {_bilan?.Empreinte ?? "?"})", maintenant);
            ShowHeader();
            ListingStatus.Text = "Fiche interne, annonce et rapport (rapport.html, imprimable en PDF) enregistrés dans " + _dossier.Chemin;
            RefreshSteps();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ListingStatus.Text = "Enregistrement impossible : " + ex.Message;
            return false;
        }
    }

    // ---------- Préparation ----------

    private void ShowPreparation()
    {
        var etat = _prep;
        UpdateState.Text = etat is null || etat.Passes == 0
            ? "Pas encore lancées."
            : $"{etat.Passes} passe(s), {etat.MisesAJourInstallees} mise(s) à jour installée(s)"
              + (etat.MisesAJourEchouees > 0 ? $", {etat.MisesAJourEchouees} échec(s)" : "")
              + (etat.DerniereSuite is { } suite ? $". Suite : {PlanPreparation.Libelle(suite)}." : ".")
              + (etat.DernierDetail is { } d ? Environment.NewLine + "Dernière passe : " + d : "");
        RebootButton.Visibility = etat?.DerniereSuite == SuitePasse.Redemarrer ? Visibility.Visible : Visibility.Collapsed;
        UpdateButton.Content = etat?.DerniereSuite is SuitePasse.Terminee ? "Vérifier à nouveau les mises à jour" : "Lancer une passe Windows Update";
        if (_pilotes is not null) DriversState.Text = $"{Libelles.De(_pilotes.Statut)} : {_pilotes.Constat}";
        PrepActions.ItemsSource = _actions.AsEnumerable().Reverse().Select(a => new LigneAction(a)).ToList();
    }

    /// <summary>Lance des actions l'une après l'autre, chacune tracée dans le journal du dossier. Une seule série à la fois.</summary>
    private async Task<List<(IActionAtelier Action, ActionRecord Trace)>> RunActionsAsync(IReadOnlyList<IActionAtelier> actions)
    {
        var faites = new List<(IActionAtelier, ActionRecord)>();
        if (_arret is not null) return faites;
        EnsureFolder(DateTime.Now);
        _arret = new CancellationTokenSource();
        SetPrepButtons(false);
        try
        {
            foreach (var action in actions)
            {
                if (_arret.IsCancellationRequested) break;
                PrepStatus.Text = $"En cours : {action.Libelle}…";
                var jeton = _arret.Token;
                var trace = await Task.Run(() => ActionJournal.Lancer(action, jeton));
                _actions.Add(trace);
                _dossier!.Journaliser("Action " + trace.Ligne());
                faites.Add((action, trace));
                ShowPreparation();
            }
            PrepStatus.Text = _arret.IsCancellationRequested ? "Arrêté à la demande du technicien." : $"Terminé à {DateTime.Now:HH:mm}.";
        }
        finally
        {
            _arret.Dispose();
            _arret = null;
            SetPrepButtons(true);
        }
        return faites;
    }

    private void SetPrepButtons(bool actif)
    {
        UpdateButton.IsEnabled = SoftwareButton.IsEnabled = MemoryButton.IsEnabled = RebootButton.IsEnabled = DriversButton.IsEnabled = actif;
        StopPrepButton.IsEnabled = !actif;
    }

    private void StopPrep_Click(object sender, RoutedEventArgs e)
    {
        _arret?.Cancel();
        _autoArret = true;
        PrepStatus.Text = "Arrêt demandé : l'action en cours se termine d'abord (une mise à jour n'est jamais coupée en plein milieu).";
    }

    private async void RunUpdates_Click(object sender, RoutedEventArgs e) => await RunUpdatePassAsync();

    /// <summary>Passes Windows Update enchaînées tant qu'il n'y a pas besoin de redémarrer, dans la limite prévue.</summary>
    private async Task RunUpdatePassAsync()
    {
        try
        {
            while (true)
            {
                var etat = _prep ?? new EtatPreparation();
                if (etat.RepriseProgrammee) AnnulerReprise();
                // Après « Terminée », une vérification repart à zéro passe pour ne pas buter sur la limite.
                var passe = etat.DerniereSuite == SuitePasse.Terminee ? 1 : etat.Passes + 1;
                var maj = new MisesAJourWindows(passe, t => Dispatcher.BeginInvoke(new Action(() => PrepStatus.Text = $"Windows Update, passe {passe} : {t}")));
                var faites = await RunActionsAsync(new IActionAtelier[] { maj });
                if (faites.Count == 0 || faites[0].Trace.Resultat == ResultatAction.Annule || faites[0].Trace.Resultat == ResultatAction.NonLance) break;

                _prep = etat with
                {
                    Passes = etat.DerniereSuite == SuitePasse.Terminee ? etat.Passes + 1 : passe,
                    MisesAJourInstallees = etat.MisesAJourInstallees + maj.Installees,
                    MisesAJourEchouees = maj.Echecs.Count,
                    DerniereSuite = maj.Suite,
                    DernierDetail = faites[0].Trace.Detail,
                    RepriseProgrammee = false,
                    MisAJourLe = DateTime.Now,
                };
                _dossier!.EnregistrerPreparation(_prep);
                ShowPreparation();
                ShowSaleStatus();
                if (maj.Suite != SuitePasse.NouvellePasse) break;
            }
            if (_prep?.DerniereSuite == SuitePasse.Redemarrer)
                PrepStatus.Text = "Redémarrage nécessaire : cliquer sur « Redémarrer et reprendre » (la clé USB doit rester branchée).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            PrepStatus.Text = "Préparation impossible : " + ex.Message;
        }
    }

    /// <summary>Passe lancée à la main : l'éventuelle relance automatique n'a plus lieu d'être.</summary>
    private static void AnnulerReprise()
    {
        try { Reprise.Annuler(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    private void Reboot_Click(object sender, RoutedEventArgs e)
    {
        if (_dossier is null || _prep is null) return;
        var choix = MessageBox.Show(this,
            "Le PC va redémarrer dans 30 secondes. JVTECH Atelier se relancera à l'ouverture de session et fera la passe suivante.\n\nLa clé USB doit rester branchée. Fermer les autres programmes avant de continuer.",
            "Redémarrer et reprendre", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (choix != MessageBoxResult.OK) return;
        RedemarrerEtReprendre();
    }

    /// <summary>Programme la relance d'Atelier puis redémarre dans 30 s. Faux si Windows refuse.</summary>
    private bool RedemarrerEtReprendre()
    {
        if (_dossier is null || _prep is null) return false;
        try
        {
            var exe = System.Reflection.Assembly.GetEntryAssembly()!.Location;
            Reprise.Programmer(exe);
            _prep = _prep with { RepriseProgrammee = true };
            _dossier.EnregistrerPreparation(_prep);
            _dossier.Journaliser($"Redémarrage demandé par le technicien, reprise programmée ({Reprise.Commande(exe)})");
            if (!Reprise.Redemarrer())
            {
                Reprise.Annuler();
                _prep = _prep with { RepriseProgrammee = false };
                _dossier.EnregistrerPreparation(_prep);
                _dossier.Journaliser("Redémarrage refusé par Windows : reprise annulée");
                PrepStatus.Text = "Windows a refusé le redémarrage : redémarrer à la main, puis relancer JVTECH Atelier.";
                return false;
            }
            PrepStatus.Text = "Redémarrage dans 30 secondes (annulable avec « shutdown /a »).";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            PrepStatus.Text = "Reprise impossible à programmer : " + ex.Message;
            return false;
        }
    }

    private async void InstallSoftware_Click(object sender, RoutedEventArgs e) => await InstallSoftwareAsync();

    private async Task InstallSoftwareAsync()
    {
        var choisis = _logiciels.Where(l => l.Coche).Select(l => l.Logiciel).ToList();
        if (choisis.Count == 0)
        {
            PrepStatus.Text = "Aucun logiciel coché.";
            return;
        }
        try
        {
            var actions = choisis.Select(l => (IActionAtelier)new InstallationLogiciel(l, t => Dispatcher.BeginInvoke(new Action(() => PrepStatus.Text = $"{l.Nom} : {t}")))).ToList();
            var faites = await RunActionsAsync(actions);
            var resultats = new Dictionary<string, ActionRecord>();
            foreach (var (action, trace) in faites) resultats[action.Id] = trace;

            // Pack de base réussi seulement si 7-Zip et VLC sont installés (ou déjà là). Un logiciel du pack non relancé
            // garde le résultat précédent ; une option en échec est notée dans le journal, sans bloquer la vente.
            var etat = _prep ?? new EtatPreparation();
            if (PackAtelier.DeBase.Any(b => resultats.ContainsKey("logiciel:" + b.Id)))
            {
                var manquants = new List<string>();
                foreach (var b in PackAtelier.DeBase)
                {
                    if (resultats.TryGetValue("logiciel:" + b.Id, out var r))
                    {
                        if (r.Resultat != ResultatAction.Reussi) manquants.Add($"{b.Nom} : {r.Detail}");
                    }
                    else if (etat.PackReussi != true) manquants.Add($"{b.Nom} non installé");
                }
                _prep = etat with
                {
                    PackReussi = manquants.Count == 0,
                    PackDetail = manquants.Count == 0 ? "7-Zip et VLC installés" : string.Join(" ; ", manquants),
                    MisAJourLe = DateTime.Now,
                };
                _dossier!.EnregistrerPreparation(_prep);
            }
            ShowPreparation();
            ShowSaleStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            PrepStatus.Text = "Installation impossible : " + ex.Message;
        }
    }

    private async void ReadDrivers_Click(object sender, RoutedEventArgs e)
    {
        DriversButton.IsEnabled = false;
        try
        {
            _pilotes = PlanPreparation.Pilotes(await Task.Run(() => LectureControles.PeripheriquesEnErreur()));
            _dossier?.Journaliser($"Contrôle {_pilotes.Id} : {Libelles.De(_pilotes.Statut)} : {_pilotes.Constat}");
            ShowPreparation();
            ShowSaleStatus();
        }
        finally
        {
            DriversButton.IsEnabled = _arret is null;
        }
    }

    private void OpenDeviceManager_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("devmgmt.msc") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            PrepStatus.Text = "Gestionnaire de périphériques introuvable : " + ex.Message;
        }
    }

    private async void RunMemoryDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RunActionsAsync(new IActionAtelier[] { new DiagnosticMemoireWindows() });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            PrepStatus.Text = "Diagnostic impossible : " + ex.Message;
        }
    }

    private void AcceptPrep_Click(object sender, RoutedEventArgs e)
    {
        var technicien = OperatorBox.Text.Trim();
        var raison = AcceptReason.Text.Trim();
        if (technicien.Length == 0 || raison.Length == 0)
        {
            PrepStatus.Text = "Indiquer le technicien (étape Identification) et une raison pour accepter la préparation en l'état.";
            return;
        }
        if (_prep is null || (_prep.Passes == 0 && _prep.PackReussi is null))
        {
            PrepStatus.Text = "Lancer d'abord les mises à jour et le pack de base : on n'accepte qu'une préparation tentée.";
            return;
        }
        try
        {
            var maintenant = DateTime.Now;
            _prep = _prep with { AccepteePar = technicien, AccepteeLe = maintenant, RaisonAcceptation = raison, MisAJourLe = maintenant };
            _dossier!.EnregistrerPreparation(_prep);
            _dossier.Journaliser($"Préparation acceptée en l'état par {technicien} : {raison}", maintenant);
            ShowPreparation();
            ShowSaleStatus();
            PrepStatus.Text = "Acceptation notée dans le dossier.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PrepStatus.Text = "Enregistrement impossible : " + ex.Message;
        }
    }

    // ---------- Tout lancer ----------

    private bool TestsFaits() => _tests.Count > 0 && _tests.All(t => t.Enregistre is { Resultat: not ResultatTest.NonTeste });

    private bool FicheAJour()
    {
        ShowSaleStatus();
        try { return _dossier?.LireEmpreinteFiche() is { } e && _bilan is not null && e == _bilan.Empreinte; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private void Auto(string texte)
    {
        AutoBar.Visibility = Visibility.Visible;
        AutoText.Text = texte;
    }

    private async void ToutLancer_Click(object sender, RoutedEventArgs e)
    {
        if (_autoEnCours) return;
        var choix = MessageBox.Show(this,
            "JVTECH Atelier va enchaîner seul :\n• les mises à jour Windows, avec les redémarrages nécessaires (il se relance tout seul) ;\n• 7-Zip, VLC et les logiciels cochés ;\n• les contrôles des disques, de la mémoire, de la batterie et des pilotes.\n\nIl s'arrêtera pour les tests manuels, puis enregistrera la fiche, l'annonce et le rapport. Sysprep n'est jamais lancé sans ta confirmation à part.\n\nLa clé USB doit rester branchée. Continuer ?",
            "Tout lancer", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (choix != MessageBoxResult.OK) return;
        await ToutLancerAsync();
    }

    /// <summary>Enchaîne les étapes d'après <see cref="ParcoursAuto"/>. Une étape qui revient deux fois de suite n'a pas abouti : arrêt.</summary>
    private async Task ToutLancerAsync()
    {
        if (_autoEnCours) return;
        _autoEnCours = true;
        _autoArret = false;
        ToutButton.IsEnabled = false;
        try
        {
            EnsureFolder(DateTime.Now);
            _prep = (_prep ?? new EtatPreparation()) with { ToutLancer = true };
            _dossier!.EnregistrerPreparation(_prep);
            _dossier.Journaliser("Tout lancer : début");
            EtapeAuto? precedente = null;
            while (!_autoArret)
            {
                var etape = ParcoursAuto.Prochaine(_prep, _logicielsTentes, _auto.Count > 0, TestsFaits(), FicheAJour());
                if (etape == precedente && etape is not (EtapeAuto.TestsManuels or EtapeAuto.Termine))
                {
                    Auto($"Arrêté : « {ParcoursAuto.Libelle(etape)} » n'a pas abouti. Voir l'étape concernée, puis relancer « Tout lancer ».");
                    _dossier.Journaliser($"Tout lancer : arrêt, {ParcoursAuto.Libelle(etape)} n'a pas abouti");
                    return;
                }
                precedente = etape;
                Auto("Tout lancer : " + ParcoursAuto.Libelle(etape) + "…");
                switch (etape)
                {
                    case EtapeAuto.MisesAJour:
                        ShowStep(1);
                        await RunUpdatePassAsync();
                        break;
                    case EtapeAuto.Redemarrer:
                        ShowStep(1);
                        if (RedemarrerEtReprendre())
                            Auto("Redémarrage dans 30 secondes. JVTECH Atelier se relancera tout seul et continuera (clé USB branchée).");
                        else
                            Auto("Windows a refusé le redémarrage : redémarrer à la main, puis relancer JVTECH Atelier et « Tout lancer ».");
                        return;
                    case EtapeAuto.Logiciels:
                        ShowStep(1);
                        _logicielsTentes = true;
                        await InstallSoftwareAsync();
                        break;
                    case EtapeAuto.Controles:
                        ShowStep(2);
                        await RunChecksAsync();
                        break;
                    case EtapeAuto.TestsManuels:
                        ShowStep(2);
                        Auto("À toi : fais les tests manuels (« Tester les périphériques » aide), puis « Enregistrer les tests ». La fiche, l'annonce et le rapport se feront seuls ensuite.");
                        return;
                    case EtapeAuto.Fiche:
                        ShowStep(3);
                        Compose();
                        SaveListing();
                        break;
                    case EtapeAuto.Termine:
                        _prep = _prep with { ToutLancer = false };
                        _dossier.EnregistrerPreparation(_prep);
                        _dossier.Journaliser($"Tout lancer : terminé ({_bilan?.Empreinte})");
                        var sansV = IdentifiantAtelier.NormaliserInventaire(_dossier.LireInventaire().Inventaire) is null;
                        ShowStep(5);
                        Auto($"Terminé : {_bilan?.Libelle}. Annonce prête dans l'étape Annonce."
                             + (sansV ? " Saisir le n° V… (Identification) avant la finalisation." : "")
                             + (Installation.FinalisationPermise(_mode) ? " Sysprep : à confirmer dans Finalisation." : ""));
                        return;
                }
            }
            Auto("Tout lancer arrêté à ta demande. Relancer « Tout lancer » pour continuer là où il s'est arrêté.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Auto("Tout lancer impossible : " + ex.Message);
        }
        finally
        {
            _autoEnCours = false;
            ToutButton.IsEnabled = true;
        }
    }

    // ---------- Finalisation ----------

    private DecisionFinalisation DecisionActuelle()
    {
        ShowSaleStatus();
        var mode = Installation.Lire();
        string? inventaire = null, empreinte = null;
        if (_dossier is not null)
        {
            inventaire = _dossier.LireInventaire().Inventaire;
            empreinte = _dossier.LireEmpreinteFiche();
        }
        return Finalisation.Decider(mode, _bilan!, inventaire, empreinte, FinalConfirm.Text);
    }

    private void ShowFinalisation()
    {
        var d = DecisionActuelle();
        FinalConditions.Text = d.Permise
            ? "✓ Toutes les conditions sont remplies."
            : string.Join(Environment.NewLine, d.Raisons.Select(r => "✕ " + r));
        SysprepButton.IsEnabled = d.Permise && _arret is null;
    }

    private void FinalConfirm_Changed(object sender, TextChangedEventArgs e)
    {
        if (FinalPanel.Visibility == Visibility.Visible) ShowFinalisation();
    }

    private async void Sysprep_Click(object sender, RoutedEventArgs e)
    {
        var technicien = OperatorBox.Text.Trim();
        var d = DecisionActuelle();
        if (!d.Permise || technicien.Length == 0)
        {
            FinalStatus.Text = technicien.Length == 0 ? "Indiquer le nom du technicien (étape Identification)." : "Conditions non remplies.";
            ShowFinalisation();
            return;
        }
        var choix = MessageBox.Show(this,
            $"Lancer Sysprep sur {PcTitle.Text} ?\n\nWindows repartira sur l'écran de premier démarrage pour l'acheteur, puis le PC s'éteindra. Aucune donnée n'est effacée.\n\nCette étape ne s'annule pas depuis JVTECH Atelier.",
            "Finalisation", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (choix != MessageBoxResult.Yes) return;

        _dossier!.Journaliser($"Finalisation demandée par {technicien} (statut : {_bilan!.Empreinte})");
        SysprepButton.IsEnabled = false;
        FinalStatus.Text = "Sysprep en cours : ne pas éteindre le PC ni retirer la clé USB avant l'extinction.";
        try
        {
            var faites = await RunActionsAsync(new IActionAtelier[] { new SysprepOobe(t => Dispatcher.BeginInvoke(new Action(() => FinalStatus.Text = t))) });
            FinalStatus.Text = faites.Count == 0 ? "Sysprep non lancé." : faites[0].Trace.Detail;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            FinalStatus.Text = "Sysprep impossible : " + ex.Message;
        }
        ShowFinalisation();
    }
}
