using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Effects
{
    /// <summary>
    /// Simple volume adjustment. Demonstrates [AudioParameter] attribute.
    /// </summary>
    public class GainEffect : IAudioEffect
    {
        public string Name => "Gain";
        public bool IsEnabled { get; set; } = true;

        [AudioParameter("Volume", -60f, 12f, 0f, "dB", isLogarithmic: true)]
        public float GainDb { get; set; } = 0f;

        /// <summary>Convert dB to linear gain: 10^(dB/20).</summary>
        private float LinearGain => MathF.Pow(10f, GainDb / 20f);

        public unsafe void Process(AudioBuffer buffer, int sampleRate)
        {
            float gain = LinearGain;
            if (MathF.Abs(gain - 1.0f) < 0.0001f) return; // Unity — no-op

            float* ptr = buffer.Ptr;
            int total = buffer.TotalSamples;
            for (int i = 0; i < total; i++)
                ptr[i] *= gain;
        }

        public void Reset() { }
        public void Dispose() { }
    }
}