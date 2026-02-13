using WaveCraft.Core.Audio;
using WaveCraft.Core.Effects;

namespace WaveCraft.Core.Tracks
{
    /// <summary>
    /// A single track in the DAW — contains clips, an effect chain,
    /// volume/pan controls, and solo/mute state.
    ///
    /// PROFESSIONAL CONCEPT: Each track is an independent processing
    /// pipeline. During rendering, the track reads all its clips into
    /// a buffer, processes it through its effect chain, then passes
    /// the result to the mixer.
    /// </summary>
    public class AudioTrack : IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; set; } = "Track";
        public float Volume { get; set; } = 1.0f;
        public float Pan { get; set; } = 0.0f; // -1 = left, 0 = center, 1 = right
        public bool IsMuted { get; set; }
        public bool IsSoloed { get; set; }
        public bool IsArmed { get; set; } // Ready for recording

        public List<AudioClip> Clips { get; } = new();
        public EffectChain Effects { get; } = new();

        // Reusable buffer for rendering — allocated once, reused every frame
        private AudioBuffer? _renderBuffer;

        /// <summary>
        /// Render this track's audio for the given frame range.
        /// Returns a buffer containing the mixed, effect-processed output.
        ///
        /// This is called from the audio thread and must be allocation-free
        /// (except for the first call which creates the reusable buffer).
        /// </summary>
        public unsafe AudioBuffer Render(long startFrame, int frameCount,
            int channels, int sampleRate, bool hasSoloedTrack)
        {
            // Should this track produce output?
            bool shouldPlay = !IsMuted && (!hasSoloedTrack || IsSoloed);

            // Ensure we have a render buffer of the right size
            if (_renderBuffer == null ||
                _renderBuffer.FrameCount != frameCount ||
                _renderBuffer.Channels != channels)
            {
                _renderBuffer?.Dispose();
                _renderBuffer = new AudioBuffer(frameCount, channels);
            }

            _renderBuffer.Clear();

            if (!shouldPlay) return _renderBuffer;

            // Read all clips into the buffer
            foreach (var clip in Clips)
            {
                clip.ReadSamples(_renderBuffer, startFrame, frameCount);
            }

            // Process through effect chain
            Effects.Process(_renderBuffer, sampleRate);

            // Apply track volume
            if (MathF.Abs(Volume - 1.0f) > 0.0001f)
                _renderBuffer.ApplyGain(Volume);

            // Apply pan (constant-power pan law)
            if (channels == 2 && MathF.Abs(Pan) > 0.001f)
            {
                // Constant-power panning:
                //   leftGain  = cos(pan * π/4 + π/4)
                //   rightGain = sin(pan * π/4 + π/4)
                // This maintains equal perceived loudness across the stereo field.
                float angle = (Pan + 1f) * MathF.PI / 4f;
                float leftGain = MathF.Cos(angle);
                float rightGain = MathF.Sin(angle);

                float* ptr = _renderBuffer.Ptr;
                int total = frameCount * 2;
                for (int i = 0; i < total; i += 2)
                {
                    ptr[i] *= leftGain;
                    ptr[i + 1] *= rightGain;
                }
            }

            return _renderBuffer;
        }

        /// <summary>
        /// Get the total duration of all clips on this track (in frames).
        /// </summary>
        public long GetTotalDurationFrames()
        {
            long maxEnd = 0;
            foreach (var clip in Clips)
            {
                if (clip.EndFrame > maxEnd)
                    maxEnd = clip.EndFrame;
            }
            return maxEnd;
        }

        public void Dispose()
        {
            _renderBuffer?.Dispose();
            Effects.Dispose();
            foreach (var clip in Clips)
                clip.SourceBuffer?.Dispose();
        }
    }
}