using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Effects
{
    /// <summary>
    /// Applies a fade-in and/or fade-out envelope.
    /// </summary>
    public class FadeEffect : IAudioEffect
    {
        public string Name => "Fade";
        public bool IsEnabled { get; set; } = true;

        [AudioParameter("Fade In", 0f, 10000f, 0f, "ms")]
        public float FadeInMs { get; set; } = 0f;

        [AudioParameter("Fade Out", 0f, 10000f, 0f, "ms")]
        public float FadeOutMs { get; set; } = 0f;

        public unsafe void Process(AudioBuffer buffer, int sampleRate)
        {
            int frames = buffer.FrameCount;
            int channels = buffer.Channels;
            float* ptr = buffer.Ptr;

            int fadeInFrames = (int)(FadeInMs * sampleRate / 1000f);
            int fadeOutFrames = (int)(FadeOutMs * sampleRate / 1000f);

            for (int f = 0; f < frames; f++)
            {
                float envelope = 1.0f;

                // Fade in
                if (f < fadeInFrames && fadeInFrames > 0)
                    envelope *= (float)f / fadeInFrames;

                // Fade out
                int fadeOutStart = frames - fadeOutFrames;
                if (f >= fadeOutStart && fadeOutFrames > 0)
                    envelope *= (float)(frames - f) / fadeOutFrames;

                if (MathF.Abs(envelope - 1.0f) > 0.0001f)
                {
                    float* frame = ptr + f * channels;
                    for (int ch = 0; ch < channels; ch++)
                        frame[ch] *= envelope;
                }
            }
        }

        public void Reset() { }
        public void Dispose() { }
    }
}