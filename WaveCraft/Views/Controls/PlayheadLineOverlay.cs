using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WaveCraft.Views.Controls
{
    /// <summary>
    /// Vertical playhead line overlay with Ableton Live styling.
    /// Orange line with no glow, driven by PlayheadPosition.
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
                    Color.FromRgb(0xFF, 0x66, 0x00),
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
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            double x = PlayheadPosition * w;
            x = Math.Clamp(x, 0, w);

            var pen = new Pen(new SolidColorBrush(LineColor), 2);
            pen.Freeze();
            dc.DrawLine(pen, new Point(x, 0), new Point(x, h));
        }
    }
}