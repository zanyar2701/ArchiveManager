using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ArchiveManager.UI.Converters;

/// <summary>
/// Shows an element only when the bound enum equals ConverterParameter (by name).
/// Used to swap the four top-level "tabs" (Archive/Reports/Settings/Guide) without
/// a separate ContentControl+DataTemplateSelector — simple and explicit at this scale.
/// </summary>
public sealed class EnumToVisibility : IValueConverter
{
    public static readonly EnumToVisibility Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return Visibility.Collapsed;
        var valueName = value.ToString();
        var paramName = parameter.ToString();
        return string.Equals(valueName, paramName, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
