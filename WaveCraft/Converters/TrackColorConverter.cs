using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WaveCraft.Converters
{
    /// <summary>
    /// Converter that assigns Ableton-style colors to tracks based on their index.
    /// Cycles through muted pastel colors.
    /// </summary>
    public class TrackColorConverter : IValueConverter
    {
        private static readonly Color[] TrackColors = new[]
        {
            (Color)ColorConverter.ConvertFromString("#5B8A72"),
            (Color)ColorConverter.ConvertFromString("#8B6B8A"),
            (Color)ColorConverter.ConvertFromString("#6B8A8B"),
            (Color)ColorConverter.ConvertFromString("#8A8B5B"),
            (Color)ColorConverter.ConvertFromString("#8B5B5B"),
            (Color)ColorConverter.ConvertFromString("#5B6B8A")
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                var color = TrackColors[index % TrackColors.Length];
                return new SolidColorBrush(color);
            }
            return new SolidColorBrush(TrackColors[0]);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
