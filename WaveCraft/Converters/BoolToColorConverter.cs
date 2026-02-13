using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WaveCraft.Converters
{
    /// <summary>
    /// Converter that returns different colors based on boolean value.
    /// Parameter format: "TrueColor,FalseColor" (e.g., "#FF6600,#2D2D2D")
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not bool boolValue || parameter is not string paramString)
                return new SolidColorBrush(Colors.Transparent);

            var colors = paramString.Split(',');
            if (colors.Length != 2)
                return new SolidColorBrush(Colors.Transparent);

            var colorStr = boolValue ? colors[0].Trim() : colors[1].Trim();
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorStr));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
