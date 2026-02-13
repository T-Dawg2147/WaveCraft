using WaveCraft.Core.Audio;
using WaveCraft.Core.Tracks;

namespace WaveCraft.Core.Project
{
    /// <summary>
    /// Represents an entire DAW project — tracks, clips, settings.
    /// </summary>
    public class DawProject
    {
        public string Name { get; set; } = "Untitled Project";
        public string? FilePath { get; set; }
        public int SampleRate { get; set; } = 44100;
        public int Channels { get; set; } = 2;
        public float Bpm { get; set; } = 120f;
        public int TimeSignatureNumerator { get; set; } = 4;
        public int TimeSignatureDenominator { get; set; } = 4;

        public TrackMixer Mixer { get; } = new();

        public AudioFormat Format => new(SampleRate, Channels, 32);

        /// <summary>
        /// Total duration of the project in frames.
        /// </summary>
        public long TotalFrames => Mixer.GetTotalDurationFrames();

        /// <summary>
        /// Total duration as a TimeSpan.
        /// </summary>
        public TimeSpan TotalDuration =>
            TimeSpan.FromSeconds((double)TotalFrames / SampleRate);

        /// <summary>
        /// Convert a frame position to a bar:beat:tick string.
        /// </summary>
        public string FrameToBarBeatTick(long frame)
        {
            double seconds = (double)frame / SampleRate;
            double beatsTotal = seconds * Bpm / 60.0;
            int bar = (int)(beatsTotal / TimeSignatureNumerator) + 1;
            int beat = (int)(beatsTotal % TimeSignatureNumerator) + 1;
            int tick = (int)((beatsTotal % 1.0) * 480); // 480 ticks per beat (standard MIDI)

            return $"{bar}:{beat:D2}:{tick:D3}";
        }

        public void Dispose()
        {
            Mixer.Dispose();
        }
    }
}