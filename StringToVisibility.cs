using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ArchiveManager.UI.Converters;

/// <summary>Visible only when the bound string is non-empty — used for inline
/// validation messages that should collapse when there's nothing to show.</summary>
public sealed class StringToVisibility : IValueConverter
{
    public static readonly StringToVisibility Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
