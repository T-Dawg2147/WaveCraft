using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WaveCraft.Views.Controls
{
    /// <summary>
    /// A timeline ruler with a draggable playhead.
    /// Shows time markings and allows click-to-seek and drag-to-scrub.
    ///
    /// Renders using WriteableBitmap for consistent performance
    /// with the rest of the app.
    /// </summary>
    public class PlayheadTimelineControl : Canvas
    {
        // ---- Dependency Properties ----

        public static readonly DependencyProperty PlayheadPositionProperty =
            DependencyProperty.Register(nameof(PlayheadPosition), typeof(double),
                typeof(PlayheadTimelineControl),
                new FrameworkPropertyMetadata(0.0,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnPlayheadChanged));

        public static readonly DependencyProperty TotalDurationProperty =
            DependencyProperty.Register(nameof(TotalDuration), typeof(TimeSpan),
                typeof(PlayheadTimelineControl),
                new PropertyMetadata(TimeSpan.FromSeconds(30), OnLayoutChanged));

        public static readonly DependencyProperty IsPlayingProperty =
            DependencyProperty.Register(nameof(IsPlaying), typeof(bool),
                typeof(PlayheadTimelineControl),
                new PropertyMetadata(false, OnLayoutChanged));

        public static readonly DependencyProperty BpmProperty =
            DependencyProperty.Register(nameof(Bpm), typeof(float),
                typeof(PlayheadTimelineControl),
                new PropertyMetadata(120f, OnLayoutChanged));

        /// <summary>
        /// Normalised playhead position (0.0 to 1.0). Two-way bound.
        /// </summary>
        public double PlayheadPosition
        {
            get => (double)GetValue(PlayheadPositionProperty);
            set => SetValue(PlayheadPositionProperty, value);
        }

        public TimeSpan TotalDuration
        {
            get => (TimeSpan)GetValue(TotalDurationProperty);
            set => SetValue(TotalDurationProperty, value);
        }

        public bool IsPlaying
        {
            get => (bool)GetValue(IsPlayingProperty);
            set => SetValue(IsPlayingProperty, value);
        }

        public float Bpm
        {
            get => (float)GetValue(BpmProperty);
            set => SetValue(BpmProperty, value);
        }

        // ---- Events ----

        /// <summary>
        /// Fired when the user clicks or drags to seek.
        /// The parameter is the normalised position (0–1).
        /// </summary>
        public event Action<double>? SeekRequested;

        // ---- Internal state ----

        private WriteableBitmap? _bitmap;
        private readonly Image _image;
        private bool _isDragging;

        // Colours
        private static readonly uint BgColor = Pack(24, 24, 37);
        private static readonly uint TickMajor = Pack(100, 100, 130);
        private static readonly uint TickMinor = Pack(55, 55, 75);
        private static readonly uint TickText = Pack(166, 173, 200);
        private static readonly uint PlayheadCol = Pack(166, 227, 161);
        private static readonly uint PlayheadGlow = Pack(166, 227, 161, 80);

        public PlayheadTimelineControl()
        {
            _image = new Image { Stretch = Stretch.None };
            Children.Add(_image);

            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);

            Background = new SolidColorBrush(Color.FromRgb(24, 24, 37));
            Height = 32;
            ClipToBounds = true;
            Cursor = Cursors.Hand;

            MouseLeftButtonDown += OnMouse_Down;
            MouseMove += OnMouse_Move;
            MouseLeftButtonUp += OnMouse_Up;
            MouseLeave += OnMouse_Leave;
            SizeChanged += (s, e) => Redraw();
        }

        private static void OnPlayheadChanged(DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is PlayheadTimelineControl ctrl)
                ctrl.Redraw();
        }

        private static void OnLayoutChanged(DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is PlayheadTimelineControl ctrl)
                ctrl.Redraw();
        }

        // ---- Mouse handling ----

        private void OnMouse_Down(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            CaptureMouse();
            UpdateFromMouse(e);
        }

        private void OnMouse_Move(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
                UpdateFromMouse(e);
        }

        private void OnMouse_Up(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }

        private void OnMouse_Leave(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            // Keep dragging if button is held, release otherwise
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _isDragging = false;
                ReleaseMouseCapture();
            }
        }

        private void UpdateFromMouse(MouseEventArgs e)
        {
            double x = e.GetPosition(this).X;
            double width = ActualWidth;
            if (width <= 0) return;

            double normalized = Math.Clamp(x / width, 0, 1);
            PlayheadPosition = normalized;
            SeekRequested?.Invoke(normalized);
        }

        // ---- Rendering ----

        private unsafe void Redraw()
        {
            int width = (int)Math.Max(ActualWidth, 10);
            int height = (int)Math.Max(ActualHeight, 10);

            if (_bitmap == null ||
                _bitmap.PixelWidth != width ||
                _bitmap.PixelHeight != height)
            {
                _bitmap = new WriteableBitmap(width, height, 96, 96,
                    PixelFormats.Bgra32, null);
                _image.Source = _bitmap;
                _image.Width = width;
                _image.Height = height;
            }

            _bitmap.Lock();
            try
            {
                uint* px = (uint*)_bitmap.BackBuffer;
                int stride = _bitmap.BackBufferStride / 4;

                // Clear background
                int total = stride * height;
                for (int i = 0; i < total; i++)
                    px[i] = BgColor;

                // Bottom border line
                for (int x = 0; x < width; x++)
                    px[(height - 1) * stride + x] = TickMinor;

                // ---- Draw time markers ----
                double totalSec = TotalDuration.TotalSeconds;
                if (totalSec <= 0) totalSec = 30;

                // Determine tick interval based on zoom level
                double pixelsPerSecond = width / totalSec;
                double tickInterval;
                if (pixelsPerSecond > 100) tickInterval = 0.5;
                else if (pixelsPerSecond > 50) tickInterval = 1;
                else if (pixelsPerSecond > 20) tickInterval = 2;
                else if (pixelsPerSecond > 10) tickInterval = 5;
                else if (pixelsPerSecond > 4) tickInterval = 10;
                else tickInterval = 30;

                double majorInterval = tickInterval * 4;

                for (double sec = 0; sec <= totalSec; sec += tickInterval)
                {
                    int x = (int)(sec / totalSec * width);
                    if (x < 0 || x >= width) continue;

                    bool isMajor = Math.Abs(sec % majorInterval) < 0.001 ||
                                   sec < 0.001;
                    uint color = isMajor ? TickMajor : TickMinor;
                    int tickH = isMajor ? height - 4 : height / 2;

                    // Draw tick line
                    for (int y = height - tickH; y < height - 1; y++)
                        px[y * stride + x] = color;

                    // Draw time label for major ticks
                    if (isMajor)
                    {
                        // Simple text: draw a small marker dot at the top
                        // (Real text rendering would use FormattedText —
                        //  we keep it simple for the bitmap approach)
                        int dotY = 3;
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int px2 = x + dx;
                                int py2 = dotY + dy;
                                if (px2 >= 0 && px2 < width && py2 >= 0 && py2 < height)
                                    px[py2 * stride + px2] = TickText;
                            }
                    }
                }

                // ---- Draw beat markers (using BPM) ----
                double beatsPerSecond = Bpm / 60.0;
                double beatInterval = 1.0 / beatsPerSecond;

                if (pixelsPerSecond * beatInterval > 8) // Only if visible
                {
                    for (double sec = 0; sec <= totalSec; sec += beatInterval)
                    {
                        int x = (int)(sec / totalSec * width);
                        if (x < 0 || x >= width) continue;

                        bool isBar = Math.Abs((sec / beatInterval) % 4) < 0.01;
                        uint color = isBar ? TickMinor : Pack(40, 40, 58);
                        int tickH = isBar ? height / 3 : height / 5;

                        for (int y = height - tickH; y < height - 1; y++)
                        {
                            if (px[y * stride + x] == BgColor)
                                px[y * stride + x] = color;
                        }
                    }
                }

                // ---- Draw playhead ----
                int playheadX = (int)(PlayheadPosition * (width - 1));
                playheadX = Math.Clamp(playheadX, 0, width - 1);

                // Glow (3px wide)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int gx = playheadX + dx;
                    if (gx >= 0 && gx < width)
                    {
                        for (int y = 0; y < height; y++)
                            px[y * stride + gx] = PlayheadGlow;
                    }
                }

                // Center line (bright)
                for (int y = 0; y < height; y++)
                    px[y * stride + playheadX] = PlayheadCol;

                // Playhead triangle at top
                for (int dy = 0; dy < 6; dy++)
                {
                    int halfW = 6 - dy;
                    for (int dx = -halfW; dx <= halfW; dx++)
                    {
                        int tx = playheadX + dx;
                        if (tx >= 0 && tx < width && dy < height)
                            px[dy * stride + tx] = PlayheadCol;
                    }
                }

                _bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally
            {
                _bitmap.Unlock();
            }
        }

        // ---- Colour helpers ----

        private static uint Pack(byte r, byte g, byte b, byte a = 255)
        {
            unchecked
            {
                return (uint)b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24);
            }
        }
    }
}