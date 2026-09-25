using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ArchiveManager.UI.Converters;

/// <summary>Maps an IconKey ("pdf","word","excel","zip","ppt",...) to its
/// design-system accent color (spec §2/§23 icon color table).</summary>
public sealed class FileTypeToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string ?? "other";
        var hex = key switch
        {
            "pdf" => "#D64545",
            "word" => "#3A6EA5",
            "excel" => "#3F8F5F",
            "zip" => "#E0A339",
            "ppt" => "#B85C9E",
            "image" => "#7A5C3E",
            "video" => "#7A5C3E",
            "text" => "#7A7266",
            _ => "#7A7266"
        };
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
