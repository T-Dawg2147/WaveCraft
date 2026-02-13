using System.Runtime.InteropServices;
using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Effects
{
    /// <summary>
    /// Simple reverb using multiple comb filters and all-pass filters.
    /// This is the Schroeder reverb algorithm — one of the foundational
    /// reverb designs in audio DSP.
    ///
    /// PROFESSIONAL CONCEPT: Real reverbs layer multiple delay lines
    /// with prime-number lengths to simulate the dense, randomised
    /// reflections in a physical space.
    ///
    /// NOTE: We store filter buffer pointers as IntPtr[] instead of float*[]
    /// because C# does not allow pointer types as generic type arguments
    /// (CS0306). We cast to float* when accessing individual buffers.
    /// </summary>
    public unsafe class ReverbEffect : IAudioEffect
    {
        public string Name => "Reverb";
        public bool IsEnabled { get; set; } = true;

        [AudioParameter("Room Size", 0.1f, 1.0f, 0.7f, "")]
        public float RoomSize { get; set; } = 0.7f;

        [AudioParameter("Damping", 0f, 1f, 0.5f, "")]
        public float Damping { get; set; } = 0.5f;

        [AudioParameter("Mix", 0f, 1f, 0.25f, "")]
        public float Mix { get; set; } = 0.25f;

        // Comb filter delay lengths (in samples at 44100 Hz)
        // Using prime-ish numbers to avoid resonance patterns
        private static readonly int[] CombLengths = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        private static readonly int[] AllPassLengths = { 556, 441, 341, 225 };

        // We use IntPtr[] instead of float*[] because pointer types
        // cannot be used as generic type arguments in C#.
        // Each IntPtr points to unmanaged memory holding float samples.
        private IntPtr[] _combBuffers = Array.Empty<IntPtr>();
        private int[] _combLengthsScaled = Array.Empty<int>();
        private float[] _combFilterStore = Array.Empty<float>();
        private int[] _combPos = Array.Empty<int>();

        private IntPtr[] _allPassBuffers = Array.Empty<IntPtr>();
        private int[] _allPassLengthsScaled = Array.Empty<int>();
        private int[] _allPassPos = Array.Empty<int>();

        private bool _initialized;

        private void Initialize(int sampleRate)
        {
            if (_initialized) Cleanup();

            float scale = sampleRate / 44100f;

            // ---- Allocate comb filter buffers ----
            _combBuffers = new IntPtr[CombLengths.Length];
            _combLengthsScaled = new int[CombLengths.Length];
            _combFilterStore = new float[CombLengths.Length];
            _combPos = new int[CombLengths.Length];

            for (int i = 0; i < CombLengths.Length; i++)
            {
                int len = (int)(CombLengths[i] * scale);
                _combLengthsScaled[i] = len;
                int bytes = len * sizeof(float);

                _combBuffers[i] = Marshal.AllocHGlobal(bytes);
                _combPos[i] = 0;
                _combFilterStore[i] = 0f;

                // Zero the buffer
                float* buf = (float*)_combBuffers[i];
                for (int j = 0; j < len; j++)
                    buf[j] = 0f;
            }

            // ---- Allocate all-pass filter buffers ----
            _allPassBuffers = new IntPtr[AllPassLengths.Length];
            _allPassLengthsScaled = new int[AllPassLengths.Length];
            _allPassPos = new int[AllPassLengths.Length];

            for (int i = 0; i < AllPassLengths.Length; i++)
            {
                int len = (int)(AllPassLengths[i] * scale);
                _allPassLengthsScaled[i] = len;
                int bytes = len * sizeof(float);

                _allPassBuffers[i] = Marshal.AllocHGlobal(bytes);
                _allPassPos[i] = 0;

                // Zero the buffer
                float* buf = (float*)_allPassBuffers[i];
                for (int j = 0; j < len; j++)
                    buf[j] = 0f;
            }

            _initialized = true;
        }

        public void Process(AudioBuffer buffer, int sampleRate)
        {
            if (!_initialized) Initialize(sampleRate);

            float* src = buffer.Ptr;
            int total = buffer.TotalSamples;
            int channels = buffer.Channels;
            float wet = Mix;
            float dry = 1.0f - Mix;

            for (int i = 0; i < total; i += channels)
            {
                // Mix stereo to mono input for the reverb
                float input = 0;
                for (int ch = 0; ch < channels; ch++)
                    input += src[i + ch];
                input /= channels;

                // Sum of all comb filters (parallel)
                float combOutput = 0;
                for (int c = 0; c < CombLengths.Length; c++)
                {
                    int len = _combLengthsScaled[c];
                    float* buf = (float*)_combBuffers[c];
                    int pos = _combPos[c];

                    float delayed = buf[pos];

                    // Low-pass filter in the feedback path (damping)
                    _combFilterStore[c] = delayed * (1f - Damping) +
                                          _combFilterStore[c] * Damping;

                    buf[pos] = input + _combFilterStore[c] * RoomSize;

                    _combPos[c] = (pos + 1) % len;
                    combOutput += delayed;
                }

                // All-pass filters (in series)
                float allPassOutput = combOutput;
                for (int a = 0; a < AllPassLengths.Length; a++)
                {
                    int len = _allPassLengthsScaled[a];
                    float* buf = (float*)_allPassBuffers[a];
                    int pos = _allPassPos[a];

                    float delayed = buf[pos];
                    float output = -allPassOutput + delayed;
                    buf[pos] = allPassOutput + delayed * 0.5f;

                    _allPassPos[a] = (pos + 1) % len;
                    allPassOutput = output;
                }

                // Mix dry and wet, apply to all channels
                for (int ch = 0; ch < channels; ch++)
                    src[i + ch] = src[i + ch] * dry + allPassOutput * wet;
            }
        }

        public void Reset()
        {
            Cleanup();
            _initialized = false;
        }

        private void Cleanup()
        {
            // Free all comb buffers
            if (_combBuffers != null)
            {
                for (int i = 0; i < _combBuffers.Length; i++)
                {
                    if (_combBuffers[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_combBuffers[i]);
                        _combBuffers[i] = IntPtr.Zero;
                    }
                }
            }

            // Free all all-pass buffers
            if (_allPassBuffers != null)
            {
                for (int i = 0; i < _allPassBuffers.Length; i++)
                {
                    if (_allPassBuffers[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_allPassBuffers[i]);
                        _allPassBuffers[i] = IntPtr.Zero;
                    }
                }
            }

            _combBuffers = Array.Empty<IntPtr>();
            _allPassBuffers = Array.Empty<IntPtr>();
            _combLengthsScaled = Array.Empty<int>();
            _allPassLengthsScaled = Array.Empty<int>();
            _combFilterStore = Array.Empty<float>();
            _combPos = Array.Empty<int>();
            _allPassPos = Array.Empty<int>();

            _initialized = false;
        }

        public void Dispose() => Cleanup();
    }
}