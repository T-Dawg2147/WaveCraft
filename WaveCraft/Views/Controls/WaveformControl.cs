using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaveCraft.Core.Analysis;

namespace WaveCraft.Views.Controls
{
    /// <summary>
    /// Audio waveform display with Ableton Live styling.
    /// Dark background with flat waveform rendering in track color.
    /// Uses WriteableBitmap for performance.
    /// </summary>
    public class WaveformControl : Image
    {
        public static readonly DependencyProperty WaveformProperty =
            DependencyProperty.Register(nameof(Waveform), typeof(WaveformData),
                typeof(WaveformControl),
                new PropertyMetadata(null, OnWaveformChanged));

        public static readonly DependencyProperty WaveColorProperty =
            DependencyProperty.Register(nameof(WaveColor), typeof(Color),
                typeof(WaveformControl),
                new PropertyMetadata(Color.FromRgb(0xFF, 0x66, 0x00), OnWaveformChanged));

        public static readonly DependencyProperty RmsColorProperty =
            DependencyProperty.Register(nameof(RmsColor), typeof(Color),
                typeof(WaveformControl),
                new PropertyMetadata(Color.FromRgb(0x2D, 0x2D, 0x2D), OnWaveformChanged));

        public static readonly DependencyProperty BackgroundColorProperty =
            DependencyProperty.Register(nameof(BackgroundColor), typeof(Color),
                typeof(WaveformControl),
                new PropertyMetadata(Color.FromRgb(0x1E, 0x1E, 0x1E), OnWaveformChanged));

        public WaveformData? Waveform
        {
            get => (WaveformData?)GetValue(WaveformProperty);
            set => SetValue(WaveformProperty, value);
        }

        public Color WaveColor
        {
            get => (Color)GetValue(WaveColorProperty);
            set => SetValue(WaveColorProperty, value);
        }

        public Color RmsColor
        {
            get => (Color)GetValue(RmsColorProperty);
            set => SetValue(RmsColorProperty, value);
        }

        public Color BackgroundColor
        {
            get => (Color)GetValue(BackgroundColorProperty);
            set => SetValue(BackgroundColorProperty, value);
        }

        private static void OnWaveformChanged(DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is WaveformControl control)
                control.RedrawWaveform();
        }

        private void RedrawWaveform()
        {
            var data = Waveform;
            if (data == null || data.ColumnCount == 0) return;

            int width = data.ColumnCount;
            int height = (int)Math.Max(ActualHeight, 80);
            if (width <= 0 || height <= 0) return;

            var bitmap = new WriteableBitmap(width, height, 96, 96,
                PixelFormats.Bgra32, null);

            unsafe
            {
                bitmap.Lock();
                try
                {
                    uint* pixels = (uint*)bitmap.BackBuffer;
                    int stride = bitmap.BackBufferStride / 4;

                    uint bgPacked = PackColor(BackgroundColor);
                    uint wavePacked = PackColor(WaveColor);
                    uint rmsPacked = PackColor(RmsColor);

                    int totalPixels = stride * height;
                    for (int i = 0; i < totalPixels; i++)
                        pixels[i] = bgPacked;

                    int centreY = height / 2;
                    uint centerLinePacked = PackColor(Color.FromRgb(0x33, 0x33, 0x33));
                    for (int x = 0; x < width; x++)
                        pixels[centreY * stride + x] = centerLinePacked;

                    float halfHeight = height / 2f;

                    for (int col = 0; col < data.ColumnCount && col < width; col++)
                    {
                        float rms = data.RmsValues[col];
                        int rmsTop = (int)(centreY - rms * halfHeight);
                        int rmsBot = (int)(centreY + rms * halfHeight);
                        rmsTop = Math.Clamp(rmsTop, 0, height - 1);
                        rmsBot = Math.Clamp(rmsBot, 0, height - 1);

                        for (int y = rmsTop; y <= rmsBot; y++)
                            pixels[y * stride + col] = rmsPacked;

                        int peakTop = (int)(centreY + data.MinPeaks[col] * halfHeight);
                        int peakBot = (int)(centreY + data.MaxPeaks[col] * halfHeight);
                        peakTop = Math.Clamp(Math.Min(peakTop, peakBot), 0, height - 1);
                        peakBot = Math.Clamp(Math.Max(peakTop, peakBot), 0, height - 1);

                        for (int y = peakTop; y <= peakBot; y++)
                            pixels[y * stride + col] = wavePacked;
                    }

                    bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                }
                finally
                {
                    bitmap.Unlock();
                }
            }

            Source = bitmap;
            Stretch = Stretch.Fill;
        }

        private static uint PackColor(Color c)
        {
            unchecked
            {
                return (uint)c.B | ((uint)c.G << 8) |
                       ((uint)c.R << 16) | ((uint)c.A << 24);
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            RedrawWaveform();
        }
    }
}