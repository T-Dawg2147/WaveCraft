namespace WaveCraft.Core.Midi
{
    /// <summary>
    /// Represents a single MIDI note event.
    /// Immutable record — safe to pass across threads and store in collections.
    ///
    /// PROFESSIONAL CONCEPT: MIDI doesn't carry audio — it carries instructions
    /// (note on, note off, control changes). A synthesiser or VST plugin
    /// converts these instructions into actual sound.
    /// </summary>
    public record MidiNote
    {
        /// <summary>Unique ID for UI selection and editing.</summary>
        public Guid Id { get; init; } = Guid.NewGuid();

        /// <summary>MIDI note number (0–127). Middle C = 60.</summary>
        public int NoteNumber { get; init; }

        /// <summary>Velocity (0–127). How hard the key was pressed.</summary>
        public int Velocity { get; init; } = 100;

        /// <summary>Start position in ticks (480 ticks per beat is standard).</summary>
        public long StartTick { get; init; }

        /// <summary>Duration in ticks.</summary>
        public long DurationTicks { get; init; }

        /// <summary>MIDI channel (0–15).</summary>
        public int Channel { get; init; }

        /// <summary>End position in ticks.</summary>
        public long EndTick => StartTick + DurationTicks;

        /// <summary>
        /// Get the note name (C, C#, D, etc.) and octave.
        /// </summary>
        public string NoteName
        {
            get
            {
                string[] names = { "C", "C#", "D", "D#", "E", "F",
                                   "F#", "G", "G#", "A", "A#", "B" };
                int octave = (NoteNumber / 12) - 1;
                string name = names[NoteNumber % 12];
                return $"{name}{octave}";
            }
        }

        /// <summary>
        /// Convert MIDI note number to frequency in Hz.
        /// Uses the standard A4 = 440 Hz tuning.
        /// Formula: f = 440 × 2^((note - 69) / 12)
        /// </summary>
        public double FrequencyHz =>
            440.0 * Math.Pow(2.0, (NoteNumber - 69) / 12.0);
    }

    /// <summary>
    /// A MIDI control change event (knob/slider movement).
    /// </summary>
    public record MidiControlChange
    {
        public long Tick { get; init; }
        public int Channel { get; init; }
        public int Controller { get; init; }  // CC number (0–127)
        public int Value { get; init; }        // CC value (0–127)
    }

    /// <summary>
    /// A MIDI pitch bend event.
    /// </summary>
    public record MidiPitchBend
    {
        public long Tick { get; init; }
        public int Channel { get; init; }
        public int Value { get; init; }  // -8192 to 8191 (0 = center)
    }

    /// <summary>
    /// Tempo change event (for tempo maps / tempo automation).
    /// </summary>
    public record TempoChange(long Tick, float Bpm);

    /// <summary>
    /// Time signature change event.
    /// </summary>
    public record TimeSignatureChange(long Tick, int Numerator, int Denominator);

    /// <summary>
    /// Standard MIDI note constants.
    /// </summary>
    public static class MidiConstants
    {
        public const int TicksPerBeat = 480;  // Standard PPQN resolution
        public const int MinNote = 0;
        public const int MaxNote = 127;
        public const int MiddleC = 60;
        public const int DefaultVelocity = 100;

        // Common note durations in ticks
        public const int WholeNote = TicksPerBeat * 4;
        public const int HalfNote = TicksPerBeat * 2;
        public const int QuarterNote = TicksPerBeat;
        public const int EighthNote = TicksPerBeat / 2;
        public const int SixteenthNote = TicksPerBeat / 4;
        public const int ThirtySecondNote = TicksPerBeat / 8;

        /// <summary>
        /// Quantise a tick position to the nearest grid division.
        /// </summary>
        public static long Quantise(long tick, int gridSize)
        {
            if (gridSize <= 0) return tick;
            return (long)Math.Round((double)tick / gridSize) * gridSize;
        }

        /// <summary>
        /// Convert ticks to seconds given a BPM.
        /// </summary>
        public static double TicksToSeconds(long ticks, float bpm)
            => (double)ticks / TicksPerBeat * (60.0 / bpm);

        /// <summary>
        /// Convert seconds to ticks given a BPM.
        /// </summary>
        public static long SecondsToTicks(double seconds, float bpm)
            => (long)(seconds * bpm / 60.0 * TicksPerBeat);

        /// <summary>
        /// Convert ticks to audio frames given a BPM and sample rate.
        /// </summary>
        public static long TicksToFrames(long ticks, float bpm, int sampleRate)
            => (long)(TicksToSeconds(ticks, bpm) * sampleRate);
    }
}