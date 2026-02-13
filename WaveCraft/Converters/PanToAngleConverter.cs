using System.Globalization;
using System.Windows.Data;

namespace WaveCraft.Converters
{
    /// <summary>
    /// Converts a pan value (-1 to 1) to a rotation angle for visual display.
    /// -1 (full left) = -45°, 0 (center) = 0°, 1 (full right) = 45°
    /// </summary>
    public class PanToAngleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is float pan)
            {
                return pan * 45.0; // -45° to +45°
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double angle)
            {
                return (float)(angle / 45.0);
            }
            return 0f;
        }
    }
}
