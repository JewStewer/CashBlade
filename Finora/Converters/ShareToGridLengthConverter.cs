using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Finora.Converters;

/// <summary>
/// Converts a Share value (0.0–1.0) to a star-sized GridLength.
/// Pass ConverterParameter="inverse" to get the complementary (empty) portion.
/// </summary>
public class ShareToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var share = value is double d ? d : 0.0;
        share = Math.Clamp(share, 0.0, 1.0);
        var inverse = parameter is string s && s == "inverse";
        var stars = inverse ? (1.0 - share) : share;
        // Ensure at least a tiny sliver so the row always exists
        return new GridLength(Math.Max(stars, 0.001), GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
