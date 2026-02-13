using System.Runtime.InteropServices;
using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Effects
{
    /// <summary>
    /// 3-band parametric EQ using biquad filters.
    ///
    /// PROFESSIONAL CONCEPT: The biquad filter is the building block of
    /// nearly all digital EQs. Each band uses 5 coefficients (a0,a1,a2,b0,b1,b2)
    /// computed from frequency, gain, and Q parameters. The filter processes
    /// samples using a simple difference equation — extremely efficient.
    /// </summary>
    public unsafe class EqualizerEffect : IAudioEffect
    {
        public string Name => "3-Band EQ";
        public bool IsEnabled { get; set; } = true;

        [AudioParameter("Low Gain", -12f, 12f, 0f, "dB")]
        public float LowGainDb { get; set; } = 0f;

        [AudioParameter("Low Freq", 20f, 500f, 100f, "Hz", isLogarithmic: true)]
        public float LowFreq { get; set; } = 100f;

        [AudioParameter("Mid Gain", -12f, 12f, 0f, "dB")]
        public float MidGainDb { get; set; } = 0f;

        [AudioParameter("Mid Freq", 200f, 5000f, 1000f, "Hz", isLogarithmic: true)]
        public float MidFreq { get; set; } = 1000f;

        [AudioParameter("High Gain", -12f, 12f, 0f, "dB")]
        public float HighGainDb { get; set; } = 0f;

        [AudioParameter("High Freq", 2000f, 20000f, 8000f, "Hz", isLogarithmic: true)]
        public float HighFreq { get; set; } = 8000f;

        // Biquad filter state (per band, per channel)
        // state[band][channel] = { x1, x2, y1, y2 }
        private float[,,] _state = new float[3, 2, 4];

        public void Process(AudioBuffer buffer, int sampleRate)
        {
            float* ptr = buffer.Ptr;
            int frames = buffer.FrameCount;
            int channels = buffer.Channels;

            // Process each band
            ProcessBand(ptr, frames, channels, sampleRate, 0, LowFreq, LowGainDb, 0.707f);
            ProcessBand(ptr, frames, channels, sampleRate, 1, MidFreq, MidGainDb, 1.0f);
            ProcessBand(ptr, frames, channels, sampleRate, 2, HighFreq, HighGainDb, 0.707f);
        }

        private void ProcessBand(float* samples, int frames, int channels,
            int sampleRate, int bandIndex, float freq, float gainDb, float q)
        {
            if (MathF.Abs(gainDb) < 0.1f) return; // No gain change — skip

            // Compute biquad coefficients for a peaking EQ filter
            float A = MathF.Pow(10f, gainDb / 40f);
            float w0 = 2f * MathF.PI * freq / sampleRate;
            float sinW0 = MathF.Sin(w0);
            float cosW0 = MathF.Cos(w0);
            float alpha = sinW0 / (2f * q);

            float b0 = 1f + alpha * A;
            float b1 = -2f * cosW0;
            float b2 = 1f - alpha * A;
            float a0 = 1f + alpha / A;
            float a1 = -2f * cosW0;
            float a2 = 1f - alpha / A;

            // Normalise
            b0 /= a0; b1 /= a0; b2 /= a0;
            a1 /= a0; a2 /= a0;

            for (int ch = 0; ch < Math.Min(channels, 2); ch++)
            {
                float x1 = _state[bandIndex, ch, 0];
                float x2 = _state[bandIndex, ch, 1];
                float y1 = _state[bandIndex, ch, 2];
                float y2 = _state[bandIndex, ch, 3];

                for (int f = 0; f < frames; f++)
                {
                    int idx = f * channels + ch;
                    float x0 = samples[idx];

                    // Biquad difference equation
                    float y0 = b0 * x0 + b1 * x1 + b2 * x2
                                        - a1 * y1 - a2 * y2;

                    x2 = x1; x1 = x0;
                    y2 = y1; y1 = y0;

                    samples[idx] = y0;
                }

                _state[bandIndex, ch, 0] = x1;
                _state[bandIndex, ch, 1] = x2;
                _state[bandIndex, ch, 2] = y1;
                _state[bandIndex, ch, 3] = y2;
            }
        }

        public void Reset()
        {
            Array.Clear(_state);
        }

        public void Dispose() { }
    }
}