using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WaveCraft.Views.Controls
{
    /// <summary>
    /// A simple overlay that draws a vertical playhead line over the track area.
    /// Position is bound to the normalised PlayheadPosition (0–1).
    /// </summary>
    public class PlayheadLineOverlay : FrameworkElement
    {
        public static readonly DependencyProperty PlayheadPositionProperty =
            DependencyProperty.Register(nameof(PlayheadPosition), typeof(double),
                typeof(PlayheadLineOverlay),
                new FrameworkPropertyMetadata(0.0,
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LineColorProperty =
            DependencyProperty.Register(nameof(LineColor), typeof(Color),
                typeof(PlayheadLineOverlay),
                new FrameworkPropertyMetadata(
                    Color.FromRgb(166, 227, 161),
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public double PlayheadPosition
        {
            get => (double)GetValue(PlayheadPositionProperty);
            set => SetValue(PlayheadPositionProperty, value);
        }

        public Color LineColor
        {
            get => (Color)GetValue(LineColorProperty);
            set => SetValue(LineColorProperty, value);
        }

        public PlayheadLineOverlay()
        {
            IsHitTestVisible = false; // Don't block mouse events on tracks below
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            double x = PlayheadPosition * w;
            x = Math.Clamp(x, 0, w);

            // Draw the line
            var pen = new Pen(new SolidColorBrush(LineColor), 1.5);
            pen.Freeze();
            dc.DrawLine(pen, new Point(x, 0), new Point(x, h));

            // Draw a small glow effect (two semi-transparent lines)
            var glowPen = new Pen(
                new SolidColorBrush(Color.FromArgb(50, LineColor.R, LineColor.G, LineColor.B)),
                3);
            glowPen.Freeze();
            dc.DrawLine(glowPen, new Point(x, 0), new Point(x, h));
        }
    }
}