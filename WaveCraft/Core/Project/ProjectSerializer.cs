using System.IO;
using System.Text;
using WaveCraft.Core.Audio;
using WaveCraft.Core.Tracks;

namespace WaveCraft.Core.Project
{
    /// <summary>
    /// Binary project file serialiser.
    ///
    /// PROFESSIONAL CONCEPT: Binary serialisation is used when file size
    /// and load speed matter. Audio projects can contain millions of samples —
    /// JSON/XML would be massive. The format uses a header + chunk structure
    /// similar to WAV files, making it extensible.
    ///
    /// File format:
    ///   [MAGIC 4 bytes "WCFT"]
    ///   [VERSION uint16]
    ///   [HEADER CHUNK]
    ///     - SampleRate, Channels, BPM, etc.
    ///   [TRACK CHUNKS]
    ///     - Track name, volume, pan, mute, solo
    ///     [CLIP CHUNKS]
    ///       - Clip name, start, duration, audio data
    /// </summary>
    public static class ProjectSerializer
    {
        private static readonly byte[] Magic = "WCFT"u8.ToArray();
        private const ushort Version = 1;

        public static void Save(DawProject project, string filePath)
        {
            using var stream = File.Create(filePath);
            using var writer = new BinaryWriter(stream, Encoding.UTF8);

            // Magic number + version
            writer.Write(Magic);
            writer.Write(Version);

            // Project header
            writer.Write(project.Name);
            writer.Write(project.SampleRate);
            writer.Write(project.Channels);
            writer.Write(project.Bpm);
            writer.Write(project.TimeSignatureNumerator);
            writer.Write(project.TimeSignatureDenominator);

            // Track count
            var tracks = project.Mixer.Tracks;
            writer.Write(tracks.Count);

            foreach (var track in tracks)
            {
                writer.Write(track.Name);
                writer.Write(track.Volume);
                writer.Write(track.Pan);
                writer.Write(track.IsMuted);
                writer.Write(track.IsSoloed);

                // Clips
                writer.Write(track.Clips.Count);
                foreach (var clip in track.Clips)
                {
                    writer.Write(clip.Name);
                    writer.Write(clip.StartFrame);
                    writer.Write(clip.TrimStartFrame);
                    writer.Write(clip.DurationFrames);
                    writer.Write(clip.Volume);

                    // Write audio data
                    if (clip.SourceBuffer != null)
                    {
                        writer.Write(true); // Has audio data
                        writer.Write(clip.SourceBuffer.FrameCount);
                        writer.Write(clip.SourceBuffer.Channels);

                        // Write raw float samples
                        unsafe
                        {
                            int byteCount = clip.SourceBuffer.TotalSamples * sizeof(float);
                            byte[] rawBytes = new byte[byteCount];
                            fixed (byte* dst = rawBytes)
                            {
                                Buffer.MemoryCopy(clip.SourceBuffer.Ptr, dst,
                                    byteCount, byteCount);
                            }
                            writer.Write(byteCount);
                            writer.Write(rawBytes);
                        }
                    }
                    else
                    {
                        writer.Write(false);
                    }
                }
            }

            project.FilePath = filePath;
        }

        public static DawProject Load(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            // Verify magic
            byte[] magic = reader.ReadBytes(4);
            if (!magic.SequenceEqual(Magic))
                throw new InvalidDataException("Not a WaveCraft project file.");

            ushort version = reader.ReadUInt16();
            if (version > Version)
                throw new InvalidDataException($"Unsupported version: {version}");

            var project = new DawProject();

            // Header
            project.Name = reader.ReadString();
            project.SampleRate = reader.ReadInt32();
            project.Channels = reader.ReadInt32();
            project.Bpm = reader.ReadSingle();
            project.TimeSignatureNumerator = reader.ReadInt32();
            project.TimeSignatureDenominator = reader.ReadInt32();

            // Tracks
            int trackCount = reader.ReadInt32();
            for (int t = 0; t < trackCount; t++)
            {
                string trackName = reader.ReadString();
                float volume = reader.ReadSingle();
                float pan = reader.ReadSingle();
                bool muted = reader.ReadBoolean();
                bool soloed = reader.ReadBoolean();

                var track = project.Mixer.AddTrack(trackName);
                track.Volume = volume;
                track.Pan = pan;
                track.IsMuted = muted;
                track.IsSoloed = soloed;

                int clipCount = reader.ReadInt32();
                for (int c = 0; c < clipCount; c++)
                {
                    var clip = new AudioClip
                    {
                        Name = reader.ReadString(),
                        StartFrame = reader.ReadInt64(),
                        TrimStartFrame = reader.ReadInt64(),
                        DurationFrames = reader.ReadInt64(),
                        Volume = reader.ReadSingle()
                    };

                    bool hasAudio = reader.ReadBoolean();
                    if (hasAudio)
                    {
                        int frameCount = reader.ReadInt32();
                        int channels = reader.ReadInt32();
                        int byteCount = reader.ReadInt32();
                        byte[] rawBytes = reader.ReadBytes(byteCount);

                        var buffer = new AudioBuffer(frameCount, channels);
                        unsafe
                        {
                            fixed (byte* src = rawBytes)
                            {
                                Buffer.MemoryCopy(src, buffer.Ptr,
                                    byteCount, byteCount);
                            }
                        }

                        clip.SourceBuffer = buffer;
                    }

                    track.Clips.Add(clip);
                }
            }

            project.FilePath = filePath;
            return project;
        }
    }
}