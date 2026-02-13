using System.Runtime.InteropServices;
using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Effects
{
    /// <summary>
    /// Echo / delay effect using a circular buffer.
    ///
    /// PROFESSIONAL CONCEPT: Circular (ring) buffers are fundamental to
    /// real-time audio. Instead of shifting data, we just move a read/write
    /// pointer around a fixed-size buffer.
    /// </summary>
    public unsafe class DelayEffect : IAudioEffect, IDisposable
    {
        public string Name => "Delay";
        public bool IsEnabled { get; set; } = true;

        [AudioParameter("Delay Time", 10f, 2000f, 300f, "ms")]
        public float DelayMs { get; set; } = 300f;

        [AudioParameter("Feedback", 0f, 0.95f, 0.4f, "")]
        public float Feedback { get; set; } = 0.4f;

        [AudioParameter("Mix", 0f, 1f, 0.3f, "")]
        public float Mix { get; set; } = 0.3f;

        // Circular buffer (unmanaged memory)
        private float* _delayBuffer;
        private int _bufferSize;
        private int _writePos;
        private IntPtr _memHandle;
        private int _allocatedForRate;

        public void Process(AudioBuffer buffer, int sampleRate)
        {
            EnsureBuffer(sampleRate, buffer.Channels);

            int delaySamples = (int)(DelayMs * sampleRate / 1000f) * buffer.Channels;
            delaySamples = Math.Clamp(delaySamples, 1, _bufferSize - 1);

            float* src = buffer.Ptr;
            int total = buffer.TotalSamples;
            float wet = Mix;
            float dry = 1.0f - Mix;
            float fb = Feedback;

            for (int i = 0; i < total; i++)
            {
                // Read from the delay line
                int readPos = _writePos - delaySamples;
                if (readPos < 0) readPos += _bufferSize;

                float delayed = _delayBuffer[readPos];
                float input = src[i];

                // Write input + feedback to the delay line
                _delayBuffer[_writePos] = input + delayed * fb;

                // Output = dry input + wet delayed signal
                src[i] = input * dry + delayed * wet;

                _writePos++;
                if (_writePos >= _bufferSize)
                    _writePos = 0;
            }
        }

        private void EnsureBuffer(int sampleRate, int channels)
        {
            if (_allocatedForRate == sampleRate && _delayBuffer != null)
                return;

            // Allocate enough for max delay time
            _bufferSize = (int)(2.1f * sampleRate) * channels; // ~2 seconds
            int bytes = _bufferSize * sizeof(float);

            if (_memHandle != IntPtr.Zero)
                Marshal.FreeHGlobal(_memHandle);

            _memHandle = Marshal.AllocHGlobal(bytes);
            _delayBuffer = (float*)_memHandle;
            _writePos = 0;
            _allocatedForRate = sampleRate;

            // Clear the buffer
            for (int i = 0; i < _bufferSize; i++)
                _delayBuffer[i] = 0f;
        }

        public void Reset()
        {
            if (_delayBuffer != null && _bufferSize > 0)
            {
                for (int i = 0; i < _bufferSize; i++)
                    _delayBuffer[i] = 0f;
                _writePos = 0;
            }
        }

        public void Dispose()
        {
            if (_memHandle != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_memHandle);
                _memHandle = IntPtr.Zero;
                _delayBuffer = null;
            }
        }
    }
}