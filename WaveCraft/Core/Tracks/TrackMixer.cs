using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Tracks
{
    /// <summary>
    /// The master mixer. Takes all track outputs and sums them into
    /// a single stereo master buffer, then applies the master effect chain.
    ///
    /// PROFESSIONAL CONCEPT: In a real DAW, the mixer runs on a dedicated
    /// audio thread at a fixed buffer size (e.g., 512 or 1024 frames).
    /// Every ~11ms (at 44100/1024), it must produce the next block of audio.
    /// If it misses the deadline, you hear a glitch. So this code path
    /// must be deterministic — no allocations, no locks, no I/O.
    /// </summary>
    public class TrackMixer : IDisposable
    {
        private readonly List<AudioTrack> _tracks = new();
        private readonly List<MidiTrack> _midiTracks = new();
        private AudioBuffer? _masterBuffer;
        private readonly Effects.EffectChain _masterEffects = new();

        public IReadOnlyList<AudioTrack> Tracks => _tracks;
        public IReadOnlyList<MidiTrack> MidiTracks => _midiTracks;
        public Effects.EffectChain MasterEffects => _masterEffects;

        public float MasterVolume { get; set; } = 1.0f;
        public float Bpm { get; set; } = 120f;

        // Peak levels from the last render — read by the UI for metering
        public float LastLeftPeak { get; private set; }
        public float LastRightPeak { get; private set; }
        public float LastLeftRms { get; private set; }
        public float LastRightRms { get; private set; }

        public AudioTrack AddTrack(string name)
        {
            var track = new AudioTrack { Name = name };
            _tracks.Add(track);
            return track;
        }

        public void RemoveTrack(AudioTrack track)
        {
            _tracks.Remove(track);
            track.Dispose();
        }

        public MidiTrack AddMidiTrack(string name, int sampleRate)
        {
            var track = new MidiTrack(sampleRate) { Name = name };
            _midiTracks.Add(track);
            return track;
        }

        public void RemoveMidiTrack(MidiTrack track)
        {
            _midiTracks.Remove(track);
            track.Dispose();
        }

        /// <summary>
        /// Render all tracks and mix them into the master buffer.
        /// This is the audio thread's main entry point.
        /// </summary>
        public unsafe AudioBuffer RenderBlock(long startFrame, int frameCount,
            int channels, int sampleRate)
        {
            // Ensure master buffer exists and is the right size
            if (_masterBuffer == null ||
                _masterBuffer.FrameCount != frameCount ||
                _masterBuffer.Channels != channels)
            {
                _masterBuffer?.Dispose();
                _masterBuffer = new AudioBuffer(frameCount, channels);
            }

            _masterBuffer.Clear();

            // Check if any track is soloed
            bool hasSolo = false;
            foreach (var track in _tracks)
            {
                if (track.IsSoloed) { hasSolo = true; break; }
            }
            foreach (var track in _midiTracks)
            {
                if (track.IsSoloed) { hasSolo = true; break; }
            }

            // Render each audio track and mix into master
            foreach (var track in _tracks)
            {
                var trackOutput = track.Render(startFrame, frameCount,
                    channels, sampleRate, hasSolo);
                _masterBuffer.MixFrom(trackOutput);
            }

            // Render each MIDI track and mix into master
            foreach (var track in _midiTracks)
            {
                var trackOutput = track.Render(startFrame, frameCount,
                    channels, sampleRate, Bpm, hasSolo);
                _masterBuffer.MixFrom(trackOutput);
            }

            // Master effects chain
            _masterEffects.Process(_masterBuffer, sampleRate);

            // Master volume
            if (MathF.Abs(MasterVolume - 1.0f) > 0.0001f)
                _masterBuffer.ApplyGain(MasterVolume);

            // Clamp to prevent clipping
            _masterBuffer.Clamp();

            // Measure levels for the UI meters
            (LastLeftPeak, LastRightPeak) = _masterBuffer.GetPeakLevels();
            (LastLeftRms, LastRightRms) = _masterBuffer.GetRmsLevels();

            return _masterBuffer;
        }

        /// <summary>
        /// Get the total project duration across all tracks.
        /// </summary>
        public long GetTotalDurationFrames()
        {
            long max = 0;
            foreach (var track in _tracks)
            {
                long dur = track.GetTotalDurationFrames();
                if (dur > max) max = dur;
            }
            foreach (var track in _midiTracks)
            {
                long dur = track.GetTotalDurationFrames(Bpm, 44100);
                if (dur > max) max = dur;
            }
            return max;
        }

        public void ResetAll()
        {
            foreach (var track in _tracks)
                track.Effects.Reset();
            foreach (var track in _midiTracks)
                track.Reset();
            _masterEffects.Reset();
        }

        public void Dispose()
        {
            foreach (var track in _tracks)
                track.Dispose();
            _tracks.Clear();
            foreach (var track in _midiTracks)
                track.Dispose();
            _midiTracks.Clear();
            _masterBuffer?.Dispose();
            _masterEffects.Dispose();
        }
    }
}