using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaveCraft.Core.Midi;
using WaveCraft.ViewModels;

namespace WaveCraft.Views.Controls
{
    /// <summary>
    /// Custom WPF control for the piano roll MIDI editor.
    /// Renders the note grid and notes using WriteableBitmap + unsafe
    /// pointer access for smooth scrolling and zooming.
    /// </summary>
    public class PianoRollControl : Canvas
    {
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(nameof(ViewModel), typeof(PianoRollViewModel),
                typeof(PianoRollControl),
                new PropertyMetadata(null, OnViewModelChanged));

        public PianoRollViewModel? ViewModel
        {
            get => (PianoRollViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        private WriteableBitmap? _gridBitmap;
        private readonly Image _gridImage;
        private const int PianoKeyWidth = 60;
        private const int NoteHeight = 16;
        private const double PixelsPerTick = 0.15;

        // Dragging state
        private bool _isDragging;
        private Guid _dragNoteId;
        private Point _dragStartPos;
        private long _dragOriginalTick;
        private int _dragOriginalNote;
        private bool _isResizing;

        // Colour palette (pre-packed for unsafe pixel writes)
        private static readonly uint BgBlack = PackColor(24, 24, 36);
        private static readonly uint BgWhite = PackColor(30, 30, 46);
        private static readonly uint GridLine = PackColor(45, 45, 68);
        private static readonly uint GridBeat = PackColor(60, 60, 90);
        private static readonly uint GridBar = PackColor(80, 80, 110);
        private static readonly uint NoteColor = PackColor(137, 180, 250);
        private static readonly uint NoteSelectedColor = PackColor(250, 179, 135);
        private static readonly uint NoteOutline = PackColor(100, 100, 140);
        private static readonly uint PianoWhiteKey = PackColor(205, 214, 244);
        private static readonly uint PianoBlackKey = PackColor(69, 71, 90);
        private static readonly uint PianoKeyBorder = PackColor(45, 45, 68);
        private static readonly uint PianoKeyLabel = PackColor(120, 120, 150);
        private static readonly uint PlayheadColor = PackColor(166, 227, 161);
        private static readonly uint VelocityBarColor = PackColor(250, 179, 135);

        private static readonly bool[] IsBlackKey =
            { false, true, false, true, false, false, true, false, true, false, true, false };

        public PianoRollControl()
        {
            _gridImage = new Image { Stretch = Stretch.None };
            Children.Add(_gridImage);

            RenderOptions.SetBitmapScalingMode(_gridImage, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(_gridImage, EdgeMode.Aliased);

            Background = new SolidColorBrush(Color.FromRgb(24, 24, 36));
            ClipToBounds = true;

            MouseLeftButtonDown += OnPianoRoll_MouseDown;
            MouseRightButtonDown += OnPianoRoll_MouseDown;
            MouseMove += OnPianoRoll_MouseMove;
            MouseLeftButtonUp += OnPianoRoll_MouseUp;
            MouseRightButtonUp += OnPianoRoll_MouseUp;
            MouseWheel += OnPianoRoll_MouseWheel;
            SizeChanged += (s, e) => RedrawAll();
        }

        private static void OnViewModelChanged(DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is PianoRollControl control)
            {
                if (e.NewValue is PianoRollViewModel vm)
                {
                    vm.PropertyChanged += (s, args) => control.RedrawAll();
                    vm.Notes.CollectionChanged += (s, args) => control.RedrawAll();
                }
                control.RedrawAll();
            }
        }

        // ---- Coordinate conversion ----

        private double GetPixelsPerTick()
            => PixelsPerTick * (ViewModel?.HorizontalZoom ?? 1.0);

        private (long tick, int noteNumber) PixelToNote(Point pos)
        {
            double ppt = GetPixelsPerTick();
            long tick = (long)((pos.X - PianoKeyWidth) / ppt);
            int row = (int)(pos.Y / NoteHeight);
            int noteNumber = 127 - row;
            return (Math.Max(0, tick), Math.Clamp(noteNumber, 0, 127));
        }

        private (int x, int y, int width) NoteToPixel(long startTick, long duration, int noteNumber)
        {
            double ppt = GetPixelsPerTick();
            int x = PianoKeyWidth + (int)(startTick * ppt);
            int row = 127 - noteNumber;
            int y = row * NoteHeight;
            int w = Math.Max(3, (int)(duration * ppt));
            return (x, y, w);
        }

        /// <summary>
        /// Find which note (if any) is at the given pixel position.
        /// </summary>
        private MidiNoteViewModel? HitTestNote(Point pos)
        {
            if (ViewModel?.Clip == null) return null;

            var (tick, noteNum) = PixelToNote(pos);

            foreach (var noteVm in ViewModel.Notes)
            {
                if (noteVm.NoteNumber == noteNum &&
                    tick >= noteVm.StartTick &&
                    tick < noteVm.EndTick)
                {
                    return noteVm;
                }
            }
            return null;
        }

        /// <summary>
        /// Check if the mouse is near the right edge of a note (for resizing).
        /// </summary>
        private bool IsNearNoteEdge(Point pos, MidiNoteViewModel note)
        {
            var (x, y, w) = NoteToPixel(note.StartTick, note.DurationTicks, note.NoteNumber);
            int rightEdge = x + w;
            return Math.Abs(pos.X - rightEdge) < 6;
        }

        // ---- Mouse handling ----

        private void OnPianoRoll_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null) return;

            Point pos = e.GetPosition(this);
            bool isRight = e.ChangedButton == MouseButton.Right;

            // Ignore clicks on the piano key area
            if (pos.X < PianoKeyWidth) return;

            var hitNote = HitTestNote(pos);
            var (tick, noteNum) = PixelToNote(pos);

            switch (ViewModel.CurrentTool)
            {
                case PianoRollTool.Draw:
                    if (hitNote != null)
                    {
                        // Start dragging existing note
                        if (IsNearNoteEdge(pos, hitNote))
                        {
                            _isResizing = true;
                            _dragNoteId = hitNote.Id;
                        }
                        else
                        {
                            _isDragging = true;
                            _dragNoteId = hitNote.Id;
                            _dragStartPos = pos;
                            _dragOriginalTick = hitNote.StartTick;
                            _dragOriginalNote = hitNote.NoteNumber;
                        }

                        if (!ViewModel.IsNoteSelected(hitNote.Id))
                        {
                            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                                ViewModel.SetToolCommand.Execute("Select");

                            ViewModel.SetNoteSelected(hitNote.Id, true);
                            ViewModel.CurrentTool = PianoRollTool.Draw;
                        }
                    }
                    else
                    {
                        // Draw new note
                        ViewModel.DrawNoteAt(tick, noteNum);
                    }
                    break;

                case PianoRollTool.Select:
                    if (hitNote != null)
                    {
                        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                            ViewModel.ToggleNoteSelection(hitNote.Id);
                        else
                        {
                            if (!ViewModel.IsNoteSelected(hitNote.Id))
                            {
                                // Deselect all, select this one
                                foreach (var n in ViewModel.Notes)
                                    ViewModel.SetNoteSelected(n.Id, false);
                            }
                            ViewModel.SetNoteSelected(hitNote.Id, true);
                        }

                        // Start drag
                        if (IsNearNoteEdge(pos, hitNote))
                        {
                            _isResizing = true;
                            _dragNoteId = hitNote.Id;
                        }
                        else
                        {
                            _isDragging = true;
                            _dragNoteId = hitNote.Id;
                            _dragStartPos = pos;
                            _dragOriginalTick = hitNote.StartTick;
                            _dragOriginalNote = hitNote.NoteNumber;
                        }
                    }
                    else
                    {
                        // Click on empty space — deselect all
                        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        {
                            foreach (var n in ViewModel.Notes)
                                ViewModel.SetNoteSelected(n.Id, false);
                        }
                    }
                    break;

                case PianoRollTool.Erase:
                    if (hitNote != null)
                        ViewModel.EraseNote(hitNote.Id);
                    break;

                case PianoRollTool.Velocity:
                    if (hitNote != null)
                    {
                        // Velocity is set based on vertical position within the note row
                        int row = 127 - hitNote.NoteNumber;
                        int rowY = row * NoteHeight;
                        float t = 1f - (float)(pos.Y - rowY) / NoteHeight;
                        int vel = Math.Clamp((int)(t * 127), 1, 127);
                        ViewModel.SetNoteVelocity(hitNote.Id, vel);
                    }
                    break;
            }

            CaptureMouse();
            RedrawAll();
        }

