using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Midi
{
    /// <summary>
    /// A polyphonic MIDI synthesiser that converts MIDI notes into audio.
    ///
    /// PROFESSIONAL CONCEPT: This is a subtractive synthesiser with:
    /// - Multiple oscillator waveforms (sine, saw, square, triangle)
    /// - ADSR envelope (Attack, Decay, Sustain, Release)
    /// - Polyphony (multiple notes playing simultaneously)
    /// - Per-voice state management using unsafe pointer arrays
    ///
    /// In a real DAW, this would be replaced by VST plugins, but having
    /// a built-in synth is essential for MIDI preview and testing.
    /// </summary>
    public unsafe class MidiSynthesizer : IDisposable
    {
        public enum Waveform { Sine, Saw, Square, Triangle }

        // ---- Synth parameters ----
        public Waveform OscillatorType { get; set; } = Waveform.Saw;
        public float MasterVolume { get; set; } = 0.3f;

        // ADSR Envelope (in seconds)
        public float AttackTime { get; set; } = 0.01f;
        public float DecayTime { get; set; } = 0.1f;
        public float SustainLevel { get; set; } = 0.7f;
        public float ReleaseTime { get; set; } = 0.2f;

        // Detuning for a richer sound (in cents, 100 cents = 1 semitone)
        public float DetuneCents { get; set; } = 5f;

        // ---- Voice management ----
        private const int MaxVoices = 32;

        /// <summary>
        /// A single synthesiser voice. Stored as a struct for cache efficiency.
        /// All voices are pre-allocated — no heap allocation during playback.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Voice
        {
            public bool IsActive;
            public int NoteNumber;
            public int Velocity;
            public double Phase;          // Oscillator phase (0 to 2π)
            public double PhaseDetune;    // Detuned oscillator phase
            public double Frequency;      // Note frequency in Hz
            public double FrequencyDetune;// Detuned frequency

            // ADSR state
            public EnvelopeStage Stage;
            public float EnvelopeLevel;
            public float EnvelopeVelocity; // Rate of change per sample

            // For note-off: how long since release started
            public float ReleaseStartLevel;
            public int ReleaseSamplesRemaining;
        }

        private enum EnvelopeStage { Off, Attack, Decay, Sustain, Release }

        private Voice* _voices;
        private IntPtr _voiceMemory;
        private int _sampleRate;

        public MidiSynthesizer(int sampleRate = 44100)
        {
            _sampleRate = sampleRate;

            // Pre-allocate all voices in unmanaged memory
            int bytes = MaxVoices * sizeof(Voice);
            _voiceMemory = Marshal.AllocHGlobal(bytes);
            _voices = (Voice*)_voiceMemory;

            // Initialise all voices to inactive
            for (int i = 0; i < MaxVoices; i++)
                _voices[i].IsActive = false;
        }

        /// <summary>
        /// Trigger a note-on event. Finds a free voice and starts it.
        /// </summary>
        public void NoteOn(int noteNumber, int velocity)
        {
            // Find a free voice (or steal the oldest one)
            int voiceIndex = -1;
            for (int i = 0; i < MaxVoices; i++)
            {
                if (!_voices[i].IsActive)
                {
                    voiceIndex = i;
                    break;
                }
            }

            // Voice stealing: if all voices are used, steal the one
            // in the release phase with the lowest level
            if (voiceIndex < 0)
            {
                float lowestLevel = float.MaxValue;
                for (int i = 0; i < MaxVoices; i++)
                {
                    if (_voices[i].Stage == EnvelopeStage.Release &&
                        _voices[i].EnvelopeLevel < lowestLevel)
                    {
                        lowestLevel = _voices[i].EnvelopeLevel;
                        voiceIndex = i;
                    }
                }
                // Last resort: steal voice 0
                if (voiceIndex < 0) voiceIndex = 0;
            }

            ref Voice v = ref _voices[voiceIndex];
            v.IsActive = true;
            v.NoteNumber = noteNumber;
            v.Velocity = velocity;
            v.Phase = 0;
            v.PhaseDetune = 0;
            v.Frequency = 440.0 * Math.Pow(2.0, (noteNumber - 69) / 12.0);

            // Calculate detuned frequency
            double detuneFactor = Math.Pow(2.0, DetuneCents / 1200.0);
            v.FrequencyDetune = v.Frequency * detuneFactor;

            v.Stage = EnvelopeStage.Attack;
            v.EnvelopeLevel = 0f;
        }

        /// <summary>
        /// Trigger a note-off event. Moves matching voices to release phase.
        /// </summary>
        public void NoteOff(int noteNumber)
        {
            for (int i = 0; i < MaxVoices; i++)
            {
                if (_voices[i].IsActive && _voices[i].NoteNumber == noteNumber &&
                    _voices[i].Stage != EnvelopeStage.Release)
                {
                    _voices[i].Stage = EnvelopeStage.Release;
                    _voices[i].ReleaseStartLevel = _voices[i].EnvelopeLevel;
                    _voices[i].ReleaseSamplesRemaining =
                        (int)(ReleaseTime * _sampleRate);
                }
            }
        }

        /// <summary>
        /// Stop all voices immediately.
        /// </summary>
        public void AllNotesOff()
        {
            for (int i = 0; i < MaxVoices; i++)
                _voices[i].IsActive = false;
        }

        /// <summary>
        /// Render audio from all active voices into a buffer.
        /// This is called from the audio thread — fully unsafe, zero allocations.
        /// </summary>
        public void RenderBlock(AudioBuffer output)
        {
            float* ptr = output.Ptr;
            int frames = output.FrameCount;
            int channels = output.Channels;

            double phaseInc, phaseIncDetune;
            float velocityScale;

            for (int v = 0; v < MaxVoices; v++)
            {
                if (!_voices[v].IsActive) continue;

                ref Voice voice = ref _voices[v];
                phaseInc = voice.Frequency * 2.0 * Math.PI / _sampleRate;
                phaseIncDetune = voice.FrequencyDetune * 2.0 * Math.PI / _sampleRate;
                velocityScale = voice.Velocity / 127f;

                for (int f = 0; f < frames; f++)
                {
                    // Generate oscillator samples
                    float osc1 = GenerateSample(voice.Phase);
                    float osc2 = GenerateSample(voice.PhaseDetune);
                    float sample = (osc1 + osc2) * 0.5f;

                    // Apply envelope
                    UpdateEnvelope(ref voice);
                    sample *= voice.EnvelopeLevel * velocityScale * MasterVolume;

                    // Mix into output (additive, supports multiple voices)
                    float* frame = ptr + f * channels;
                    for (int ch = 0; ch < channels; ch++)
                        frame[ch] += sample;

                    // Advance phase
                    voice.Phase += phaseInc;
                    voice.PhaseDetune += phaseIncDetune;

                    // Wrap phase to prevent floating-point precision loss
                    if (voice.Phase > 2.0 * Math.PI) voice.Phase -= 2.0 * Math.PI;
                    if (voice.PhaseDetune > 2.0 * Math.PI) voice.PhaseDetune -= 2.0 * Math.PI;
                }

                // Deactivate voice if envelope is done
                if (voice.Stage == EnvelopeStage.Off)
                    voice.IsActive = false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GenerateSample(double phase)
        {
            return OscillatorType switch
            {
                Waveform.Sine => (float)Math.Sin(phase),
                Waveform.Saw => (float)(1.0 - 2.0 * (phase / (2.0 * Math.PI))),
                Waveform.Square => phase < Math.PI ? 1f : -1f,
                Waveform.Triangle => (float)(2.0 * Math.Abs(
                    2.0 * (phase / (2.0 * Math.PI)) - 1.0) - 1.0),
                _ => 0f
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateEnvelope(ref Voice voice)
        {
            switch (voice.Stage)
            {
                case EnvelopeStage.Attack:
                    voice.EnvelopeLevel += 1f / (AttackTime * _sampleRate);
                    if (voice.EnvelopeLevel >= 1f)
                    {
                        voice.EnvelopeLevel = 1f;
                        voice.Stage = EnvelopeStage.Decay;
                    }
                    break;

                case EnvelopeStage.Decay:
                    voice.EnvelopeLevel -= (1f - SustainLevel) /
                        (DecayTime * _sampleRate);
                    if (voice.EnvelopeLevel <= SustainLevel)
                    {
                        voice.EnvelopeLevel = SustainLevel;
                        voice.Stage = EnvelopeStage.Sustain;
                    }
                    break;

                case EnvelopeStage.Sustain:
                    // Hold at sustain level until note-off
                    break;

                case EnvelopeStage.Release:
                    if (voice.ReleaseSamplesRemaining > 0)
                    {
                        voice.EnvelopeLevel = voice.ReleaseStartLevel *
                            ((float)voice.ReleaseSamplesRemaining /
                             (ReleaseTime * _sampleRate));
                        voice.ReleaseSamplesRemaining--;
                    }
                    else
                    {
                        voice.EnvelopeLevel = 0f;
                        voice.Stage = EnvelopeStage.Off;
                    }
                    break;
            }
        }

        public void Dispose()
        {
            if (_voiceMemory != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_voiceMemory);
                _voiceMemory = IntPtr.Zero;
                _voices = null;
            }
        }
    }
}