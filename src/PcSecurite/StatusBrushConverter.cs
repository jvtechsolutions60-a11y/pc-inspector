using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PcSecurite.Checks;

namespace PcSecurite;

/// <summary>Couleur du feu selon le résultat du contrôle.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public static Brush For(CheckStatus status) => (Brush)Application.Current.FindResource(status switch
    {
        CheckStatus.Ok => "OkBrush",
        CheckStatus.Warning => "WarningBrush",
        CheckStatus.Danger => "DangerBrush",
        _ => "InfoBrush",
    });

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        For(value is CheckStatus s ? s : CheckStatus.Info);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Étiquette courte affichée sur chaque carte.</summary>
public sealed class StatusTextConverter : IValueConverter
{
    public static string For(CheckStatus status) => status switch
    {
        CheckStatus.Danger => "URGENT",
        CheckStatus.Warning => "À AMÉLIORER",
        CheckStatus.Ok => "EN ORDRE",
        _ => "INFO",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        For(value is CheckStatus s ? s : CheckStatus.Info);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Les groupes à traiter sont ouverts, les points en ordre sont repliés pour ne pas noyer l'essentiel.</summary>
public sealed class GroupExpandedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value as string != SecurityCheck.VerifiedTitle;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Couleur de la classe A à E, du vert au rouge.</summary>
public static class GradeBrushes
{
    public static Brush For(char letter)
    {
        var color = letter switch
        {
            'A' => "#16A34A",
            'B' => "#65A30D",
            'C' => "#CA8A04",
            'D' => "#EA580C",
            _ => "#DC2626",
        };
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
