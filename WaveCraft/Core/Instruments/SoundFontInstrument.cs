using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Instruments
{
    /// <summary>
    /// SoundFont 2 (.sf2) instrument player.
    ///
    /// PROFESSIONAL CONCEPT: SoundFonts are sample-based instruments.
    /// Instead of generating waveforms mathematically (like our synth),
    /// they store actual recorded audio samples of real instruments
    /// at multiple pitches and velocities. During playback, the correct
    /// sample is selected, pitch-shifted, and played with an envelope.
    ///
    /// SF2 File Structure:
    ///   [RIFF header]
    ///   [INFO chunk] — metadata (name, author, etc.)
    ///   [sdta chunk] — raw sample data (16-bit PCM)
    ///   [pdta chunk] — preset/instrument/zone definitions
    ///
    /// This implementation loads the samples and preset mappings,
    /// then plays them back with pitch interpolation and ADSR envelopes.
    /// </summary>
    public unsafe class SoundFontInstrument : IInstrument
    {
        private string _name;
        private int _sampleRate;
        private bool _isReady;

        // Raw sample data from the SoundFont (all samples concatenated)
        private float* _sampleData;
        private IntPtr _sampleDataHandle;
        private int _totalSamples;

        // Preset/instrument/zone mappings
        private List<SfPreset> _presets = new();
        private List<SfInstrumentZone> _instrumentZones = new();
        private int _selectedPresetIndex;

        // Active voices
        private const int MaxVoices = 48;
        private SfVoice[] _voices = new SfVoice[MaxVoices];

        public string Name => _name;
        public InstrumentCategory Category => InstrumentCategory.SoundFont;
        public bool IsReady => _isReady;

        public List<SfPreset> Presets => _presets;
        public int SelectedPresetIndex
        {
            get => _selectedPresetIndex;
            set => _selectedPresetIndex = Math.Clamp(value, 0,
                Math.Max(0, _presets.Count - 1));
        }

        public SoundFontInstrument(int sampleRate = 44100)
        {
            _name = "SoundFont";
            _sampleRate = sampleRate;

            for (int i = 0; i < MaxVoices; i++)
                _voices[i] = new SfVoice();
        }

        /// <summary>
        /// Load a SoundFont 2 (.sf2) file.
        /// Parses the RIFF structure and extracts samples and presets.
        /// </summary>
        public void LoadFromFile(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream, Encoding.ASCII);

            // ---- RIFF header ----
            string riffId = new string(reader.ReadChars(4));
            if (riffId != "RIFF")
                throw new InvalidDataException("Not a RIFF file.");

            int fileSize = reader.ReadInt32();
            string sfbkId = new string(reader.ReadChars(4));
            if (sfbkId != "sfbk")
                throw new InvalidDataException("Not a SoundFont file.");

            byte[]? rawSamples = null;
            var presetHeaders = new List<SfPresetHeader>();
            var presetBags = new List<SfBag>();
            var presetGens = new List<SfGen>();
            var instHeaders = new List<SfInstHeader>();
            var instBags = new List<SfBag>();
            var instGens = new List<SfGen>();
            var sampleHeaders = new List<SfSampleHeader>();

            // ---- Parse chunks ----
            while (stream.Position < stream.Length - 8)
            {
                string chunkId = new string(reader.ReadChars(4));
                int chunkSize = reader.ReadInt32();
                long chunkEnd = stream.Position + chunkSize;

                switch (chunkId)
                {
                    case "LIST":
                        string listType = new string(reader.ReadChars(4));
                        // Parse sub-chunks within LIST
                        while (stream.Position < chunkEnd)
                        {
                            string subId = new string(reader.ReadChars(4));
                            int subSize = reader.ReadInt32();
                            long subEnd = stream.Position + subSize;

                            switch (subId)
                            {
                                case "INAM":
                                    byte[] nameBytes = reader.ReadBytes(subSize);
                                    _name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                                    break;

                                case "smpl":
                                    // Raw 16-bit PCM sample data
                                    rawSamples = reader.ReadBytes(subSize);
                                    break;

                                case "phdr":
                                    // Preset headers
                                    int phdrCount = subSize / 38;
                                    for (int i = 0; i < phdrCount; i++)
                                    {
                                        presetHeaders.Add(new SfPresetHeader
                                        {
                                            Name = ReadFixedString(reader, 20),
                                            Preset = reader.ReadUInt16(),
                                            Bank = reader.ReadUInt16(),
                                            BagIndex = reader.ReadUInt16(),
                                            Library = reader.ReadUInt32(),
                                            Genre = reader.ReadUInt32(),
                                            Morphology = reader.ReadUInt32()
                                        });
                                    }
                                    break;

                                case "pbag":
                                    int pbagCount = subSize / 4;
                                    for (int i = 0; i < pbagCount; i++)
                                    {
                                        presetBags.Add(new SfBag
                                        {
                                            GenIndex = reader.ReadUInt16(),
                                            ModIndex = reader.ReadUInt16()
                                        });
                                    }
                                    break;

                                case "pgen":
                                    int pgenCount = subSize / 4;
                                    for (int i = 0; i < pgenCount; i++)
                                    {
                                        presetGens.Add(new SfGen
                                        {
                                            Oper = reader.ReadUInt16(),
                                            Amount = reader.ReadInt16()
                                        });
                                    }
                                    break;

                                case "inst":
                                    int instCount = subSize / 22;
                                    for (int i = 0; i < instCount; i++)
                                    {
                                        instHeaders.Add(new SfInstHeader
                                        {
                                            Name = ReadFixedString(reader, 20),
                                            BagIndex = reader.ReadUInt16()
                                        });
                                    }
                                    break;

                                case "ibag":
                                    int ibagCount = subSize / 4;
                                    for (int i = 0; i < ibagCount; i++)
                                    {
                                        instBags.Add(new SfBag
                                        {
                                            GenIndex = reader.ReadUInt16(),
                                            ModIndex = reader.ReadUInt16()
                                        });
                                    }
                                    break;

                                case "igen":
                                    int igenCount = subSize / 4;
                                    for (int i = 0; i < igenCount; i++)
                                    {
                                        instGens.Add(new SfGen
                                        {
                                            Oper = reader.ReadUInt16(),
                                            Amount = reader.ReadInt16()
                                        });
                                    }
                                    break;

                                case "shdr":
                                    int shdrCount = subSize / 46;
                                    for (int i = 0; i < shdrCount; i++)
                                    {
                                        sampleHeaders.Add(new SfSampleHeader
                                        {
                                            Name = ReadFixedString(reader, 20),
                                            Start = reader.ReadUInt32(),
                                            End = reader.ReadUInt32(),
                                            LoopStart = reader.ReadUInt32(),
                                            LoopEnd = reader.ReadUInt32(),
                                            SampleRate = reader.ReadUInt32(),
                                            OriginalPitch = reader.ReadByte(),
                                            PitchCorrection = reader.ReadSByte(),
                                            SampleLink = reader.ReadUInt16(),
                                            SampleType = reader.ReadUInt16()
                                        });
                                    }
                                    break;

                                default:
                                    reader.ReadBytes(subSize);
                                    break;
                            }

                            stream.Position = subEnd;
                            // Pad to even boundary
                            if (subSize % 2 != 0 && stream.Position < chunkEnd)
                                stream.Position++;
                        }
                        break;

                    default:
                        stream.Position = chunkEnd;
                        break;
                }

                // Pad to even boundary
                if (chunkSize % 2 != 0 && stream.Position < stream.Length)
                    stream.Position++;
            }

            // ---- Convert 16-bit PCM samples to float ----
            if (rawSamples != null && rawSamples.Length > 0)
            {
                _totalSamples = rawSamples.Length / 2;
                int bytes = _totalSamples * sizeof(float);
                _sampleDataHandle = Marshal.AllocHGlobal(bytes);
                _sampleData = (float*)_sampleDataHandle;

                fixed (byte* raw = rawSamples)
                {
                    short* shorts = (short*)raw;
                    for (int i = 0; i < _totalSamples; i++)
                        _sampleData[i] = shorts[i] / 32768f;
                }
            }

            // ---- Build preset and zone mappings ----
            BuildPresetMappings(presetHeaders, presetBags, presetGens,
                instHeaders, instBags, instGens, sampleHeaders);

            _isReady = _totalSamples > 0 && _presets.Count > 0;
        }

        private void BuildPresetMappings(
            List<SfPresetHeader> presetHeaders, List<SfBag> presetBags,
            List<SfGen> presetGens, List<SfInstHeader> instHeaders,
            List<SfBag> instBags, List<SfGen> instGens,
            List<SfSampleHeader> sampleHeaders)
        {
            // Build instrument zones first
            for (int i = 0; i < instHeaders.Count - 1; i++) // Last is EOS
            {
                var inst = instHeaders[i];
                int bagStart = inst.BagIndex;
                int bagEnd = i + 1 < instHeaders.Count
                    ? instHeaders[i + 1].BagIndex : instBags.Count;

                for (int b = bagStart; b < bagEnd && b < instBags.Count; b++)
                {
                    int genStart = instBags[b].GenIndex;
                    int genEnd = b + 1 < instBags.Count
                        ? instBags[b + 1].GenIndex : instGens.Count;

                    var zone = new SfInstrumentZone
                    {
                        InstrumentName = inst.Name
                    };

                    for (int g = genStart; g < genEnd && g < instGens.Count; g++)
                    {
                        var gen = instGens[g];
                        switch (gen.Oper)
                        {
                            case 43: // keyRange
                                zone.KeyRangeLow = (byte)(gen.Amount & 0xFF);
                                zone.KeyRangeHigh = (byte)((gen.Amount >> 8) & 0xFF);
                                break;
                            case 44: // velRange
                                zone.VelRangeLow = (byte)(gen.Amount & 0xFF);
                                zone.VelRangeHigh = (byte)((gen.Amount >> 8) & 0xFF);
                                break;
                            case 53: // sampleID
                                int sampleIdx = gen.Amount;
                                if (sampleIdx >= 0 && sampleIdx < sampleHeaders.Count)
                                {
                                    zone.SampleHeader = sampleHeaders[sampleIdx];
                                    zone.HasSample = true;
                                }
                                break;
                            case 54: // sampleModes (loop)
                                zone.LoopMode = gen.Amount;
                                break;
                            case 58: // overridingRootKey
                                zone.RootKey = (byte)gen.Amount;
                                break;
                            case 51: // coarseTune
                                zone.CoarseTune = gen.Amount;
                                break;
                            case 52: // fineTune
                                zone.FineTune = gen.Amount;
                                break;
                        }
                    }

                    if (zone.HasSample)
                        _instrumentZones.Add(zone);
                }
            }

            // Build presets
            for (int i = 0; i < presetHeaders.Count - 1; i++) // Last is EOS
            {
                var ph = presetHeaders[i];
                _presets.Add(new SfPreset
                {
                    Name = ph.Name,
                    Bank = ph.Bank,
                    PresetNumber = ph.Preset,
                    // For simplicity, link all zones to every preset
                    // A full implementation would follow the preset→instrument chain
                    Zones = new List<SfInstrumentZone>(_instrumentZones)
                });
            }

            // If no presets were built, create a default one
            if (_presets.Count == 0 && _instrumentZones.Count > 0)
            {
                _presets.Add(new SfPreset
                {
                    Name = _name,
                    Zones = new List<SfInstrumentZone>(_instrumentZones)
                });
            }
        }

        // ---- IInstrument implementation ----

        public void NoteOn(int noteNumber, int velocity)
        {
            if (!_isReady || _selectedPresetIndex >= _presets.Count) return;

            var preset = _presets[_selectedPresetIndex];

            // Find the best matching zone for this note/velocity
            SfInstrumentZone? bestZone = null;
            foreach (var zone in preset.Zones)
            {
                if (noteNumber >= zone.KeyRangeLow && noteNumber <= zone.KeyRangeHigh &&
                    velocity >= zone.VelRangeLow && velocity <= zone.VelRangeHigh)
                {
                    bestZone = zone;
                    break;
                }
            }

            // Fallback: use any zone with a sample
            if (bestZone == null)
            {
                foreach (var zone in preset.Zones)
                {
                    if (zone.HasSample)
                    {
                        bestZone = zone;
                        break;
                    }
                }
            }

            if (bestZone == null) return;

            // Find a free voice
            int voiceIdx = -1;
            for (int i = 0; i < MaxVoices; i++)
            {
                if (!_voices[i].IsActive)
                {
                    voiceIdx = i;
                    break;
                }
            }

            if (voiceIdx < 0)
            {
                // Steal oldest releasing voice
                for (int i = 0; i < MaxVoices; i++)
                {
                    if (_voices[i].Stage == SfVoiceStage.Release)
                    {
                        voiceIdx = i;
                        break;
                    }
                }
                if (voiceIdx < 0) voiceIdx = 0;
            }

            ref var voice = ref _voices[voiceIdx];
            voice.IsActive = true;
            voice.NoteNumber = noteNumber;
            voice.Velocity = velocity;
            voice.Zone = bestZone;

            // Calculate playback rate based on pitch difference
            var shdr = bestZone.SampleHeader!;
            int rootKey = bestZone.RootKey > 0 ? bestZone.RootKey : shdr.OriginalPitch;
            double semitones = noteNumber - rootKey + bestZone.CoarseTune +
                               bestZone.FineTune / 100.0;
            voice.PlaybackRate = Math.Pow(2.0, semitones / 12.0) *
                                 shdr.SampleRate / _sampleRate;

            voice.SamplePosition = shdr.Start;
            voice.Stage = SfVoiceStage.Attack;
            voice.EnvelopeLevel = 0;
        }

        public void NoteOff(int noteNumber)
        {
            for (int i = 0; i < MaxVoices; i++)
            {
                if (_voices[i].IsActive && _voices[i].NoteNumber == noteNumber &&
                    _voices[i].Stage != SfVoiceStage.Release)
                {
                    _voices[i].Stage = SfVoiceStage.Release;
                    _voices[i].ReleaseLevel = _voices[i].EnvelopeLevel;
                    _voices[i].ReleaseSamples = (int)(0.3f * _sampleRate);
                }
            }
        }

        public void AllNotesOff()
        {
            for (int i = 0; i < MaxVoices; i++)
                _voices[i].IsActive = false;
        }

        public void RenderBlock(AudioBuffer output, int sampleRate)
        {
            if (!_isReady || _sampleData == null) return;

            float* dest = output.Ptr;
            int frames = output.FrameCount;
            int channels = output.Channels;

            for (int v = 0; v < MaxVoices; v++)
            {
                ref var voice = ref _voices[v];
                if (!voice.IsActive || voice.Zone == null) continue;

                var shdr = voice.Zone.SampleHeader!;
                float velScale = voice.Velocity / 127f * 0.5f;
                bool looping = voice.Zone.LoopMode == 1 || voice.Zone.LoopMode == 3;

                for (int f = 0; f < frames; f++)
                {
                    // Read sample with linear interpolation
                    uint sampleIdx = (uint)voice.SamplePosition;
                    float frac = (float)(voice.SamplePosition - sampleIdx);

                    float sample = 0;
                    if (sampleIdx < _totalSamples)
                    {
                        float s0 = _sampleData[sampleIdx];
                        float s1 = sampleIdx + 1 < _totalSamples
                            ? _sampleData[sampleIdx + 1] : s0;
                        sample = s0 + (s1 - s0) * frac;
                    }

                    // Update envelope
                    switch (voice.Stage)
                    {
                        case SfVoiceStage.Attack:
                            voice.EnvelopeLevel += 1f / (0.01f * _sampleRate);
                            if (voice.EnvelopeLevel >= 1f)
                            {
                                voice.EnvelopeLevel = 1f;
                                voice.Stage = SfVoiceStage.Sustain;
                            }
                            break;
                        case SfVoiceStage.Sustain:
                            break;
                        case SfVoiceStage.Release:
                            if (voice.ReleaseSamples > 0)
                            {
                                voice.EnvelopeLevel = voice.ReleaseLevel *
                                    ((float)voice.ReleaseSamples / (0.3f * _sampleRate));
                                voice.ReleaseSamples--;
                            }
                            else
                            {
                                voice.IsActive = false;
                                break;
                            }
                            break;
                    }

                    sample *= voice.EnvelopeLevel * velScale;

                    // Mix into output
                    float* frame = dest + f * channels;
                    for (int ch = 0; ch < channels; ch++)
                        frame[ch] += sample;

                    // Advance sample position
                    voice.SamplePosition += voice.PlaybackRate;

                    // Handle looping
                    if (looping && voice.SamplePosition >= shdr.LoopEnd && shdr.LoopEnd > shdr.LoopStart)
                    {
                        voice.SamplePosition = shdr.LoopStart +
                            (voice.SamplePosition - shdr.LoopEnd);
                    }
                    else if (!looping && voice.SamplePosition >= shdr.End)
                    {
                        voice.IsActive = false;
                        break;
                    }
                }
            }
        }

        public void Reset() => AllNotesOff();

        public void Dispose()
        {
            if (_sampleDataHandle != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_sampleDataHandle);
                _sampleDataHandle = IntPtr.Zero;
                _sampleData = null;
            }
        }

        // ---- Helpers ----

        private static string ReadFixedString(BinaryReader reader, int length)
        {
            byte[] bytes = reader.ReadBytes(length);
            int nullIdx = Array.IndexOf(bytes, (byte)0);
            int strLen = nullIdx >= 0 ? nullIdx : length;
            return Encoding.ASCII.GetString(bytes, 0, strLen).Trim();
        }

        // ---- SF2 Data Structures ----

        private struct SfPresetHeader
        {
            public string Name;
            public ushort Preset, Bank, BagIndex;
            public uint Library, Genre, Morphology;
        }

        private struct SfInstHeader
        {
            public string Name;
            public ushort BagIndex;
        }

        private struct SfBag
        {
            public ushort GenIndex, ModIndex;
        }

        private struct SfGen
        {
            public ushort Oper;
            public short Amount;
        }
    }

    // ---- Shared data types for SoundFont ----

    public class SfSampleHeader
    {
        public string Name { get; set; } = "";
        public uint Start, End, LoopStart, LoopEnd, SampleRate;
        public byte OriginalPitch;
        public sbyte PitchCorrection;
        public ushort SampleLink, SampleType;
    }

    public class SfInstrumentZone
    {
        public string InstrumentName { get; set; } = "";
        public byte KeyRangeLow { get; set; } = 0;
        public byte KeyRangeHigh { get; set; } = 127;
        public byte VelRangeLow { get; set; } = 0;
        public byte VelRangeHigh { get; set; } = 127;
        public byte RootKey { get; set; }
        public short CoarseTune { get; set; }
        public short FineTune { get; set; }
        public short LoopMode { get; set; }
        public SfSampleHeader? SampleHeader { get; set; }
        public bool HasSample { get; set; }
    }

    public class SfPreset
    {
        public string Name { get; set; } = "";
        public int Bank { get; set; }
        public int PresetNumber { get; set; }
        public List<SfInstrumentZone> Zones { get; set; } = new();
    }

    public enum SfVoiceStage { Off, Attack, Sustain, Release }

    public class SfVoice
    {
        public bool IsActive;
        public int NoteNumber;
        public int Velocity;
        public SfInstrumentZone? Zone;
        public double SamplePosition;
        public double PlaybackRate;
        public SfVoiceStage Stage;
        public float EnvelopeLevel;
        public float ReleaseLevel;
        public int ReleaseSamples;
    }
}