using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WaveCraft.Core.Audio;
using WaveCraft.Core.Tracks;

namespace WaveCraft.Core.Instruments
{
    public class VstInstrument : IInstrument
    {
        private readonly VstPluginInstance _plugin;
        private readonly string _displayName;

        public string Name => _displayName;
        public InstrumentCategory Category => InstrumentCategory.VstPlugin;
        public bool IsReady => _plugin.IsLoaded;
        public VstPluginInstance Plugin => _plugin;

        public VstInstrument(VstPluginInstance plugin, string? displayName = null)
        {
            _plugin = plugin;
            _displayName = displayName ?? plugin.PluginName;
        }

        public void NoteOn(int noteNumber, int velocity)
            => _plugin.SendNoteOn(noteNumber, velocity);

        public void NoteOff(int noteNumber)
            => _plugin.SendNoteOff(noteNumber);

        public void AllNotesOff()
            => _plugin.SendControlChange(123, 0);

        public void RenderBlock(AudioBuffer output, int sampleRate)
            => _plugin.ProcessAudio(output);

        public void Reset() => _plugin.Reset();

        public void Dispose() => _plugin.Dispose();

        public static VstInstrument? LoadFromFile(string dllPath, int sampleRate = 44100, int blockSize = 1024)
        {
            var plugin = VstPluginInstance.LoadPlugin(dllPath, sampleRate, blockSize);
            if (plugin == null) return null;
            return new VstInstrument(plugin);
        }
    }
}
