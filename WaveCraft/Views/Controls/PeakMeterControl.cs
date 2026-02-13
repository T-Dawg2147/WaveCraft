using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WaveCraft.Views.Controls
{
    /// <summary>
    /// Vertical stereo peak meter with segmented green→yellow→red bars and peak hold indicators.
    /// Ableton Live styling: flat, minimalist, no gradients or glows.
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

        private static readonly Color BgColor = Color.FromRgb(0x1E, 0x1E, 0x1E);
        private static readonly Color SegmentGap = Color.FromRgb(0x2D, 0x2D, 0x2D);
        private static readonly Color GreenColor = Color.FromRgb(0x00, 0xCC, 0x00);
        private static readonly Color YellowColor = Color.FromRgb(0xCC, 0xCC, 0x00);
        private static readonly Color RedColor = Color.FromRgb(0xCC, 0x00, 0x00);
        private static readonly Color BorderColor = Color.FromRgb(0x33, 0x33, 0x33);

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            dc.DrawRectangle(new SolidColorBrush(BgColor), null, new Rect(0, 0, w, h));

            const int segmentCount = 24;
            const int gapSize = 1;
            double segmentHeight = (h - (segmentCount - 1) * gapSize) / segmentCount;

            float rmsLevel = Math.Clamp(RmsLevel, 0, 1);
            float peakLevel = Math.Clamp(PeakLevel, 0, 1);

            for (int i = 0; i < segmentCount; i++)
            {
                double segY = h - (i + 1) * segmentHeight - i * gapSize;
                float segmentThreshold = (i + 1) / (float)segmentCount;

                Color segColor;
                if (segmentThreshold > 0.9f)
                    segColor = RedColor;
                else if (segmentThreshold > 0.7f)
                    segColor = YellowColor;
                else
                    segColor = GreenColor;

                if (rmsLevel >= segmentThreshold)
                {
                    dc.DrawRectangle(new SolidColorBrush(segColor), null,
                        new Rect(0, segY, w, segmentHeight));
                }
            }

            double peakY = h - (peakLevel * h);
            if (peakLevel > 0.001f)
            {
                Color peakColor;
                if (peakLevel > 0.9f)
                    peakColor = RedColor;
                else if (peakLevel > 0.7f)
                    peakColor = YellowColor;
                else
                    peakColor = GreenColor;

                dc.DrawRectangle(new SolidColorBrush(peakColor), null,
                    new Rect(0, peakY - 1, w, 2));
            }

            dc.DrawRectangle(null, new Pen(new SolidColorBrush(BorderColor), 1),
                new Rect(0.5, 0.5, w - 1, h - 1));
        }
    }
}