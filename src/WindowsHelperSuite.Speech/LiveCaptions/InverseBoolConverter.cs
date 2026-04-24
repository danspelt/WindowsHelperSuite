using System.Globalization;
using System.Windows.Data;

namespace WindowsHelperSuite.Speech.LiveCaptions;

/// <summary>
/// Inverts a boolean value (true → false, false → true).
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return value;
    }
}
