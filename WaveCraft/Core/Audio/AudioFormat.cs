namespace WaveCraft.Core.Audio
{
    /// <summary>
    /// Describes an audio stream's format.
    /// Immutable record — passed around the entire system without copying.
    /// </summary>
    public record AudioFormat(
        int SampleRate,
        int Channels,
        int BitsPerSample)
    {
        /// <summary>Bytes per single sample (one channel).</summary>
        public int BytesPerSample => BitsPerSample / 8;

        /// <summary>Bytes per frame (all channels for one time step).</summary>
        public int BytesPerFrame => BytesPerSample * Channels;

        /// <summary>Bytes per second of audio.</summary>
        public int ByteRate => SampleRate * BytesPerFrame;

        /// <summary>Convert a sample count to a TimeSpan.</summary>
        public TimeSpan SamplesToTime(long sampleCount)
            => TimeSpan.FromSeconds((double)sampleCount / SampleRate);

        /// <summary>Convert a TimeSpan to a sample position.</summary>
        public long TimeToSamples(TimeSpan time)
            => (long)(time.TotalSeconds * SampleRate);

        /// <summary>CD quality: 44100 Hz, stereo, 16-bit.</summary>
        public static readonly AudioFormat CdQuality = new(44100, 2, 16);

        /// <summary>Studio quality: 48000 Hz, stereo, 24-bit.</summary>
        public static readonly AudioFormat StudioQuality = new(48000, 2, 24);

        /// <summary>Internal processing format: 44100 Hz, stereo, 32-bit float.</summary>
        public static readonly AudioFormat InternalFloat = new(44100, 2, 32);
    }
}