using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaveCraft.Core.Midi;

namespace WaveCraft.Views.Controls
{
    /// <summary>
    /// MIDI clip visualization showing a mini piano-roll preview of notes.
    /// Renders colored blocks at note positions, similar to Ableton Live.
    /// </summary>
    public class MidiClipControl : Image
    {
        // Controls how many note rows can theoretically fit in the display
        private const int NOTE_HEIGHT_DIVISOR = 30;

        public static readonly DependencyProperty MidiClipProperty =
            DependencyProperty.Register(nameof(MidiClip), typeof(MidiClip),
                typeof(MidiClipControl),
                new PropertyMetadata(null, OnClipChanged));

        public static readonly DependencyProperty NoteColorProperty =
            DependencyProperty.Register(nameof(NoteColor), typeof(Color),
                typeof(MidiClipControl),
                new PropertyMetadata(Color.FromRgb(0x89, 0xB4, 0xFA), OnClipChanged));

        public static readonly DependencyProperty BackgroundColorProperty =
            DependencyProperty.Register(nameof(BackgroundColor), typeof(Color),
                typeof(MidiClipControl),
                new PropertyMetadata(Color.FromRgb(0x1E, 0x1E, 0x1E), OnClipChanged));

        public Core.Midi.MidiClip? MidiClip
        {
            get => (Core.Midi.MidiClip?)GetValue(MidiClipProperty);
            set => SetValue(MidiClipProperty, value);
        }

        public Color NoteColor
        {
            get => (Color)GetValue(NoteColorProperty);
            set => SetValue(NoteColorProperty, value);
        }

        public Color BackgroundColor
        {
            get => (Color)GetValue(BackgroundColorProperty);
            set => SetValue(BackgroundColorProperty, value);
        }

        private static void OnClipChanged(DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is MidiClipControl control)
                control.RedrawClip();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            RedrawClip();
        }

        private void RedrawClip()
        {
            var clip = MidiClip;
            if (clip == null || clip.Notes.Count == 0)
            {
                Source = null;
                return;
            }

            int width = Math.Max((int)ActualWidth, 100);
            int height = Math.Max((int)ActualHeight, 60);
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

                    // Fill background
                    uint bgPacked = PackColor(BackgroundColor);
                    uint notePacked = PackColor(NoteColor);

                    int totalPixels = stride * height;
                    for (int i = 0; i < totalPixels; i++)
                        pixels[i] = bgPacked;

                    // Find note range for vertical scaling
                    int minNote = 127;
                    int maxNote = 0;
                    foreach (var note in clip.Notes)
                    {
                        if (note.NoteNumber < minNote) minNote = note.NoteNumber;
                        if (note.NoteNumber > maxNote) maxNote = note.NoteNumber;
                    }

                    // Ensure we have at least some range (default to middle C area)
                    if (maxNote <= minNote)
                    {
                        minNote = 48; // C3
                        maxNote = 72; // C5
                    }
                    else
                    {
                        // Add some padding
                        int padding = 2;
                        minNote = Math.Max(0, minNote - padding);
                        maxNote = Math.Min(127, maxNote + padding);
                    }

                    int noteRange = maxNote - minNote;
                    if (noteRange == 0) noteRange = 1;

                    long clipLength = clip.LengthTicks;
                    if (clipLength == 0) clipLength = 1;

                    // Draw notes as rectangles
                    foreach (var note in clip.Notes)
                    {
                        // Calculate horizontal position and width
                        int x1 = (int)((note.StartTick * width) / clipLength);
                        int x2 = (int)(((note.StartTick + note.DurationTicks) * width) / clipLength);
                        
                        x1 = Math.Max(0, Math.Min(width - 1, x1));
                        x2 = Math.Max(x1 + 1, Math.Min(width, x2));

                        // Calculate vertical position (inverted - higher notes at top)
                        float notePos = (float)(note.NoteNumber - minNote) / noteRange;
                        int y = (int)((1f - notePos) * (height - 4)) + 2; // Leave 2px margin
                        int noteHeight = Math.Max(2, height / NOTE_HEIGHT_DIVISOR);

                        y = Math.Max(0, Math.Min(height - noteHeight, y));

                        // Draw the note rectangle
                        for (int py = y; py < y + noteHeight && py < height; py++)
                        {
                            for (int px = x1; px < x2 && px < width; px++)
                            {
                                pixels[py * stride + px] = notePacked;
                            }
                        }
                    }

                    bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                }
                finally
                {
                    bitmap.Unlock();
                }
            }

            Source = bitmap;
        }

        private static uint PackColor(Color color)
        {
            // Pack color for BGRA32 format: Blue in lowest byte, then Green, Red, Alpha
            return (uint)((color.B << 0) | (color.G << 8) | (color.R << 16) | (color.A << 24));
        }
    }
}