        private void OnPianoRoll_MouseMove(object sender, MouseEventArgs e)
        {
            if (ViewModel == null) return;

            Point pos = e.GetPosition(this);
            var (tick, noteNum) = PixelToNote(pos);

            // Update cursor based on context
            var hitNote = HitTestNote(pos);
            if (hitNote != null && IsNearNoteEdge(pos, hitNote))
                Cursor = Cursors.SizeWE;
            else if (hitNote != null)
                Cursor = Cursors.Hand;
            else
                Cursor = Cursors.Cross;

            if (e.LeftButton != MouseButtonState.Pressed) return;

            if (_isResizing && ViewModel.Clip != null)
            {
                // Resize note
                var note = ViewModel.Clip.Notes.Find(n => n.Id == _dragNoteId);
                if (note != null)
                {
                    long newDuration = tick - note.StartTick;
                    ViewModel.ResizeNote(_dragNoteId, newDuration);
                    RedrawAll();
                }
            }
            else if (_isDragging)
            {
                // Move note
                double dx = pos.X - _dragStartPos.X;
                double dy = pos.Y - _dragStartPos.Y;

                double ppt = GetPixelsPerTick();
                long tickOffset = (long)(dx / ppt);
                int noteOffset = -(int)(dy / NoteHeight);

                long newTick = _dragOriginalTick + tickOffset;
                int newNote = _dragOriginalNote + noteOffset;

                ViewModel.MoveNote(_dragNoteId, newTick, newNote);
                RedrawAll();
            }
            else if (ViewModel.CurrentTool == PianoRollTool.Erase)
            {
                // Continuous erase while dragging
                if (hitNote != null)
                {
                    ViewModel.EraseNote(hitNote.Id);
                    RedrawAll();
                }
            }
            else if (ViewModel.CurrentTool == PianoRollTool.Draw)
            {
                // Continuous drawing while dragging
                if (hitNote == null && pos.X > PianoKeyWidth)
                {
                    ViewModel.DrawNoteAt(tick, noteNum);
                    RedrawAll();
                }
            }
        }

