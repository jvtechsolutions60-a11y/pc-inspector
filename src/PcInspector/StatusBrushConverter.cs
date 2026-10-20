using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PcInspector;

/// <summary>Colore les valeurs jugées : ✘ en rouge, ! en orange, ✔ en vert, le reste garde la couleur normale.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is not string s ? null
            : s.StartsWith("✘ ", StringComparison.Ordinal) ? "CriticalBrush"
            : s.StartsWith("! ", StringComparison.Ordinal) ? "WarningBrush"
            : s.StartsWith("✔ ", StringComparison.Ordinal) ? "OkBrush"
            : null;
        return key is not null && Application.Current?.TryFindResource(key) is { } brush ? brush : DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
