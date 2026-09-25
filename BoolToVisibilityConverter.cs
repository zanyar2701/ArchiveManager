using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ArchiveManager.UI.Converters;

/// <summary>Standard bool→Visibility, with an optional "Invert" parameter
/// (pass the string "Invert") for the many "show only when empty/disabled" cases.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (parameter as string == "Invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
