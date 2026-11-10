using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PcMaintenance.Preparation;

namespace PcMaintenance;

/// <summary>
/// Affichée quand « /preparation » est lancé hors mode audit. Par défaut rien n'est fait. Pour préparer quand même
/// ce PC (cas d'un PC d'achat-revente déjà installé), le technicien tape le nom exact du PC : un clic machinal ne
/// suffit pas. Même confirmée, la préparation ne lancera jamais Sysprep sur ce PC.
/// </summary>
internal sealed class PreparationRefusalWindow : Window
{
    private readonly TextBox _typed = new() { Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 6, 0, 12) };
    private readonly Button _go = new() { Content = "Préparer ce PC", Padding = new Thickness(14, 6, 14, 6), IsEnabled = false };

    private PreparationRefusalWindow(string reason, string machine)
    {
        Title = "Préparation revente refusée";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        if (TryFindResource("ContentBrush") is Brush background) Background = background;

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "La préparation revente n'a pas été lancée.",
            FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8),
        });
        panel.Children.Add(new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(new TextBlock
        {
            Text = "Elle retire des applications, change des réglages pour un acheteur et redémarre le PC plusieurs fois. " +
                   "Elle est faite pour un PC effacé et réinstallé par le serveur PXE de l'atelier (mode audit).\n\n" +
                   "S'il s'agit bien d'un PC d'achat-revente déjà installé, la préparation peut être faite quand même, " +
                   "mais sans remise au premier démarrage (Sysprep). Pour cela, taper le nom de ce PC :  " + machine,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(_typed);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Ne rien faire", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), IsCancel = true, IsDefault = true };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_go);
        panel.Children.Add(buttons);
        Content = panel;

        _typed.TextChanged += (_, _) => _go.IsEnabled = PreparationGuard.IsConfirmation(_typed.Text, machine);
        _go.Click += (_, _) => { DialogResult = true; };
    }

    /// <summary>True si le technicien a confirmé en tapant le nom du PC.</summary>
    public static bool Ask(string reason, string machine) =>
        new PreparationRefusalWindow(reason, machine).ShowDialog() == true;
}
