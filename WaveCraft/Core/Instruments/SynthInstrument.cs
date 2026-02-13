using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using WaveCraft.Core.Audio;
using WaveCraft.Core.Midi;

namespace WaveCraft.Core.Instruments
{
    internal class SynthInstrument : IInstrument
    {
        private readonly MidiSynthesizer _synth;

        public string Name { get; }
        public InstrumentCategory Category => InstrumentCategory.BuiltInSynth;
        public bool IsReady => true;
        public MidiSynthesizer Synth => _synth;

        public SynthInstrument(string name, MidiSynthesizer synth)
        {
            Name = name;
            _synth = synth;
        }

        public void NoteOn(int noteNumber, int velocity) => _synth.NoteOn(noteNumber, velocity);
        public void NoteOff(int noteNumber) => _synth.NoteOff(noteNumber);
        public void AllNotesOff() => _synth.AllNotesOff();
        public void RenderBlock(AudioBuffer output, int sampleRate) => _synth.RenderBlock(output);
        public void Reset() => _synth.AllNotesOff();
        public void Dispose() => _synth.Dispose();

        public static List<SynthInstrument> CreatePresets(int sampleRate = 44100)
        {
            return new List<SynthInstrument>
            {
                Create("Saw Lead", sampleRate,
                    MidiSynthesizer.Waveform.Saw, 0.01f, 0.1f, 0.7f, 0.2f, 0.3f, 5f),

                Create("Square Lead", sampleRate,
                    MidiSynthesizer.Waveform.Square, 0.01f, 0.05f, 0.8f, 0.15f, 0.25f, 3f),

                Create("Sine Pad", sampleRate,
                    MidiSynthesizer.Waveform.Sine, 0.3f, 0.5f, 0.6f, 1.0f, 0.2f, 0f),

                Create("Triangle Pad", sampleRate,
                    MidiSynthesizer.Waveform.Triangle, 0.2f, 0.3f, 0.65f, 0.8f, 0.2f, 2f),

                Create("Pluck", sampleRate,
                    MidiSynthesizer.Waveform.Saw, 0.001f, 0.15f, 0.0f, 0.1f, 0.3f, 8f),

                Create("Organ", sampleRate,
                    MidiSynthesizer.Waveform.Square, 0.005f, 0.01f, 1.0f, 0.05f, 0.2f, 0f),

                Create("Soft Sine", sampleRate,
                    MidiSynthesizer.Waveform.Sine, 0.05f, 0.1f, 0.5f, 0.3f, 0.15f, 0f),

                Create("Brass", sampleRate,
                    MidiSynthesizer.Waveform.Saw, 0.05f, 0.2f, 0.75f, 0.15f, 0.35f, 10f),

                Create("Bass", sampleRate,
                    MidiSynthesizer.Waveform.Saw, 0.005f, 0.1f, 0.6f, 0.08f, 0.4f, 3f),

                Create("Bell", sampleRate,
                    MidiSynthesizer.Waveform.Triangle, 0.001f, 0.8f, 0.0f, 1.5f, 0.2f, 12f),
            };
        }

        private static SynthInstrument Create(string name, int sampleRate,
            MidiSynthesizer.Waveform waveform,
            float attack, float decay, float sustain, float release,
            float volume, float detune)
        {
            var synth = new MidiSynthesizer(sampleRate)
            {
                OscillatorType = waveform,
                AttackTime = attack,
                DecayTime = decay,
                SustainLevel = sustain,
                ReleaseTime = release,
                MasterVolume = volume,
                DetuneCents = detune
            };

            return new SynthInstrument(name, synth);
        }
    }
}
