using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Jvtech.Lecture;
using Jvtech.Lecture.Collecte;
using Jvtech.Lecture.Regles;

namespace JvtechAudit;

/// <summary>
/// Page d'accueil de JVTECH Audit (cahier § 4.1) : identification, synthèse (alertes prioritaires, recommandations),
/// onze blocs, limites. Lecture seule : rien n'est écrit sur le PC ; le rapport n'est enregistré qu'à l'export,
/// à l'endroit choisi par le technicien.
/// </summary>
public partial class MainWindow : Window
{
    private readonly string _version;
    private DonneesAudit? _donnees;
    private ResultatAudit? _resultat;

    public MainWindow()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        _version = v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
        Title = "JVTECH Audit " + _version;
        VersionText.Text = $"{_version} · Lecture seule : rien n'est modifié sur ce PC";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await AuditerAsync();

    private async void Relancer_Click(object sender, RoutedEventArgs e) => await AuditerAsync();

    private async Task AuditerAsync()
    {
        RelancerButton.IsEnabled = ExportButton.IsEnabled = false;
        CollecteText.Text = "Collecte en cours…";
        var dossier = AppDomain.CurrentDomain.BaseDirectory;
        try
        {
            var progression = new Progress<string>(t => CollecteText.Text = "Collecte : " + t);
            var (donnees, baseW11, avertissement) = await Task.Run(() =>
            {
                var b = LectureWindows11.Base(dossier, out var avert);
                return (LectureAudit.Lire(((IProgress<string>)progression).Report), b, avert);
            });
            _donnees = donnees;
            _resultat = RegleAudit.Evaluer(donnees, baseW11);
            if (avertissement is not null)
                _resultat = _resultat with { Limites = _resultat.Limites.Concat(new[] { "Base Windows 11 : " + avertissement }).ToList() };
            Afficher(donnees, _resultat);
            ExportButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            CollecteText.Text = "Collecte interrompue : " + ex.Message;
        }
        finally
        {
            RelancerButton.IsEnabled = true;
        }
    }

