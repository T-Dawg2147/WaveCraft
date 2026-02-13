using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WaveCraft.Views.Controls
{
    /// <summary>
    /// A vertical peak meter with RMS and peak indicators.
    /// Uses gradient colour stops: green → yellow → red.
    /// </summary>
    public class PeakMeterControl : Control
    {
        public static readonly DependencyProperty PeakLevelProperty =
            DependencyProperty.Register(nameof(PeakLevel), typeof(float),
                typeof(PeakMeterControl),
                new FrameworkPropertyMetadata(0f, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty RmsLevelProperty =
            DependencyProperty.Register(nameof(RmsLevel), typeof(float),
                typeof(PeakMeterControl),
                new FrameworkPropertyMetadata(0f, FrameworkPropertyMetadataOptions.AffectsRender));

        public float PeakLevel
        {
            get => (float)GetValue(PeakLevelProperty);
            set => SetValue(PeakLevelProperty, value);
        }

        public float RmsLevel
        {
            get => (float)GetValue(RmsLevelProperty);
            set => SetValue(RmsLevelProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            // Background
            dc.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(17, 17, 27)),
                null, new Rect(0, 0, w, h));

            // RMS level bar
            double rmsHeight = Math.Clamp(RmsLevel, 0, 1) * h;
            if (rmsHeight > 0)
            {
                var rmsRect = new Rect(1, h - rmsHeight, w - 2, rmsHeight);
                dc.DrawRectangle(GetLevelBrush(RmsLevel, 0.6), null, rmsRect);
            }

            // Peak level indicator (thin line)
            double peakY = h - Math.Clamp(PeakLevel, 0, 1) * h;
            if (PeakLevel > 0.001f)
            {
                var peakColor = PeakLevel > 0.9f
                    ? Colors.Red
                    : PeakLevel > 0.7f ? Colors.Yellow : Colors.LimeGreen;
                dc.DrawLine(new Pen(new SolidColorBrush(peakColor), 2),
                    new Point(0, peakY), new Point(w, peakY));
            }

            // Border
            dc.DrawRectangle(null,
                new Pen(new SolidColorBrush(Color.FromRgb(69, 71, 90)), 1),
                new Rect(0, 0, w, h));
        }

        private static Brush GetLevelBrush(float level, double opacity)
        {
            Color color;
            if (level > 0.9f)
                color = Color.FromArgb((byte)(opacity * 255), 255, 50, 50);
            else if (level > 0.7f)
                color = Color.FromArgb((byte)(opacity * 255), 255, 220, 50);
            else
                color = Color.FromArgb((byte)(opacity * 255), 50, 220, 100);

            return new SolidColorBrush(color);
        }
    }
}