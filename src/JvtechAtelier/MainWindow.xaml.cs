using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Jvtech.Atelier.Dossier;
using Jvtech.Lecture;
using Jvtech.Lecture.Collecte;
using Jvtech.Lecture.Regles;
using PcInspector;

namespace JvtechAtelier;

/// <summary>Une ligne de l'inventaire : la valeur détectée (figée) et la valeur retenue (modifiable).</summary>
public sealed class LigneChamp : INotifyPropertyChanged
{
    public LigneChamp(Champ champ)
    {
        Champ = champ;
        _retenu = champ.Valeur ?? "";
    }

    public Champ Champ { get; }
    public string Nom => Champ.Nom;
    public string Detecte => Champ.Detecte ?? Jvtech.Lecture.Champ.NonDetecte;
    public string Source => Champ.Source ?? "";

    private string _retenu;
    public string Retenu
    {
        get => _retenu;
        set { _retenu = value ?? ""; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Retenu))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class MainWindow : Window
{
    private static readonly string[] StepNames =
        { "1 · Identification", "2 · Préparation", "3 · Tests", "4 · Fiche", "5 · Annonce", "6 · Finalisation" };

    private readonly ModeWindows _mode = Installation.Lire();
    private readonly string _racine = DossierPc.Racine(AppPaths.ExeFolder);
    private List<LigneChamp> _lignes = new();
    private DossierPc? _dossier;

    public MainWindow()
    {
        InitializeComponent();
        Title = "JVTECH Atelier " + AppInfo.Version;
        VersionText.Text = $"{AppInfo.Version} · {Installation.Libelle(_mode)}";
        FinalizeText.Text = Installation.FinalisationPermise(_mode)
            ? "Sysprep pourra être proposé à la fin (Windows en mode audit), après confirmation à part."
            : "Sysprep ne sera pas proposé : Windows n'est pas en mode audit. La finalisation depuis une session normale est à l'étude.";
        ShowSteps(0);
        ShowSaleStatus();
    }

    private void ShowSteps(int current)
    {
        var accent = (Brush)FindResource("AccentBrush");
        var border = (Brush)FindResource("BorderBrush");
        Steps.ItemsSource = StepNames.Select((n, i) => new
        {
            Title = n.ToUpperInvariant(),
            State = i == current ? "⟳ En cours" : i < current ? "✓ Terminé" : "○ À venir",
            Border = i == current ? accent : border,
            Thickness = new Thickness(i == current ? 2 : 1),
        }).ToList();
    }

    private void ShowSaleStatus()
    {
        // Les contrôles arrivent avec l'étape Tests : pour l'instant la règle donne « Incomplet », jamais « Prêt ».
        var bilan = RegleVente.Evaluer(Array.Empty<Controle>(), new[] { "disque", "batterie", "memoire", "tests-manuels" });
        SaleStatus.Text = bilan.Libelle;
        SaleDetail.Text = string.Join(Environment.NewLine, bilan.Manquants.Select(m => "○ " + m + " : non fait"));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await ReadPcAsync();

    private async void Reload_Click(object sender, RoutedEventArgs e) => await ReadPcAsync();

    private async Task ReadPcAsync()
    {
        ReloadButton.IsEnabled = SaveButton.IsEnabled = false;
        StatusText.Text = "Lecture du PC…";
        try
        {
            var champs = await Task.Run(() => Inventaire.Lire());
            var serie = champs.FirstOrDefault(c => c.Id == "serie")?.Detecte;
            _dossier = await Task.Run(() => DossierPc.Trouver(_racine, serie));
            if (_dossier is not null)
            {
                var (inventaire, corrections) = _dossier.LireInventaire();
                DossierPc.AppliquerCorrections(champs, corrections);
                if (string.IsNullOrWhiteSpace(InventoryBox.Text)) InventoryBox.Text = inventaire ?? "";
            }
            _lignes = champs.Select(c => new LigneChamp(c)).ToList();
            FieldsGrid.ItemsSource = _lignes;
            ShowHeader();
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
        FolderText.Text = _dossier is null ? $"Dossiers : {_racine}" : $"Dossier : {_dossier.Chemin}";
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
        var corrige = _lignes.Any(l => Champ.Nettoyer(l.Retenu) != (l.Champ.Detecte));
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
                if (Champ.Nettoyer(l.Retenu) is null || Champ.Nettoyer(l.Retenu) == l.Champ.Detecte) l.Champ.AnnulerCorrection();
                else if (Champ.Nettoyer(l.Retenu) != l.Champ.Corrige) l.Champ.Corriger(l.Retenu, technicien, maintenant);
                if (avant != l.Champ.Valeur)
                    changements.Add($"Correction {l.Champ.Id} : « {avant ?? Champ.NonDetecte} » → « {l.Champ.Affichage} » (détecté : « {l.Champ.Detecte ?? Champ.NonDetecte} », {technicien})");
            }

            var inventaire = IdentifiantAtelier.NormaliserInventaire(InventoryBox.Text);
            var serie = _lignes.FirstOrDefault(l => l.Champ.Id == "serie")?.Champ.Detecte;
            var identifiant = IdentifiantAtelier.Calculer(inventaire, serie, maintenant);
            if (_dossier is null)
            {
                _dossier = DossierPc.OuvrirOuCreer(_racine, identifiant);
                _dossier.Journaliser($"Dossier créé ({Installation.Libelle(_mode)}, {AppInfo.Product} {AppInfo.Version})", maintenant);
            }
            else if (inventaire is not null && !string.Equals(_dossier.Identifiant, inventaire, StringComparison.OrdinalIgnoreCase))
            {
                _dossier.Renommer(inventaire);
            }

            foreach (var ligne in changements) _dossier.Journaliser(ligne, maintenant);
            _dossier.EnregistrerInventaire(inventaire, _lignes.Select(l => l.Champ));
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
}
