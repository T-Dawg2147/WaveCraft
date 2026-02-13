using System.IO;
using System.Text;

namespace WaveCraft.Core.Audio
{
    /// <summary>
    /// Writes audio buffers to WAV files using binary output.
    /// Supports 16-bit and 24-bit PCM export, plus 32-bit float.
    /// </summary>
    public static class WavFileWriter
    {
        public static void SaveToFile(string filePath, AudioBuffer buffer,
            AudioFormat format)
        {
            using var stream = File.Create(filePath);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);

            int bitsPerSample = format.BitsPerSample;
            int channels = buffer.Channels;
            int frames = buffer.FrameCount;
            int bytesPerSample = bitsPerSample / 8;
            int dataSize = frames * channels * bytesPerSample;

            bool isFloat = bitsPerSample == 32;
            short audioFormat = isFloat ? (short)3 : (short)1;

            // ---- RIFF header ----
            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + dataSize);          // File size - 8
            writer.Write("WAVE".ToCharArray());

            // ---- fmt chunk ----
            writer.Write("fmt ".ToCharArray());
            writer.Write(16);                      // Chunk size
            writer.Write(audioFormat);             // 1=PCM, 3=float
            writer.Write((short)channels);
            writer.Write(format.SampleRate);
            writer.Write(format.ByteRate);         // Byte rate
            writer.Write((short)format.BytesPerFrame); // Block align
            writer.Write((short)bitsPerSample);

            // ---- data chunk ----
            writer.Write("data".ToCharArray());
            writer.Write(dataSize);

            // ---- Write sample data ----
            unsafe
            {
                float* src = buffer.Ptr;
                int totalSamples = frames * channels;

                switch (bitsPerSample)
                {
                    case 16:
                        for (int i = 0; i < totalSamples; i++)
                        {
                            float clamped = Math.Clamp(src[i], -1.0f, 1.0f);
                            short sample = (short)(clamped * 32767);
                            writer.Write(sample);
                        }
                        break;

                    case 24:
                        for (int i = 0; i < totalSamples; i++)
                        {
                            float clamped = Math.Clamp(src[i], -1.0f, 1.0f);
                            int sample = (int)(clamped * 8388607);
                            unchecked
                            {
                                writer.Write((byte)(sample & 0xFF));
                                writer.Write((byte)((sample >> 8) & 0xFF));
                                writer.Write((byte)((sample >> 16) & 0xFF));
                            }
                        }
                        break;

                    case 32:
                        // Write raw float bytes directly
                        byte[] rawBytes = new byte[totalSamples * sizeof(float)];
                        fixed (byte* pDest = rawBytes)
                        {
                            Buffer.MemoryCopy(src, pDest,
                                rawBytes.Length, rawBytes.Length);
                        }
                        writer.Write(rawBytes);
                        break;
                }
            }
        }
    }
}