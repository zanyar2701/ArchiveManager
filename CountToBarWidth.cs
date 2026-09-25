using System;
using System.Globalization;
using System.Windows.Data;

namespace ArchiveManager.UI.Converters;

/// <summary>Turns a raw count into a simple proportional bar width (px) for the
/// lightweight bar charts on the Reports screen (spec §11: "simple charts", no
/// charting library dependency needed for a handful of horizontal bars).</summary>
public sealed class CountToBarWidth : IValueConverter
{
    public static readonly CountToBarWidth Instance = new();
    private const double PixelsPerUnit = 18;
    private const double MaxWidth = 380;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        return Math.Min(MaxWidth, Math.Max(4, count * PixelsPerUnit));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
