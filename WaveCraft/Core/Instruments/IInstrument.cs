using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Instruments
{
    public interface IInstrument : IDisposable
    {
        string Name { get; }
        
        InstrumentCategory Category { get; }

        bool IsReady { get; }

        void NoteOn(int noteNumber, int velocity);

        void NoteOff(int noteNumber);

        void AllNotesOff();

        void RenderBlock(AudioBuffer output, int sampleRate);

        void Reset();
    }

    public enum InstrumentCategory
    {
        BuiltInSynth,
        SoundFont,
        VstPlugin
    }
}