    private void Afficher(DonneesAudit d, ResultatAudit r)
    {
        var ident = r.Blocs.First(b => b.Id == "identification");
        PcText.Text = ident.Valeur;
        PcDetailText.Text = ident.Detail;
        CollecteText.Text = d.Duree is { } t ? $"Collecte terminée en {t.TotalSeconds:0} s" : "Collecte terminée";
        var restreints = d.NonLus.Count;
        DroitsText.Text = d.Administrateur
            ? "Lancé avec les droits administrateur (lecture seule)"
            : $"Lancé sans droits administrateur{(restreints > 0 ? $" : {restreints} point(s) non lu(s)" : "")}";

        SyntheseText.Text = r.Synthese;
        AlertesPanel.Children.Clear();
        if (r.Prioritaires.Count == 0) AlertesPanel.Children.Add(Texte("Aucun point à signaler parmi les contrôles faits.", true));
        foreach (var a in r.Prioritaires)
        {
            var (symbole, couleur) = a.Urgence switch
            {
                Urgence.Critique or Urgence.Important => ("✕", "DangerBrush"),
                Urgence.AVerifier => ("?", "WarningBrush"),
                _ => ("•", "AccentBrush"),
            };
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), Foreground = Pinceau("TextBrush") };
            tb.Inlines.Add(new System.Windows.Documents.Run($"{symbole} {RegleAudit.Libelle(a.Urgence)}  ") { Foreground = Pinceau(couleur), FontWeight = FontWeights.SemiBold });
            tb.Inlines.Add(new System.Windows.Documents.Run(a.Titre) { FontWeight = FontWeights.SemiBold });
            tb.Inlines.Add(new System.Windows.Documents.Run(" : " + a.Constat));
            AlertesPanel.Children.Add(tb);
        }

        RecosPanel.Children.Clear();
        if (r.Recommandations.Count == 0) RecosPanel.Children.Add(Texte("Aucune recommandation.", true));
        foreach (var c in r.Recommandations.Take(6))
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var titre = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Pinceau("TextBrush") };
            titre.Inlines.Add(new System.Windows.Documents.Run($"{RegleAudit.Libelle(c.Categorie)} · {c.Titre}") { FontWeight = FontWeights.SemiBold });
            titre.Inlines.Add(new System.Windows.Documents.Run($" · {RegleAudit.Libelle(c.Confiance)}") { Foreground = Pinceau("MutedTextBrush") });
            p.Children.Add(titre);
            p.Children.Add(Texte(c.Solution + " " + c.Conditions, true));
            RecosPanel.Children.Add(p);
        }
        if (r.Recommandations.Count > 6) RecosPanel.Children.Add(Texte($"+ {r.Recommandations.Count - 6} autre(s) dans le rapport.", true));

        BlocsPanel.Children.Clear();
        foreach (var b in r.Blocs.Where(b => b.Id != "identification")) BlocsPanel.Children.Add(Carte(b));

        LimitesPanel.Children.Clear();
        foreach (var l in r.Limites) LimitesPanel.Children.Add(Texte("• " + l, true));
    }

    private Border Carte(BlocAudit b)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = b.Titre, Style = (Style)FindResource("CardTitle") });
        p.Children.Add(new TextBlock { Text = b.Valeur, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Pinceau("TextBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
        if (b.Detail.Length > 0) p.Children.Add(new TextBlock { Text = b.Detail, Style = (Style)FindResource("CardDetail") });
        var points = b.Points.Where(x => x.Statut != Statut.NonApplicable || b.Statut == Statut.NonApplicable).ToList();
        if (points.Count > 0)
        {
            var liste = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var x in points.Take(8))
            {
                var (symbole, couleur) = Symbole(x.Statut);
                var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 1, 0, 1), Foreground = Pinceau("TextBrush") };
                tb.Inlines.Add(new System.Windows.Documents.Run(symbole + " ") { Foreground = Pinceau(couleur), FontWeight = FontWeights.Bold });
                tb.Inlines.Add(new System.Windows.Documents.Run(x.Texte));
                liste.Children.Add(tb);
            }
            if (points.Count > 8) liste.Children.Add(Texte($"+ {points.Count - 8} autre(s) point(s) dans l'annexe", true));
            p.Children.Add(liste);
        }
        var (s, c) = Symbole(b.Statut);
        p.Children.Add(new TextBlock { Text = $"{s} {Libelles.De(b.Statut)}", Foreground = Pinceau(c), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        p.Children.Add(new TextBlock { Text = b.Interpretation, Style = (Style)FindResource("CardDetail"), FontSize = 11 });
        return new Border { Style = (Style)FindResource("Card"), Width = 370, Margin = new Thickness(4, 0, 4, 8), Child = p, ToolTip = b.Source };
    }

    private static (string, string) Symbole(Statut s) => s switch
    {
        Statut.Conforme => ("✓", "OkBrush"),
        Statut.Anomalie => ("✕", "DangerBrush"),
        Statut.NonApplicable => ("–", "InfoBrush"),
        _ => ("?", "WarningBrush"),
    };

    private TextBlock Texte(string t, bool discret) => new()
    {
        Text = t,
        TextWrapping = TextWrapping.Wrap,
        FontSize = discret ? 12 : 13,
        Foreground = Pinceau(discret ? "MutedTextBrush" : "TextBrush"),
        Margin = new Thickness(0, 1, 0, 1),
    };

    private Brush Pinceau(string cle) => (Brush)FindResource(cle);

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_donnees is null || _resultat is null) return;
        var options = OptionsExport.Demander(this);
        if (options is null) return;
        var html = RapportAudit.Html(_donnees, _resultat, options, _version);
        var nom = $"Audit {Annonce.NomPc(_donnees.Fabricant, _donnees.Modele) ?? "PC"} {_donnees.Date:yyyy-MM-dd HHmm}.html";
        var chemin = ExportRapport.Enregistrer(this, html, nom);
        if (chemin is not null)
            MessageBox.Show(this, "Rapport enregistré :\n" + chemin + "\n\nÀ ouvrir dans le navigateur ; imprimable en PDF.", "JVTECH Audit", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
