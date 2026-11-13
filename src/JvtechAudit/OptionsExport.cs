using System.Windows;
using System.Windows.Controls;
using Jvtech.Lecture.Regles;

namespace JvtechAudit;

/// <summary>Choix avant l'export : client, technicien, annexe technicien, anonymisation.</summary>
internal static class OptionsExport
{
    public static OptionsRapportAudit? Demander(Window proprietaire)
    {
        var client = new TextBox { Margin = new Thickness(0, 2, 0, 8) };
        var technicien = new TextBox { Margin = new Thickness(0, 2, 0, 8) };
        var annexe = new CheckBox { Content = "Ajouter l'annexe technicien (valeurs détaillées, sources, versions des règles)", IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
        var anonyme = new CheckBox { Content = "Anonymiser : masquer n° de série, nom du PC, domaine, comptes et adresses IP", Margin = new Thickness(0, 0, 0, 12) };
        var ok = new Button { Content = "Choisir l'emplacement…", IsDefault = true, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var annuler = new Button { Content = "Annuler", IsCancel = true, Padding = new Thickness(12, 6, 12, 6) };
        if (Application.Current.TryFindResource("PrimaryButton") is Style primaire) ok.Style = primaire;
        if (Application.Current.TryFindResource("BaseButton") is Style simple) annuler.Style = simple;

        var boutons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        boutons.Children.Add(ok);
        boutons.Children.Add(annuler);
        var panneau = new StackPanel { Margin = new Thickness(20) };
        panneau.Children.Add(new TextBlock { Text = "Client (facultatif)" });
        panneau.Children.Add(client);
        panneau.Children.Add(new TextBlock { Text = "Technicien (facultatif)" });
        panneau.Children.Add(technicien);
        panneau.Children.Add(annexe);
        panneau.Children.Add(anonyme);
        panneau.Children.Add(new TextBlock { Text = "Aucun mot de passe ni clé BitLocker n'est lu ni écrit dans le rapport.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 12) });
        panneau.Children.Add(boutons);

        var fenetre = new Window
        {
            Title = "Exporter le rapport",
            Owner = proprietaire,
            Content = panneau,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = proprietaire.FontFamily,
        };
        ok.Click += (_, _) => fenetre.DialogResult = true;
        client.Focus();
        if (fenetre.ShowDialog() != true) return null;
        return new OptionsRapportAudit(annexe.IsChecked == true, anonyme.IsChecked == true,
            string.IsNullOrWhiteSpace(client.Text) ? null : client.Text.Trim(),
            string.IsNullOrWhiteSpace(technicien.Text) ? null : technicien.Text.Trim());
    }
}
