using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Effects
{
    /// <summary>
    /// Noise gate — silences audio below a threshold level.
    /// Used to remove background noise, hum, or bleed between takes.
    ///
    /// PROFESSIONAL CONCEPT: Like the compressor, this uses an envelope
    /// follower, but instead of reducing gain proportionally, it either
    /// passes audio (gate open) or silences it (gate closed). The attack
    /// and release prevent harsh clicking at the gate transitions.
    /// </summary>
    public unsafe class NoiseGateEffect : IAudioEffect
    {
        public string Name => "Noise Gate";
        public bool IsEnabled { get; set; } = true;

        [AudioParameter("Threshold", -80f, 0f, -40f, "dB")]
        public float ThresholdDb { get; set; } = -40f;

        [AudioParameter("Attack", 0.1f, 50f, 1f, "ms")]
        public float AttackMs { get; set; } = 1f;

        [AudioParameter("Release", 5f, 500f, 50f, "ms")]
        public float ReleaseMs { get; set; } = 50f;

        [AudioParameter("Hold", 0f, 500f, 10f, "ms")]
        public float HoldMs { get; set; } = 10f;

        [AudioParameter("Range", -80f, 0f, -80f, "dB")]
        public float RangeDb { get; set; } = -80f;

        private float _envelope;
        private float _gateGain;       // Current gate gain (0 to 1)
        private int _holdCounter;    // Frames remaining in hold phase

        public void Process(AudioBuffer buffer, int sampleRate)
        {
            float* ptr = buffer.Ptr;
            int frames = buffer.FrameCount;
            int channels = buffer.Channels;

            float attackCoeff = MathF.Exp(-1.0f / (AttackMs * 0.001f * sampleRate));
            float releaseCoeff = MathF.Exp(-1.0f / (ReleaseMs * 0.001f * sampleRate));

            float thresholdLinear = MathF.Pow(10f, ThresholdDb / 20f);
            float rangeLinear = MathF.Pow(10f, RangeDb / 20f);
            int holdFrames = (int)(HoldMs * 0.001f * sampleRate);

            for (int f = 0; f < frames; f++)
            {
                // Peak detection across channels
                float peakLevel = 0f;
                float* frame = ptr + f * channels;
                for (int ch = 0; ch < channels; ch++)
                {
                    float abs = MathF.Abs(frame[ch]);
                    if (abs > peakLevel) peakLevel = abs;
                }

                // Envelope follower
                float envCoeff = peakLevel > _envelope ? attackCoeff : releaseCoeff;
                _envelope = envCoeff * _envelope + (1f - envCoeff) * peakLevel;

                // Gate logic with hold phase
                float targetGain;
                if (_envelope >= thresholdLinear)
                {
                    // Gate OPEN
                    targetGain = 1.0f;
                    _holdCounter = holdFrames;
                }
                else if (_holdCounter > 0)
                {
                    // HOLD phase — gate stays open briefly after signal drops
                    targetGain = 1.0f;
                    _holdCounter--;
                }
                else
                {
                    // Gate CLOSED — attenuate to range level
                    targetGain = rangeLinear;
                }

                // Smooth the gate gain to prevent clicking
                float smoothCoeff = targetGain > _gateGain ? 0.999f : 0.995f;
                _gateGain = smoothCoeff * _gateGain + (1f - smoothCoeff) * targetGain;

                // Apply gate
                for (int ch = 0; ch < channels; ch++)
                    frame[ch] *= _gateGain;
            }
        }

        public void Reset()
        {
            _envelope = 0f;
            _gateGain = 0f;
            _holdCounter = 0;
        }

        public void Dispose() { }
    }
}