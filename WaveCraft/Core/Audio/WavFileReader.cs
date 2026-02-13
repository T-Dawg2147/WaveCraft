using System.IO;
using System.Text;

namespace WaveCraft.Core.Audio
{
    /// <summary>
    /// Reads WAV files using low-level binary parsing.
    /// Supports 8-bit, 16-bit, 24-bit, 32-bit integer and 32-bit float formats.
    ///
    /// PROFESSIONAL CONCEPT: Real audio software reads WAV files at the byte
    /// level — not through wrapper libraries — because you need full control
    /// over format conversion and error handling.
    /// </summary>
    public static class WavFileReader
    {
        public static (AudioBuffer buffer, AudioFormat format) LoadFromFile(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

            // ---- RIFF header ----
            string riffId = new string(reader.ReadChars(4));
            if (riffId != "RIFF")
                throw new InvalidDataException($"Not a RIFF file (got '{riffId}').");

            int fileSize = reader.ReadInt32(); // File size - 8
            string waveId = new string(reader.ReadChars(4));
            if (waveId != "WAVE")
                throw new InvalidDataException($"Not a WAVE file (got '{waveId}').");

            // ---- Find chunks ----
            int sampleRate = 0, channels = 0, bitsPerSample = 0;
            int audioFormat = 0;
            byte[]? rawData = null;

            while (stream.Position < stream.Length - 8)
            {
                string chunkId = new string(reader.ReadChars(4));
                int chunkSize = reader.ReadInt32();

                switch (chunkId)
                {
                    case "fmt ":
                        audioFormat = reader.ReadInt16();    // 1=PCM, 3=IEEE float
                        channels = reader.ReadInt16();
                        sampleRate = reader.ReadInt32();
                        int byteRate = reader.ReadInt32();    // Bytes per second
                        int blockAlign = reader.ReadInt16();   // Bytes per frame
                        bitsPerSample = reader.ReadInt16();

                        // Skip any extra fmt bytes
                        int extraFmt = chunkSize - 16;
                        if (extraFmt > 0)
                            reader.ReadBytes(extraFmt);
                        break;

                    case "data":
                        rawData = reader.ReadBytes(chunkSize);
                        break;

                    default:
                        // Skip unknown chunks (LIST, INFO, etc.)
                        reader.ReadBytes(chunkSize);
                        break;
                }
            }

            if (rawData == null)
                throw new InvalidDataException("No 'data' chunk found in WAV file.");

            if (channels == 0 || sampleRate == 0)
                throw new InvalidDataException("No 'fmt ' chunk found in WAV file.");

            var format = new AudioFormat(sampleRate, channels, bitsPerSample);

            // ---- Convert raw bytes to float samples ----
            int bytesPerSample = bitsPerSample / 8;
            int totalFrames = rawData.Length / (bytesPerSample * channels);
            var buffer = new AudioBuffer(totalFrames, channels);

            unsafe
            {
                fixed (byte* pRaw = rawData)
                {
                    float* pDest = buffer.Ptr;

                    switch (bitsPerSample)
                    {
                        case 8:
                            // 8-bit WAV is unsigned: 0–255, 128 = silence
                            for (int i = 0; i < totalFrames * channels; i++)
                                pDest[i] = (pRaw[i] - 128) / 128.0f;
                            break;

                        case 16:
                            short* pShort = (short*)pRaw;
                            for (int i = 0; i < totalFrames * channels; i++)
                                pDest[i] = pShort[i] / 32768.0f;
                            break;

                        case 24:
                            // 24-bit is stored as 3 bytes per sample, little-endian.
                            // No native 24-bit type — must construct manually.
                            for (int i = 0; i < totalFrames * channels; i++)
                            {
                                int byteOffset = i * 3;
                                unchecked
                                {
                                    int sample = pRaw[byteOffset]
                                               | (pRaw[byteOffset + 1] << 8)
                                               | (pRaw[byteOffset + 2] << 16);

                                    // Sign-extend from 24 to 32 bits
                                    if ((sample & 0x800000) != 0)
                                        sample |= unchecked((int)0xFF000000);

                                    pDest[i] = sample / 8388608.0f; // 2^23
                                }
                            }
                            break;

                        case 32:
                            if (audioFormat == 3)
                            {
                                // IEEE 32-bit float — direct copy
                                float* pFloat = (float*)pRaw;
                                Buffer.MemoryCopy(pFloat, pDest,
                                    totalFrames * channels * sizeof(float),
                                    totalFrames * channels * sizeof(float));
                            }
                            else
                            {
                                // 32-bit integer
                                int* pInt = (int*)pRaw;
                                for (int i = 0; i < totalFrames * channels; i++)
                                    pDest[i] = pInt[i] / 2147483648.0f;
                            }
                            break;

                        default:
                            throw new NotSupportedException(
                                $"Unsupported bit depth: {bitsPerSample}");
                    }
                }
            }

            return (buffer, format);
        }
    }
}