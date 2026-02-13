using WaveCraft.Core.Audio;
using WaveCraft.Core.Effects;
using WaveCraft.Core.Midi;

namespace WaveCraft.Core.Tracks
{
    /// <summary>
    /// A MIDI track — contains MIDI clips and a synthesiser (or VST plugin)
    /// that converts MIDI events to audio during rendering.
    ///
    /// PROFESSIONAL CONCEPT: MIDI tracks don't contain audio data.
    /// Instead, they contain note/event data and route it through a
    /// "virtual instrument" (synth or VST) which generates the audio
    /// in real-time. This is how FL Studio, Ableton, Logic, etc. work.
    /// </summary>
    public class MidiTrack : IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; set; } = "MIDI Track";
        public float Volume { get; set; } = 1.0f;
        public float Pan { get; set; } = 0.0f;
        public bool IsMuted { get; set; }
        public bool IsSoloed { get; set; }

        public List<MidiClip> Clips { get; } = new();
        public EffectChain Effects { get; } = new();

        // The instrument that converts MIDI to audio
        public MidiSynthesizer Synth { get; set; }

        // VST plugin (if loaded) — will override the built-in synth
        public VstPluginInstance? VstPlugin { get; set; }

        // Render buffer (reused every frame)
        private AudioBuffer? _renderBuffer;

        // Track which notes are currently active (for note-off detection)
        private readonly HashSet<int> _activeNotes = new();

        public MidiTrack(int sampleRate = 44100)
        {
            Synth = new MidiSynthesizer(sampleRate);
        }

        /// <summary>
        /// Render this MIDI track's audio for the given frame range.
        /// Converts tick positions to frame positions, triggers note
        /// events on the synth, then renders the synth output.
        /// </summary>
        public unsafe AudioBuffer Render(long startFrame, int frameCount,
            int channels, int sampleRate, float bpm, bool hasSoloedTrack)
        {
            bool shouldPlay = !IsMuted && (!hasSoloedTrack || IsSoloed);

            if (_renderBuffer == null ||
                _renderBuffer.FrameCount != frameCount ||
                _renderBuffer.Channels != channels)
            {
                _renderBuffer?.Dispose();
                _renderBuffer = new AudioBuffer(frameCount, channels);
            }

            _renderBuffer.Clear();
            if (!shouldPlay) return _renderBuffer;

            // Convert frame range to tick range
            long startTick = MidiConstants.SecondsToTicks(
                (double)startFrame / sampleRate, bpm);
            long endTick = MidiConstants.SecondsToTicks(
                (double)(startFrame + frameCount) / sampleRate, bpm);

            // Process all clips
            foreach (var clip in Clips)
            {
                // Offset ticks to clip's position on the timeline
                long clipStartTick = clip.StartTick;
                long localStart = startTick - clipStartTick;
                long localEnd = endTick - clipStartTick;

                // Trigger note-on events
                var noteOns = clip.GetNoteOnEvents(localStart, localEnd);
                foreach (var note in noteOns)
                {
                    if (VstPlugin != null)
                        VstPlugin.SendNoteOn(note.NoteNumber, note.Velocity);
                    else
                        Synth.NoteOn(note.NoteNumber, note.Velocity);

                    _activeNotes.Add(note.NoteNumber);
                }

                // Trigger note-off events
                var noteOffs = clip.GetNoteOffEvents(localStart, localEnd);
                foreach (var note in noteOffs)
                {
                    if (VstPlugin != null)
                        VstPlugin.SendNoteOff(note.NoteNumber);
                    else
                        Synth.NoteOff(note.NoteNumber);

                    _activeNotes.Remove(note.NoteNumber);
                }
            }

            // Render the synth/VST output
            if (VstPlugin != null)
                VstPlugin.ProcessAudio(_renderBuffer);
            else
                Synth.RenderBlock(_renderBuffer);

            // Apply effect chain
            Effects.Process(_renderBuffer, sampleRate);

            // Apply volume and pan (same as AudioTrack)
            if (MathF.Abs(Volume - 1.0f) > 0.0001f)
                _renderBuffer.ApplyGain(Volume);

            if (channels == 2 && MathF.Abs(Pan) > 0.001f)
            {
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

        public void Reset()
        {
            Synth.AllNotesOff();
            VstPlugin?.Reset();
            Effects.Reset();
            _activeNotes.Clear();
        }

        public long GetTotalDurationFrames(float bpm, int sampleRate)
        {
            long maxTick = 0;
            foreach (var clip in Clips)
            {
                if (clip.EndTick > maxTick) maxTick = clip.EndTick;
            }
            double seconds = MidiConstants.TicksToSeconds(maxTick, bpm);
            return (long)(seconds * sampleRate);
        }

        public void Dispose()
        {
            _renderBuffer?.Dispose();
            Synth?.Dispose();
            VstPlugin?.Dispose();
            Effects.Dispose();
        }
    }
}