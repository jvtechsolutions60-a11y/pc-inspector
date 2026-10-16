using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PcSecurite.Checks;

namespace PcSecurite;

/// <summary>Couleur du feu selon le résultat du contrôle.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            CheckStatus.Ok => "OkBrush",
            CheckStatus.Warning => "WarningBrush",
            CheckStatus.Danger => "DangerBrush",
            _ => "InfoBrush",
        };
        return Application.Current.FindResource(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
