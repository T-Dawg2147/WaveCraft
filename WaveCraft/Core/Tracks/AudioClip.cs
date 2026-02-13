using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Tracks
{
    /// <summary>
    /// Represents a segment of audio placed on a track's timeline.
    /// A clip references a source AudioBuffer and defines where it
    /// sits (start position) and how much of it plays (trim/length).
    ///
    /// PROFESSIONAL CONCEPT: Clips are non-destructive references.
    /// The source audio is never modified — clips just describe which
    /// portion plays at which time. Multiple clips can reference the
    /// same source buffer.
    /// </summary>
    public class AudioClip
    {
        /// <summary>Unique identifier for this clip.</summary>
        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>Display name.</summary>
        public string Name { get; set; } = "Untitled Clip";

        /// <summary>The source audio data.</summary>
        public AudioBuffer SourceBuffer { get; set; }

        /// <summary>
        /// Position on the timeline where this clip starts (in frames).
        /// Frame 0 = the very beginning of the project.
        /// </summary>
        public long StartFrame { get; set; }

        /// <summary>
        /// Offset into the source buffer where playback begins (for trimming).
        /// If TrimStartFrame = 100, the first 100 frames of the source are skipped.
        /// </summary>
        public long TrimStartFrame { get; set; }

        /// <summary>
        /// How many frames of the source to play.
        /// If 0, plays the entire source from TrimStartFrame to the end.
        /// </summary>
        public long DurationFrames { get; set; }

        /// <summary>Per-clip volume multiplier.</summary>
        public float Volume { get; set; } = 1.0f;

        /// <summary>The effective end frame on the timeline.</summary>
        public long EndFrame => StartFrame + EffectiveDuration;

        /// <summary>Actual number of frames this clip will produce.</summary>
        public long EffectiveDuration =>
            DurationFrames > 0
                ? DurationFrames
                : (SourceBuffer?.FrameCount ?? 0) - TrimStartFrame;

        /// <summary>
        /// Read samples from this clip into a destination buffer.
        /// The readStartFrame is relative to the project timeline.
        /// Uses unsafe pointer access for zero-overhead sample copying.
        /// </summary>
        public unsafe void ReadSamples(AudioBuffer destination,
            long readStartFrame, int framesToRead)
        {
            if (SourceBuffer == null) return;

            int channels = Math.Min(destination.Channels, SourceBuffer.Channels);
            long clipEnd = EndFrame;

            for (int f = 0; f < framesToRead; f++)
            {
                long projectFrame = readStartFrame + f;

                // Is this frame within the clip's range?
                if (projectFrame < StartFrame || projectFrame >= clipEnd)
                    continue;

                // Map project frame to source buffer frame
                long sourceFrame = TrimStartFrame + (projectFrame - StartFrame);
                if (sourceFrame < 0 || sourceFrame >= SourceBuffer.FrameCount)
                    continue;

                // Copy samples with volume applied
                float* srcFrame = SourceBuffer.GetFramePtr((int)sourceFrame);
                float* dstFrame = destination.GetFramePtr(f);

                for (int ch = 0; ch < channels; ch++)
                    dstFrame[ch] += srcFrame[ch] * Volume;
            }
        }
    }
}