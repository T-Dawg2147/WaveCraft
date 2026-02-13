using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Effects
{
    /// <summary>
    /// Interface for all audio effects. Every effect processes a buffer
    /// of audio samples in-place.
    ///
    /// PROFESSIONAL CONCEPT: The Strategy pattern — effects are interchangeable.
    /// The EffectChain doesn't know or care what each effect does;
    /// it just calls Process() on each one in order.
    /// </summary>
    public interface IAudioEffect : IDisposable
    {
        /// <summary>Human-readable name for the UI.</summary>
        string Name { get; }

        /// <summary>Whether the effect is active (bypass if false).</summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// Process audio samples in-place. This runs on the audio thread
        /// and must be real-time safe (no allocations, no locks, no I/O).
        /// </summary>
        void Process(AudioBuffer buffer, int sampleRate);

        /// <summary>
        /// Reset any internal state (e.g., delay lines, filter history).
        /// Called when playback is stopped or seeked.
        /// </summary>
        void Reset();
    }
}