using System.Globalization;
using System.Windows.Data;

namespace WaveCraft.Converters
{
    /// <summary>
    /// Converts a 0-1 level value to a pixel height.
    /// Parameter is the maximum height in pixels.
    /// </summary>
    public class LevelToHeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is float level && parameter is string maxHeightStr 
                && double.TryParse(maxHeightStr, out double maxHeight))
            {
                return Math.Max(0, Math.Min(maxHeight, level * maxHeight));
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
