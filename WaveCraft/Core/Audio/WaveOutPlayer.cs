using System.Runtime.InteropServices;

namespace WaveCraft.Core.Audio
{
    /// <summary>
    /// Real-time audio output using Windows winmm.dll (waveOut API).
    /// This class uses P/Invoke to send rendered audio directly to the sound card.
    /// No external dependencies required - uses built-in Windows audio APIs.
    /// </summary>
    public class WaveOutPlayer : IDisposable
    {
        private IntPtr _waveOutHandle = IntPtr.Zero;
        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly int _bufferSize; // In frames
        private readonly int _bufferCount = 3; // Triple buffering for smooth playback
        
        private WaveOutBuffer?[] _buffers;
        private bool _isPlaying;
        private volatile bool _disposed;
        
        // Callback to request audio data from the engine
        private readonly Func<float[], int, bool> _renderCallback;
        
        // Temporary buffer for float-to-int16 conversion
        private readonly float[] _floatBuffer;
        
        // Wave callback delegate
        private readonly WaveOutProc _waveOutProc;
        
        public WaveOutPlayer(int sampleRate, int channels, int bufferSize,
            Func<float[], int, bool> renderCallback)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _bufferSize = bufferSize;
            _renderCallback = renderCallback;
            _buffers = new WaveOutBuffer[_bufferCount];
            _floatBuffer = new float[bufferSize * channels];
            _waveOutProc = new WaveOutProc(WaveOutCallback);
        }

        public void Start()
        {
            if (_isPlaying) return;

            // Set up wave format (16-bit PCM)
            var waveFormat = new WaveFormat
            {
                wFormatTag = 1, // PCM
                nChannels = (ushort)_channels,
                nSamplesPerSec = (uint)_sampleRate,
                nAvgBytesPerSec = (uint)(_sampleRate * _channels * 2),
                nBlockAlign = (ushort)(_channels * 2),
                wBitsPerSample = 16,
                cbSize = 0
            };

            // Open the wave output device
            int result = waveOutOpen(out _waveOutHandle, WAVE_MAPPER,
                ref waveFormat, _waveOutProc, IntPtr.Zero, CALLBACK_FUNCTION);

            if (result != 0)
                throw new InvalidOperationException($"Failed to open wave output device. Error: {result}");

            // Create and prepare buffers
            int bytesPerBuffer = _bufferSize * _channels * 2; // 16-bit = 2 bytes per sample
            for (int i = 0; i < _bufferCount; i++)
            {
                _buffers[i] = new WaveOutBuffer(_waveOutHandle, bytesPerBuffer);
            }

            _isPlaying = true;

            // Queue initial buffers
            for (int i = 0; i < _bufferCount; i++)
            {
                FillAndQueueBuffer(_buffers[i]!);
            }
        }

        public void Stop()
        {
            if (!_isPlaying) return;
            _isPlaying = false;

            if (_waveOutHandle != IntPtr.Zero)
            {
                waveOutReset(_waveOutHandle);
                waveOutClose(_waveOutHandle);
                _waveOutHandle = IntPtr.Zero;
            }

            // Clean up buffers
            if (_buffers != null)
            {
                foreach (var buffer in _buffers)
                {
                    buffer?.Dispose();
                }
                _buffers = new WaveOutBuffer[_bufferCount];
            }
        }

        private void FillAndQueueBuffer(WaveOutBuffer buffer)
        {
            if (_disposed || !_isPlaying) return;

            // Request audio data from the engine
            bool hasData = _renderCallback(_floatBuffer, _bufferSize);
            
            if (!hasData)
            {
                // If no data, fill with silence
                Array.Clear(_floatBuffer, 0, _floatBuffer.Length);
            }

            // Convert float audio to 16-bit PCM
            buffer.ConvertAndWrite(_floatBuffer);

            // Queue the buffer for playback
            buffer.Queue();
        }

        // Wave callback - called by Windows when a buffer has finished playing
        private void WaveOutCallback(IntPtr hWaveOut, uint uMsg, IntPtr dwInstance,
            IntPtr dwParam1, IntPtr dwParam2)
        {
            if (uMsg == MM_WOM_DONE && _isPlaying && !_disposed)
            {
                // Find which buffer completed
                foreach (var buffer in _buffers)
                {
                    if (buffer != null && buffer.HeaderPtr == dwParam1)
                    {
                        // Refill and requeue this buffer
                        FillAndQueueBuffer(buffer);
                        break;
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        #region P/Invoke declarations

        private const int WAVE_MAPPER = -1;
        private const int CALLBACK_FUNCTION = 0x30000;
        private const uint MM_WOM_DONE = 0x3BD;

        private delegate void WaveOutProc(IntPtr hWaveOut, uint uMsg, IntPtr dwInstance,
            IntPtr dwParam1, IntPtr dwParam2);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct WaveFormat
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHeader
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID,
            ref WaveFormat lpFormat, WaveOutProc dwCallback, IntPtr dwInstance, int dwFlags);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hWaveOut);

        #endregion

        /// <summary>
        /// Wrapper for a single audio buffer sent to the wave output device.
        /// </summary>
        private class WaveOutBuffer : IDisposable
        {
            private readonly IntPtr _waveOutHandle;
            private IntPtr _headerPtr;
            private IntPtr _dataPtr;
            private readonly int _bufferSize;
            private GCHandle _headerHandle;
            private bool _isPrepared;

            public IntPtr HeaderPtr => _headerPtr;

            public WaveOutBuffer(IntPtr waveOutHandle, int bufferSize)
            {
                _waveOutHandle = waveOutHandle;
                _bufferSize = bufferSize;

                // Allocate unmanaged memory for the buffer data
                _dataPtr = Marshal.AllocHGlobal(bufferSize);

                // Create and pin the header
                var header = new WaveHeader
                {
                    lpData = _dataPtr,
                    dwBufferLength = (uint)bufferSize,
                    dwFlags = 0
                };

                _headerHandle = GCHandle.Alloc(header, GCHandleType.Pinned);
                _headerPtr = _headerHandle.AddrOfPinnedObject();

                // Prepare the header
                int result = waveOutPrepareHeader(_waveOutHandle, _headerPtr,
                    Marshal.SizeOf(typeof(WaveHeader)));
                if (result == 0)
                    _isPrepared = true;
            }

            public unsafe void ConvertAndWrite(float[] floatData)
            {
                // Convert float samples (-1.0 to 1.0) to 16-bit PCM
                short* ptr = (short*)_dataPtr;
                int sampleCount = Math.Min(floatData.Length, _bufferSize / 2);
                
                for (int i = 0; i < sampleCount; i++)
                {
                    float sample = floatData[i];
                    // Fast inline clamping - assumes input is mostly within range
                    if (sample > 1.0f) sample = 1.0f;
                    else if (sample < -1.0f) sample = -1.0f;
                    ptr[i] = (short)(sample * 32767f);
                }
            }

            public void Queue()
            {
                if (!_isPrepared) return;
                waveOutWrite(_waveOutHandle, _headerPtr, Marshal.SizeOf(typeof(WaveHeader)));
            }

            public void Dispose()
            {
                if (_isPrepared && _waveOutHandle != IntPtr.Zero)
                {
                    waveOutUnprepareHeader(_waveOutHandle, _headerPtr,
                        Marshal.SizeOf(typeof(WaveHeader)));
                    _isPrepared = false;
                }

                if (_headerHandle.IsAllocated)
                    _headerHandle.Free();

                if (_dataPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_dataPtr);
                    _dataPtr = IntPtr.Zero;
                }
            }
        }
    }
}
