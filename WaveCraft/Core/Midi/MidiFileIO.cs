using System.IO;
using System.Text;

namespace WaveCraft.Core.Midi
{
    /// <summary>
    /// Reads and writes Standard MIDI Files (.mid).
    ///
    /// PROFESSIONAL CONCEPT: The SMF (Standard MIDI File) format uses
    /// variable-length encoding for delta times (same concept as UTF-8
    /// and Protocol Buffers). Each event stores the time DIFFERENCE from
    /// the previous event, not an absolute position. This makes the
    /// format very compact.
    ///
    /// Format structure:
    ///   [MThd header]
    ///     - Format type (0=single track, 1=multi-track)
    ///     - Number of tracks
    ///     - Time division (ticks per beat)
    ///   [MTrk chunks] (one per track)
    ///     - Sequence of delta-time + event pairs
    /// </summary>
    public static class MidiFileReader
    {
        public static List<MidiClip> LoadFromFile(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream, Encoding.ASCII);

            var clips = new List<MidiClip>();

            // ---- MThd header ----
            string headerChunk = ReadChunkId(reader);
            if (headerChunk != "MThd")
                throw new InvalidDataException("Not a MIDI file.");

            int headerLength = ReadBigEndianInt32(reader);
            short formatType = ReadBigEndianInt16(reader);
            short trackCount = ReadBigEndianInt16(reader);
            short timeDivision = ReadBigEndianInt16(reader);

            // Time division: ticks per beat (if positive)
            int ticksPerBeat = timeDivision > 0
                ? timeDivision
                : MidiConstants.TicksPerBeat;

            // Scale factor to normalise to our internal PPQN
            double tickScale = (double)MidiConstants.TicksPerBeat / ticksPerBeat;

            // ---- Read each track ----
            for (int t = 0; t < trackCount; t++)
            {
                string trackChunk = ReadChunkId(reader);
                if (trackChunk != "MTrk")
                    throw new InvalidDataException($"Expected MTrk, got '{trackChunk}'.");

                int trackLength = ReadBigEndianInt32(reader);
                long trackEnd = stream.Position + trackLength;

                var clip = new MidiClip
                {
                    Name = $"Track {t + 1}"
                };

                long absoluteTick = 0;
                byte lastStatus = 0; // Running status

                while (stream.Position < trackEnd)
                {
                    // Read delta time (variable-length quantity)
                    long delta = ReadVariableLength(reader);
                    absoluteTick += delta;

                    long scaledTick = (long)(absoluteTick * tickScale);

                    // Read event
                    byte status = reader.ReadByte();

                    // Running status: if high bit is not set, reuse last status
                    if ((status & 0x80) == 0)
                    {
                        // This byte is actually data, not a status byte
                        stream.Position--;
                        status = lastStatus;
                    }
                    else
                    {
                        lastStatus = status;
                    }

                    byte eventType = (byte)(status & 0xF0);
                    byte channel = (byte)(status & 0x0F);

                    switch (eventType)
                    {
                        case 0x90: // Note On
                            {
                                byte note = reader.ReadByte();
                                byte velocity = reader.ReadByte();

                                if (velocity > 0)
                                {
                                    // Note On — we'll set duration when we find Note Off
                                    clip.Notes.Add(new MidiNote
                                    {
                                        NoteNumber = note,
                                        Velocity = velocity,
                                        StartTick = scaledTick,
                                        DurationTicks = MidiConstants.QuarterNote, // Default, updated later
                                        Channel = channel
                                    });
                                }
                                else
                                {
                                    // Velocity 0 = Note Off (common in MIDI files)
                                    ResolveNoteOff(clip, note, channel, scaledTick);
                                }
                                break;
                            }

                        case 0x80: // Note Off
                            {
                                byte note = reader.ReadByte();
                                byte velocity = reader.ReadByte(); // Release velocity (usually ignored)
                                ResolveNoteOff(clip, note, channel, scaledTick);
                                break;
                            }

                        case 0xB0: // Control Change
                            {
                                byte controller = reader.ReadByte();
                                byte value = reader.ReadByte();
                                clip.ControlChanges.Add(new MidiControlChange
                                {
                                    Tick = scaledTick,
                                    Channel = channel,
                                    Controller = controller,
                                    Value = value
                                });
                                break;
                            }

                        case 0xE0: // Pitch Bend
                            {
                                byte lsb = reader.ReadByte();
                                byte msb = reader.ReadByte();
                                int value = ((msb << 7) | lsb) - 8192;
                                clip.PitchBends.Add(new MidiPitchBend
                                {
                                    Tick = scaledTick,
                                    Channel = channel,
                                    Value = value
                                });
                                break;
                            }

                        case 0xC0: // Program Change
                        case 0xD0: // Channel Pressure
                            reader.ReadByte(); // 1 data byte
                            break;

                        case 0xA0: // Polyphonic Aftertouch
                            reader.ReadByte();
                            reader.ReadByte();
                            break;

                        case 0xF0: // System / Meta events
                            {
                                if (status == 0xFF) // Meta event
                                {
                                    byte metaType = reader.ReadByte();
                                    int metaLength = (int)ReadVariableLength(reader);

                                    if (metaType == 0x03 && metaLength > 0) // Track name
                                    {
                                        byte[] nameBytes = reader.ReadBytes(metaLength);
                                        clip.Name = Encoding.ASCII.GetString(nameBytes).Trim();
                                    }
                                    else
                                    {
                                        reader.ReadBytes(metaLength); // Skip
                                    }
                                }
                                else if (status == 0xF0 || status == 0xF7) // SysEx
                                {
                                    int sysexLen = (int)ReadVariableLength(reader);
                                    reader.ReadBytes(sysexLen);
                                }
                                break;
                            }

                        default:
                            // Unknown event — try to skip gracefully
                            break;
                    }
                }

                // Only add clips that have notes
                if (clip.Notes.Count > 0)
                    clips.Add(clip);

                // Ensure we're at the track end
                stream.Position = trackEnd;
            }

