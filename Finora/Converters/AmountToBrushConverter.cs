using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Finora.Converters;

public class AmountToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal amount)
        {
            if (amount > 0) return new SolidColorBrush(Color.FromRgb(52, 211, 153));
            if (amount < 0) return new SolidColorBrush(Color.FromRgb(248, 113, 113));
        }

        return new SolidColorBrush(Color.FromRgb(203, 213, 225));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
