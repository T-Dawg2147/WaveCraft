using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Effects
{
    /// <summary>
    /// Dynamic range compressor — reduces the volume of loud signals.
    ///
    /// PROFESSIONAL CONCEPT: Compressors use an envelope follower to track
    /// the signal level, then apply gain reduction when the level exceeds
    /// the threshold. Attack and release control how quickly the compressor
    /// responds. This is one of the most important effects in audio production.
    /// </summary>
    public unsafe class CompressorEffect : IAudioEffect
    {
        public string Name => "Compressor";
        public bool IsEnabled { get; set; } = true;

        [AudioParameter("Threshold", -60f, 0f, -20f, "dB")]
        public float ThresholdDb { get; set; } = -20f;

        [AudioParameter("Ratio", 1f, 20f, 4f, ":1")]
        public float Ratio { get; set; } = 4f;

        [AudioParameter("Attack", 0.1f, 100f, 10f, "ms")]
        public float AttackMs { get; set; } = 10f;

        [AudioParameter("Release", 10f, 1000f, 100f, "ms")]
        public float ReleaseMs { get; set; } = 100f;

        [AudioParameter("Makeup Gain", 0f, 24f, 0f, "dB")]
        public float MakeupGainDb { get; set; } = 0f;

        // Envelope follower state — persists between Process() calls
        // so the compressor "remembers" where it was in the attack/release cycle.
        private float _envelope;

        public void Process(AudioBuffer buffer, int sampleRate)
        {
            float* ptr = buffer.Ptr;
            int frames = buffer.FrameCount;
            int channels = buffer.Channels;

            // Pre-compute coefficients from ms → per-sample smoothing factors
            // These are exponential decay coefficients: coeff = exp(-1 / (time * sampleRate))
            float attackCoeff = MathF.Exp(-1.0f / (AttackMs * 0.001f * sampleRate));
            float releaseCoeff = MathF.Exp(-1.0f / (ReleaseMs * 0.001f * sampleRate));

            float thresholdLinear = MathF.Pow(10f, ThresholdDb / 20f);
            float makeupLinear = MathF.Pow(10f, MakeupGainDb / 20f);
            float ratio = Ratio;

            for (int f = 0; f < frames; f++)
            {
                // Detect the peak level across all channels for this frame
                float peakLevel = 0f;
                float* frame = ptr + f * channels;
                for (int ch = 0; ch < channels; ch++)
                {
                    float abs = MathF.Abs(frame[ch]);
                    if (abs > peakLevel) peakLevel = abs;
                }

                // Envelope follower: smoothly tracks the signal level.
                // Uses a different speed for attack (level rising) vs
                // release (level falling). This prevents "pumping" artifacts.
                float coeff = peakLevel > _envelope ? attackCoeff : releaseCoeff;
                _envelope = coeff * _envelope + (1f - coeff) * peakLevel;

                // Compute gain reduction
                float gainReduction = 1.0f;
                if (_envelope > thresholdLinear && _envelope > 0.000001f)
                {
                    // How many dB above threshold?
                    float dbAbove = 20f * MathF.Log10(_envelope / thresholdLinear);

                    // Reduce by (1 - 1/ratio) of the overshoot
                    float dbReduction = dbAbove * (1f - 1f / ratio);

                    gainReduction = MathF.Pow(10f, -dbReduction / 20f);
                }

                // Apply gain reduction + makeup gain to all channels
                float totalGain = gainReduction * makeupLinear;
                for (int ch = 0; ch < channels; ch++)
                    frame[ch] *= totalGain;
            }
        }

        public void Reset()
        {
            _envelope = 0f;
        }

        public void Dispose() { }
    }
}