        private void OnPianoRoll_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            _isResizing = false;
            _dragNoteId = Guid.Empty;
            ReleaseMouseCapture();
        }

        private void OnPianoRoll_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ViewModel == null) return;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // Ctrl+Scroll = horizontal zoom
                double delta = e.Delta > 0 ? 1.15 : 0.87;
                ViewModel.HorizontalZoom *= delta;
                e.Handled = true;
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Shift+Scroll = vertical zoom
                double delta = e.Delta > 0 ? 1.1 : 0.91;
                ViewModel.VerticalZoom *= delta;
                e.Handled = true;
            }

            RedrawAll();
        }

        // ---- Rendering ----

        private unsafe void RedrawAll()
        {
            int width = (int)Math.Max(ActualWidth, 100);
            int height = (int)Math.Max(ActualHeight, 100);

            if (_gridBitmap == null ||
                _gridBitmap.PixelWidth != width ||
                _gridBitmap.PixelHeight != height)
            {
                _gridBitmap = new WriteableBitmap(width, height, 96, 96,
                    PixelFormats.Bgra32, null);
                _gridImage.Source = _gridBitmap;
                _gridImage.Width = width;
                _gridImage.Height = height;
            }

            _gridBitmap.Lock();
            try
            {
                uint* pixels = (uint*)_gridBitmap.BackBuffer;
                int stride = _gridBitmap.BackBufferStride / 4;

                // Clear
                int totalPixels = stride * height;
                for (int i = 0; i < totalPixels; i++)
                    pixels[i] = BgBlack;

                double ppt = GetPixelsPerTick();

                // ---- Draw row backgrounds (black/white key rows) ----
                for (int noteNum = 0; noteNum <= 127; noteNum++)
                {
                    int row = 127 - noteNum;
                    int y = row * NoteHeight;
                    if (y >= height) continue;
                    if (y + NoteHeight < 0) continue;

                    bool isBlack = IsBlackKey[noteNum % 12];
                    uint bgColor = isBlack ? BgBlack : BgWhite;

                    int y0 = Math.Max(0, y);
                    int y1 = Math.Min(height, y + NoteHeight);

                    for (int py = y0; py < y1; py++)
                        for (int px = PianoKeyWidth; px < width; px++)
                            pixels[py * stride + px] = bgColor;

                    // Row separator
                    if (y >= 0 && y < height)
                        for (int px = PianoKeyWidth; px < width; px++)
                            pixels[y * stride + px] = GridLine;

                    // Highlight C notes
                    if (noteNum % 12 == 0 && y >= 0 && y < height)
                        for (int px = PianoKeyWidth; px < width; px++)
                            pixels[y * stride + px] = GridBeat;
                }

                // ---- Draw vertical grid lines ----
                int gridDiv = ViewModel?.GridDivision ?? MidiConstants.EighthNote;
                int ticksVisible = (int)(width / ppt) + gridDiv;

                for (long tick = 0; tick < ticksVisible; tick += gridDiv)
                {
                    int x = PianoKeyWidth + (int)(tick * ppt);
                    if (x < PianoKeyWidth || x >= width) continue;

                    uint lineColor;
                    if (tick % (MidiConstants.QuarterNote * 4) == 0)
                        lineColor = GridBar;
                    else if (tick % MidiConstants.QuarterNote == 0)
                        lineColor = GridBeat;
                    else
                        lineColor = GridLine;

                    for (int py = 0; py < height; py++)
                        pixels[py * stride + x] = lineColor;
                }

                // ---- Draw notes ----
                if (ViewModel?.Clip != null)
                {
                    foreach (var note in ViewModel.Clip.Notes)
                    {
                        int row = 127 - note.NoteNumber;
                        int y = row * NoteHeight + 1;
                        int h = NoteHeight - 2;
                        int x = PianoKeyWidth + (int)(note.StartTick * ppt);
                        int w = Math.Max(4, (int)(note.DurationTicks * ppt));

                        bool selected = ViewModel.IsNoteSelected(note.Id);
                        uint baseColor = selected ? NoteSelectedColor : NoteColor;

                        // Velocity brightness
                        float velScale = 0.4f + (note.Velocity / 127f) * 0.6f;
                        uint fillColor = ScaleColor(baseColor, velScale);

                        // Clamp to visible area
                        int x0 = Math.Max(PianoKeyWidth, x);
                        int x1 = Math.Min(width, x + w);
                        int y0 = Math.Max(0, y);
                        int y1 = Math.Min(height, y + h);

                        if (x0 >= x1 || y0 >= y1) continue;

                        // Fill
                        for (int py = y0; py < y1; py++)
                            for (int px = x0; px < x1; px++)
                                pixels[py * stride + px] = fillColor;

                        // Outline
                        for (int px = x0; px < x1; px++)
                        {
                            if (y0 < height) pixels[y0 * stride + px] = NoteOutline;
                            if (y1 - 1 >= 0 && y1 - 1 < height) pixels[(y1 - 1) * stride + px] = NoteOutline;
                        }
                        for (int py = y0; py < y1; py++)
                        {
                            if (x0 < width) pixels[py * stride + x0] = NoteOutline;
                            if (x1 - 1 >= 0 && x1 - 1 < width) pixels[py * stride + x1 - 1] = NoteOutline;
                        }

                        // Velocity bar at bottom of note
                        int velBarWidth = (int)(w * (note.Velocity / 127f));
                        int velY = y + h - 3;
                        if (velY >= 0 && velY < height)
                        {
                            int vx1 = Math.Min(width, x + velBarWidth);
                            for (int px = Math.Max(PianoKeyWidth, x); px < vx1; px++)
                                pixels[velY * stride + px] = VelocityBarColor;
                        }
                    }
                }

                // ---- Draw piano keys ----
                for (int noteNum = 0; noteNum <= 127; noteNum++)
                {
                    int row = 127 - noteNum;
                    int y = row * NoteHeight;
                    if (y >= height) continue;
                    if (y + NoteHeight < 0) continue;

                    bool isBlack = IsBlackKey[noteNum % 12];
                    uint keyColor = isBlack ? PianoBlackKey : PianoWhiteKey;
                    int keyW = isBlack ? PianoKeyWidth - 15 : PianoKeyWidth;

                    int y0 = Math.Max(0, y + 1);
                    int y1 = Math.Min(height, y + NoteHeight - 1);

                    // Key fill
                    for (int py = y0; py < y1; py++)
                        for (int px = 0; px < keyW; px++)
                            pixels[py * stride + px] = keyColor;

                    // Key border (bottom)
                    if (y + NoteHeight - 1 >= 0 && y + NoteHeight - 1 < height)
                        for (int px = 0; px < PianoKeyWidth; px++)
                            pixels[(y + NoteHeight - 1) * stride + px] = PianoKeyBorder;

                    // Right edge of piano area
                    for (int py = y0; py < y1; py++)
                        if (PianoKeyWidth - 1 < width)
                            pixels[py * stride + PianoKeyWidth - 1] = PianoKeyBorder;
                }

                _gridBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally
            {
                _gridBitmap.Unlock();
            }

            _gridImage.Source = _gridBitmap;
        }

        // ---- Colour helpers ----

        private static uint PackColor(byte r, byte g, byte b)
        {
            unchecked
            {
                return (uint)b | ((uint)g << 8) | ((uint)r << 16) | (0xFFu << 24);
            }
        }

        private static uint ScaleColor(uint packed, float scale)
        {
            unchecked
            {
                byte b = (byte)((packed & 0xFF) * scale);
                byte g = (byte)(((packed >> 8) & 0xFF) * scale);
                byte r = (byte)(((packed >> 16) & 0xFF) * scale);
                return (uint)b | ((uint)g << 8) | ((uint)r << 16) | (0xFFu << 24);
            }
        }
    }
}