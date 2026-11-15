using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Lcia.App.Converters;

/// <summary>true → Collapsed, false → Visible (es. messaggio di benvenuto quando non c'è un repository).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility visibility && visibility != Visibility.Visible;
}

/// <summary>
/// Testo vuoto → Collapsed, altrimenti Visible (avvisi mostrati solo se presenti).
/// Con ConverterParameter="Invert" il contrario (segnaposto mostrati quando manca il contenuto).
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasText = !string.IsNullOrWhiteSpace(value as string);
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            hasText = !hasText;
        }

        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
