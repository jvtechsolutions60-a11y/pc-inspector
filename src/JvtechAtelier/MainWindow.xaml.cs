using System;
using System.Linq;
using System.Windows;
using Jvtech.Lecture;
using Jvtech.Lecture.Regles;
using PcInspector;

namespace JvtechAtelier;

public partial class MainWindow : Window
{
    private static readonly string[] StepNames =
        { "1 · Identification", "2 · Préparation", "3 · Tests", "4 · Fiche", "5 · Annonce", "6 · Finalisation" };

    public MainWindow()
    {
        InitializeComponent();
        Title = "JVTECH Atelier " + AppInfo.Version;
        VersionText.Text = $"{AppInfo.Version} · aperçu du lot 0";
        Steps.ItemsSource = StepNames.Select(n => new { Title = n.ToUpperInvariant(), State = "○ À venir (lot 1)" }).ToList();

        // Aucun contrôle encore fait : la règle donne « Incomplet », jamais « Prêt » par défaut.
        var bilan = RegleVente.Evaluer(Array.Empty<Controle>(), Array.Empty<string>());
        SaleStatus.Text = bilan.Libelle;
        SaleDetail.Text = string.Join(Environment.NewLine, bilan.Manquants.Select(m => "○ " + m));
    }
}
