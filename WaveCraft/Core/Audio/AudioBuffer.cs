using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WaveCraft.Core.Audio
{
    /// <summary>
    /// A high-performance audio sample buffer stored in unmanaged memory.
    /// All DSP operations use unsafe pointer access for zero-overhead processing.
    ///
    /// Samples are stored as 32-bit floats in the range [-1.0, 1.0].
    /// Interleaved format: [L0, R0, L1, R1, L2, R2, ...]
    ///
    /// PROFESSIONAL CONCEPT: In real DAWs, the audio thread cannot allocate
    /// heap memory (it would trigger GC pauses = audio glitches). So all
    /// buffers are pre-allocated and reused. This class supports that pattern.
    /// </summary>
    public unsafe class AudioBuffer : IDisposable
    {
        private float* _samples;
        private int _frameCount;    // Number of frames (time steps)
        private int _channels;      // Number of channels (1=mono, 2=stereo)
        private int _totalSamples;  // frameCount × channels
        private IntPtr _memoryHandle;
        private bool _disposed;

        public int FrameCount => _frameCount;
        public int Channels => _channels;
        public int TotalSamples => _totalSamples;
        public float* Ptr => _samples;

        /// <summary>
        /// Create a new buffer. Memory is zeroed (silence).
        /// </summary>
        public AudioBuffer(int frameCount, int channels)
        {
            _frameCount = frameCount;
            _channels = channels;
            _totalSamples = frameCount * channels;

            int byteCount = _totalSamples * sizeof(float);
            _memoryHandle = Marshal.AllocHGlobal(byteCount);
            _samples = (float*)_memoryHandle;

            Clear();
        }

        // ---- Sample access ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetSample(int frame, int channel)
        {
            int index = frame * _channels + channel;
            if ((uint)index >= (uint)_totalSamples) return 0f;
            return _samples[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetSample(int frame, int channel, float value)
        {
            int index = frame * _channels + channel;
            if ((uint)index < (uint)_totalSamples)
                _samples[index] = value;
        }

        /// <summary>
        /// Get a pointer to the start of a specific frame.
        /// Used by effects that process frame-by-frame.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float* GetFramePtr(int frame)
            => _samples + frame * _channels;

        /// <summary>
        /// Returns a Span over the raw sample data. Allows safe code to
        /// process the buffer without pointers when performance isn't critical.
        /// </summary>
        public Span<float> AsSpan()
            => new Span<float>(_samples, _totalSamples);

        /// <summary>
        /// Returns a Memory-compatible Span for a sub-range of frames.
        /// </summary>
        public Span<float> GetFrameRange(int startFrame, int frameCount)
        {
            int offset = startFrame * _channels;
            int length = frameCount * _channels;
            length = Math.Min(length, _totalSamples - offset);
            if (offset < 0 || offset >= _totalSamples) return Span<float>.Empty;
            return new Span<float>(_samples + offset, length);
        }

        // ---- Bulk operations (pointer-based for speed) ----

        /// <summary>
        /// Fill the entire buffer with silence (0.0f).
        /// Uses long* writes for 8-bytes-at-a-time zeroing.
        /// </summary>
        public void Clear()
        {
            long* ptr = (long*)_samples;
            int longCount = (_totalSamples * sizeof(float)) / sizeof(long);
            for (int i = 0; i < longCount; i++)
                ptr[i] = 0L;

            // Handle remaining bytes
            int remaining = _totalSamples - (longCount * 2);
            int offset = longCount * 2;
            for (int i = 0; i < remaining; i++)
                _samples[offset + i] = 0f;
        }

        /// <summary>
        /// Copy all samples from another buffer into this one.
        /// Buffers must be the same size.
        /// </summary>
        public void CopyFrom(AudioBuffer source)
        {
            int count = Math.Min(_totalSamples, source._totalSamples);
            Buffer.MemoryCopy(source._samples, _samples,
                count * sizeof(float), count * sizeof(float));
        }

        /// <summary>
        /// Mix (add) another buffer's samples into this buffer.
        /// This is the core operation of a mixer — adding tracks together.
        /// </summary>
        public void MixFrom(AudioBuffer source, float volume = 1.0f)
        {
            int count = Math.Min(_totalSamples, source._totalSamples);
            float* src = source._samples;
            float* dst = _samples;

            if (Math.Abs(volume - 1.0f) < 0.0001f)
            {
                // Unity gain — simple add
                for (int i = 0; i < count; i++)
                    dst[i] += src[i];
            }
            else
            {
                // Scaled add
                for (int i = 0; i < count; i++)
                    dst[i] += src[i] * volume;
            }
        }

        /// <summary>
        /// Apply a gain (volume) to all samples in-place.
        /// </summary>
        public void ApplyGain(float gain)
        {
            for (int i = 0; i < _totalSamples; i++)
                _samples[i] *= gain;
        }

        /// <summary>
        /// Clamp all samples to [-1.0, 1.0] to prevent clipping.
        /// Uses branchless min/max for speed.
        /// </summary>
        public void Clamp()
        {
            for (int i = 0; i < _totalSamples; i++)
            {
                float s = _samples[i];
                s = s > 1.0f ? 1.0f : s;
                s = s < -1.0f ? -1.0f : s;
                _samples[i] = s;
            }
        }

        /// <summary>
        /// Calculate the peak amplitude across all samples.
        /// Used for metering and normalisation.
        /// </summary>
        public (float leftPeak, float rightPeak) GetPeakLevels()
        {
            float leftPeak = 0f, rightPeak = 0f;

            if (_channels == 2)
            {
                for (int i = 0; i < _totalSamples; i += 2)
                {
                    float l = Math.Abs(_samples[i]);
                    float r = Math.Abs(_samples[i + 1]);
                    if (l > leftPeak) leftPeak = l;
                    if (r > rightPeak) rightPeak = r;
                }
            }
            else
            {
                for (int i = 0; i < _totalSamples; i++)
                {
                    float v = Math.Abs(_samples[i]);
                    if (v > leftPeak) leftPeak = v;
                }
                rightPeak = leftPeak;
            }

            return (leftPeak, rightPeak);
        }

        /// <summary>
        /// Calculate the RMS (Root Mean Square) level — a better
        /// representation of perceived loudness than peak.
        /// </summary>
        public (float leftRms, float rightRms) GetRmsLevels()
        {
            double leftSum = 0, rightSum = 0;

            if (_channels == 2)
            {
                for (int i = 0; i < _totalSamples; i += 2)
                {
                    leftSum += _samples[i] * _samples[i];
                    rightSum += _samples[i + 1] * _samples[i + 1];
                }
                int frames = _totalSamples / 2;
                return ((float)Math.Sqrt(leftSum / frames),
                        (float)Math.Sqrt(rightSum / frames));
            }
            else
            {
                for (int i = 0; i < _totalSamples; i++)
                    leftSum += _samples[i] * _samples[i];
                float rms = (float)Math.Sqrt(leftSum / _totalSamples);
                return (rms, rms);
            }
        }

        // ---- Factory methods ----

        /// <summary>
        /// Create a buffer filled with a sine wave. Useful for testing.
        /// </summary>
        public static AudioBuffer CreateSineWave(float frequency, float duration,
            int sampleRate = 44100, int channels = 2, float amplitude = 0.5f)
        {
            int frames = (int)(duration * sampleRate);
            var buffer = new AudioBuffer(frames, channels);

            double phaseIncrement = 2.0 * Math.PI * frequency / sampleRate;
            double phase = 0;

            for (int f = 0; f < frames; f++)
            {
                float sample = (float)(Math.Sin(phase) * amplitude);
                for (int ch = 0; ch < channels; ch++)
                    buffer.SetSample(f, ch, sample);
                phase += phaseIncrement;
            }

            return buffer;
        }

        // ---- Cleanup ----

        public void Dispose()
        {
            if (!_disposed)
            {
                Marshal.FreeHGlobal(_memoryHandle);
                _samples = null;
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        ~AudioBuffer() => Dispose();
    }
}