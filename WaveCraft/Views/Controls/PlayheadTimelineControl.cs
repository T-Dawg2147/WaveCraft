using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WaveCraft.Views.Controls
{
    /// <summary>
    /// Bar/beat ruler timeline with Ableton Live styling.
    /// Click-to-seek with bar numbers display.
    /// </summary>
    public class PlayheadTimelineControl : Canvas
    {
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

        public event Action<double>? SeekRequested;

        private WriteableBitmap? _bitmap;
        private readonly Image _image;
        private bool _isDragging;

        private static readonly uint BgColor = Pack(0x1E, 0x1E, 0x1E);
        private static readonly uint BorderColor = Pack(0x44, 0x44, 0x44);
        private static readonly uint GridMinor = Pack(0x33, 0x33, 0x33);
        private static readonly uint GridMajor = Pack(0x44, 0x44, 0x44);
        private static readonly uint TextColor = Pack(0x99, 0x99, 0x99);
        private static readonly uint PlayheadCol = Pack(0xFF, 0x66, 0x00);

        public PlayheadTimelineControl()
        {
            _image = new Image { Stretch = Stretch.None };
            Children.Add(_image);

            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);

            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
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

                int total = stride * height;
                for (int i = 0; i < total; i++)
                    px[i] = BgColor;

                for (int x = 0; x < width; x++)
                    px[(height - 1) * stride + x] = BorderColor;

                double totalSec = TotalDuration.TotalSeconds;
                if (totalSec <= 0) totalSec = 30;

                double beatsPerSecond = Bpm / 60.0;
                double beatInterval = 1.0 / beatsPerSecond;
                double barInterval = beatInterval * 4;

                for (double sec = 0; sec <= totalSec; sec += beatInterval)
                {
                    int x = (int)(sec / totalSec * width);
                    if (x < 0 || x >= width) continue;

                    bool isBar = Math.Abs((sec / beatInterval) % 4) < 0.01;
                    uint color = isBar ? GridMajor : GridMinor;
                    int tickH = isBar ? height - 4 : height / 2;

                    for (int y = height - tickH; y < height - 1; y++)
                        px[y * stride + x] = color;

                    if (isBar)
                    {
                        int barNumber = (int)Math.Round(sec / barInterval) + 1;
                        int dotY = 3;
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int px2 = x + dx;
                                int py2 = dotY + dy;
                                if (px2 >= 0 && px2 < width && py2 >= 0 && py2 < height)
                                    px[py2 * stride + px2] = TextColor;
                            }
                    }
                }

                int playheadX = (int)(PlayheadPosition * (width - 1));
                playheadX = Math.Clamp(playheadX, 0, width - 1);

                for (int y = 0; y < height; y++)
                    px[y * stride + playheadX] = PlayheadCol;

                for (int dy = 0; dy < 5; dy++)
                {
                    int halfW = 5 - dy;
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

        private static uint Pack(byte r, byte g, byte b, byte a = 255)
        {
            unchecked
            {
                return (uint)b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24);
            }
        }
    }
}