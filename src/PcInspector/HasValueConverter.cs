using System;
using System.Globalization;
using System.Windows.Data;

namespace PcInspector;

/// <summary>Vrai quand la valeur liée n'est ni nulle ni vide (sert à afficher les liens).</summary>
public sealed class HasValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? s.Length > 0 : value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