            return clips;
        }

        /// <summary>
        /// Find a matching Note On and set its duration.
        /// </summary>
        private static void ResolveNoteOff(MidiClip clip, int noteNumber,
            int channel, long offTick)
        {
            // Search backwards for the most recent unresolved Note On
            for (int i = clip.Notes.Count - 1; i >= 0; i--)
            {
                var note = clip.Notes[i];
                if (note.NoteNumber == noteNumber && note.Channel == channel &&
                    note.EndTick > offTick) // Still has the default duration
                {
                    long duration = offTick - note.StartTick;
                    if (duration > 0)
                    {
                        clip.Notes[i] = note with { DurationTicks = duration };
                    }
                    break;
                }
            }
        }

        // ---- Binary helpers for big-endian MIDI format ----

        private static string ReadChunkId(BinaryReader reader)
            => new string(reader.ReadChars(4));

        private static int ReadBigEndianInt32(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }

        private static short ReadBigEndianInt16(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(2);
            return (short)((bytes[0] << 8) | bytes[1]);
        }

        /// <summary>
        /// Read a MIDI variable-length quantity.
        /// Uses 7 bits per byte, with the high bit as a continuation flag.
        /// Same encoding principle as UTF-8 and Protocol Buffers.
        /// </summary>
        private static long ReadVariableLength(BinaryReader reader)
        {
            long result = 0;
            byte b;
            do
            {
                b = reader.ReadByte();
                result = (result << 7) | (long)(b & 0x7F);
            } while ((b & 0x80) != 0);

            return result;
        }
    }

    /// <summary>
    /// Writes MIDI clips to Standard MIDI File format.
    /// </summary>
    public static class MidiFileWriter
    {
        public static void SaveToFile(string filePath, List<MidiClip> clips,
            int ticksPerBeat = 480)
        {
            using var stream = File.Create(filePath);
            using var writer = new BinaryWriter(stream, Encoding.ASCII);

            double tickScale = (double)ticksPerBeat / MidiConstants.TicksPerBeat;

            // ---- MThd header ----
            writer.Write("MThd".ToCharArray());
            WriteBigEndianInt32(writer, 6);
            WriteBigEndianInt16(writer, 1); // Format 1 (multi-track)
            WriteBigEndianInt16(writer, (short)clips.Count);
            WriteBigEndianInt16(writer, (short)ticksPerBeat);

            // ---- Write each track ----
            foreach (var clip in clips)
            {
                // Build the track data in memory first (need to know length)
                using var trackStream = new MemoryStream();
                using var trackWriter = new BinaryWriter(trackStream);

                // Track name meta event
                byte[] nameBytes = Encoding.ASCII.GetBytes(clip.Name);
                WriteVariableLength(trackWriter, 0); // Delta = 0
                trackWriter.Write((byte)0xFF);
                trackWriter.Write((byte)0x03);
                WriteVariableLength(trackWriter, nameBytes.Length);
                trackWriter.Write(nameBytes);

                // Build a sorted list of all events with absolute ticks
                var events = new List<(long Tick, byte[] Data)>();

                foreach (var note in clip.Notes)
                {
                    long startTick = (long)(note.StartTick * tickScale);
                    long endTick = (long)(note.EndTick * tickScale);

                    // Note On
                    events.Add((startTick, new byte[]
                    {
                        (byte)(0x90 | note.Channel),
                        (byte)note.NoteNumber,
                        (byte)note.Velocity
                    }));

                    // Note Off
                    events.Add((endTick, new byte[]
                    {
                        (byte)(0x80 | note.Channel),
                        (byte)note.NoteNumber,
                        0
                    }));
                }

                // Sort by tick
                events.Sort((a, b) => a.Tick.CompareTo(b.Tick));

                // Write events with delta times
                long lastTick = 0;
                foreach (var (tick, data) in events)
                {
                    long delta = tick - lastTick;
                    WriteVariableLength(trackWriter, delta);
                    trackWriter.Write(data);
                    lastTick = tick;
                }

                // End of track meta event
                WriteVariableLength(trackWriter, 0);
                trackWriter.Write((byte)0xFF);
                trackWriter.Write((byte)0x2F);
                trackWriter.Write((byte)0x00);

                // Write MTrk chunk
                byte[] trackData = trackStream.ToArray();
                writer.Write("MTrk".ToCharArray());
                WriteBigEndianInt32(writer, trackData.Length);
                writer.Write(trackData);
            }
        }

        private static void WriteBigEndianInt32(BinaryWriter writer, int value)
        {
            writer.Write((byte)((value >> 24) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }

        private static void WriteBigEndianInt16(BinaryWriter writer, short value)
        {
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }

        private static void WriteVariableLength(BinaryWriter writer, long value)
        {
            // Encode value into 7-bit groups with continuation bits
            var bytes = new List<byte>();
            bytes.Add((byte)(value & 0x7F));
            value >>= 7;

            while (value > 0)
            {
                bytes.Add((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }

            // Write in reverse order (most significant first)
            bytes.Reverse();
            foreach (byte b in bytes)
                writer.Write(b);
        }
    }
}