using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PcInspector.Report;

/// <summary>
/// Impression directe de l'étiquette PC sur une étiqueteuse (Brother QL, Dymo LabelWriter…) ou une imprimante classique :
/// l'étiquette est agrandie ou réduite pour remplir le format choisi dans la fenêtre d'impression de Windows.
/// En noir et blanc, lisible sur une imprimante thermique.
/// </summary>
public static class LabelPrinter
{
    // Proportions d'une carte de visite (85 × 55 mm) : convient aussi aux rouleaux 62 mm des Brother QL.
    private const double Width = 340, Height = 220;

    public static bool Print(Window owner, ReportInfo info)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return false;

        var label = Build(info);
        var area = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
        // Étiquette en long ou en large selon le papier choisi.
        var landscape = area.Width < area.Height;
        var box = new Viewbox { Child = label, Stretch = Stretch.Uniform, Width = area.Width, Height = area.Height };
        if (landscape) label.LayoutTransform = new RotateTransform(90);
        box.Measure(area);
        box.Arrange(new Rect(area));
        box.UpdateLayout();
        dialog.PrintVisual(box, "Étiquette PC");
        return true;
    }

    private static FrameworkElement Build(ReportInfo info)
    {
        var root = new StackPanel { Width = Width, Height = Height, Background = Brushes.White };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        if (Logo(info.LogoPath) is { } logo)
        {
            var image = new Image { Source = logo, MaxHeight = 34, MaxWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(image, Dock.Left);
            header.Children.Add(image);
        }
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Text(string.IsNullOrWhiteSpace(info.Company) ? "Fiche du PC" : info.Company, 15, FontWeights.Bold));
        if (!string.IsNullOrWhiteSpace(info.Contact)) title.Children.Add(Text(info.Contact, 10, FontWeights.Normal));
        header.Children.Add(title);
        root.Children.Add(header);
        root.Children.Add(new Border { Height = 1.5, Background = Brushes.Black, Margin = new Thickness(0, 0, 0, 5) });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var row = 0;
        foreach (var (name, value) in LabelWriter.Rows(info))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = Text(name, 10, FontWeights.SemiBold);
            label.Margin = new Thickness(0, 0, 8, 1);
            Grid.SetRow(label, row);
            var text = Text(value, 10, FontWeights.Normal);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            grid.Children.Add(label);
            grid.Children.Add(text);
            row++;
        }
        root.Children.Add(grid);
        return new Border { Child = root, Padding = new Thickness(8), Background = Brushes.White };
    }

    private static TextBlock Text(string text, double size, FontWeight weight) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        FontFamily = new FontFamily("Segoe UI"),
        Foreground = Brushes.Black,
        TextWrapping = TextWrapping.Wrap,
    };

    private static BitmapImage? Logo(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException) { return null; }
    }
}
