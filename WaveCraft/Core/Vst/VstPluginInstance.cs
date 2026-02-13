using System.Runtime.InteropServices;
using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Tracks
{
    /// <summary>
    /// Represents a loaded VST2 plugin instance.
    ///
    /// PROFESSIONAL CONCEPT: VST plugins are native C/C++ DLLs that follow
    /// a standard binary interface. To host them from C#, we use P/Invoke
    /// and Marshal.GetDelegateForFunctionPointer to call into native code.
    ///
    /// VST2 Communication:
    ///   Host → Plugin: via the "dispatcher" function pointer
    ///   Plugin → Host: via a "hostCallback" function pointer we provide
    ///   Audio: via "processReplacing" function pointer
    /// </summary>
    public unsafe class VstPluginInstance : IDisposable
    {
        // ---- VST2 Constants ----
        private const int VstMagic = 0x56737450; // "VstP"

        // Opcodes for the dispatcher function
        private const int effOpen = 0;
        private const int effClose = 1;
        private const int effSetSampleRate = 10;
        private const int effSetBlockSize = 11;
        private const int effMainsChanged = 12;
        private const int effProcessEvents = 25;
        private const int effGetEffectName = 45;
        private const int effGetVendorString = 47;
        private const int effGetProductString = 48;
        private const int effGetParamName = 29;
        private const int effGetParamDisplay = 30;
        private const int effGetParamLabel = 31;

        // MIDI event type
        private const int kVstMidiType = 1;

        // ---- Native function pointer delegates ----

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr VstPluginMain(IntPtr hostCallback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr DispatcherProc(
            IntPtr effect, int opcode, int index, IntPtr value, IntPtr ptr, float opt);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ProcessProc(
            IntPtr effect, float** inputs, float** outputs, int sampleFrames);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetParameterProc(IntPtr effect, int index, float value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate float GetParameterProc(IntPtr effect, int index);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr HostCallbackProc(
            IntPtr effect, int opcode, int index, IntPtr value, IntPtr ptr, float opt);

        // ---- VST2 AEffect structure ----
        [StructLayout(LayoutKind.Sequential)]
        private struct AEffect
        {
            public int Magic;
            public IntPtr Dispatcher;
            public IntPtr ProcessDeprecated;
            public IntPtr SetParameter;
            public IntPtr GetParameter;
            public int NumPrograms;
            public int NumParams;
            public int NumInputs;
            public int NumOutputs;
            public int Flags;
            public IntPtr ResvdHost1;
            public IntPtr ResvdHost2;
            public int InitialDelay;
            public int RealQualities;
            public int OffQualities;
            public float IORatio;
            public IntPtr Object;
            public IntPtr User;
            public int UniqueID;
            public int Version;
            public IntPtr ProcessReplacing;
            public IntPtr ProcessDoubleReplacing;
        }

        // ---- MIDI event structures for VST ----
        [StructLayout(LayoutKind.Sequential)]
        private struct VstMidiEvent
        {
            public int Type;           // kVstMidiType = 1
            public int ByteSize;       // sizeof(VstMidiEvent)
            public int DeltaFrames;    // Sample offset
            public int Flags;
            public int NoteLength;
            public int NoteOffset;
            public byte MidiData0;     // Status byte
            public byte MidiData1;     // Data1
            public byte MidiData2;     // Data2
            public byte MidiData3;     // Padding
            public byte Detune;
            public byte NoteOffVelocity;
            public byte Reserved1;
            public byte Reserved2;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VstEvents
        {
            public int NumEvents;
            public IntPtr Reserved;
            // Followed by array of IntPtr to events
            // We'll handle this manually with Marshal
        }

        // ---- Instance state ----
        private IntPtr _libraryHandle;
        private IntPtr _effectPtr;
        private AEffect* _effect;
        private DispatcherProc? _dispatcher;
        private ProcessProc? _processReplacing;
        private SetParameterProc? _setParameter;
        private GetParameterProc? _getParameter;
        private HostCallbackProc? _hostCallback;
        private GCHandle _hostCallbackHandle;

        // Audio buffer handles (for freeing memory)
        private IntPtr[] _outputBufferHandles = Array.Empty<IntPtr>();
        private IntPtr[] _inputBufferHandles = Array.Empty<IntPtr>();

        // Audio buffer pointers — stored as IntPtr because float*
        // cannot be used as a generic type argument (CS0306).
        // Cast to float* at the point of use inside unsafe blocks.
        private IntPtr[] _outputPtrs = Array.Empty<IntPtr>();
        private IntPtr[] _inputPtrs = Array.Empty<IntPtr>();

        private int _blockSize;
        private int _sampleRate;
        private bool _disposed;

        // Thread-safe MIDI event queue
        private readonly List<(byte Status, byte Data1, byte Data2)> _pendingMidiEvents = new();
        private readonly object _midiLock = new();

        // Pre-allocated buffer for VstEvents structure
        private IntPtr _vstEventsBuffer;
        private IntPtr[] _midiEventPtrs = Array.Empty<IntPtr>();
        private const int MaxMidiEventsPerBlock = 256;

        public string PluginName { get; private set; } = "Unknown";
        public string VendorName { get; private set; } = "Unknown";
        public string ProductName { get; private set; } = "Unknown";
        public int NumParameters => _effect != null ? _effect->NumParams : 0;
        public int NumInputs => _effect != null ? _effect->NumInputs : 0;
        public int NumOutputs => _effect != null ? _effect->NumOutputs : 0;
        public bool IsLoaded => _effect != null;

        // ---- P/Invoke for loading native DLLs ----

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        /// <summary>
        /// Load a VST2 plugin from a DLL file.
        /// </summary>
        public static VstPluginInstance? LoadPlugin(string dllPath,
            int sampleRate = 44100, int blockSize = 1024)
        {
            var instance = new VstPluginInstance();

            try
            {
                instance._sampleRate = sampleRate;
                instance._blockSize = blockSize;

                // Load the native DLL
                instance._libraryHandle = LoadLibrary(dllPath);
                if (instance._libraryHandle == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new DllNotFoundException(
                        $"Failed to load VST DLL: {dllPath} (Win32 error: {err})");
                }

                // Find the plugin's main entry point
                IntPtr mainProc = GetProcAddress(instance._libraryHandle, "VSTPluginMain");
                if (mainProc == IntPtr.Zero)
                    mainProc = GetProcAddress(instance._libraryHandle, "main");
                if (mainProc == IntPtr.Zero)
                    throw new EntryPointNotFoundException(
                        "Could not find VSTPluginMain or main entry point.");

                var pluginMain = Marshal.GetDelegateForFunctionPointer<VstPluginMain>(mainProc);

                // Create and pin the host callback so GC won't collect it
                instance._hostCallback = instance.HostCallback;
                instance._hostCallbackHandle = GCHandle.Alloc(instance._hostCallback);
                IntPtr hostCallbackPtr = Marshal.GetFunctionPointerForDelegate(
                    instance._hostCallback);

                // Call the plugin's main function — creates the AEffect
                instance._effectPtr = pluginMain(hostCallbackPtr);
                if (instance._effectPtr == IntPtr.Zero)
                    throw new InvalidOperationException("Plugin returned null AEffect.");

                instance._effect = (AEffect*)instance._effectPtr;

                // Verify magic number
                if (instance._effect->Magic != VstMagic)
                    throw new InvalidOperationException(
                        $"Invalid VST magic: 0x{instance._effect->Magic:X8}");

                // Get function pointers from the AEffect
                instance._dispatcher =
                    Marshal.GetDelegateForFunctionPointer<DispatcherProc>(
                        instance._effect->Dispatcher);

                if (instance._effect->SetParameter != IntPtr.Zero)
                    instance._setParameter =
                        Marshal.GetDelegateForFunctionPointer<SetParameterProc>(
                            instance._effect->SetParameter);

                if (instance._effect->GetParameter != IntPtr.Zero)
                    instance._getParameter =
                        Marshal.GetDelegateForFunctionPointer<GetParameterProc>(
                            instance._effect->GetParameter);

                if (instance._effect->ProcessReplacing != IntPtr.Zero)
                    instance._processReplacing =
                        Marshal.GetDelegateForFunctionPointer<ProcessProc>(
                            instance._effect->ProcessReplacing);

                // Initialise the plugin
                instance.Dispatch(effOpen, 0, IntPtr.Zero, IntPtr.Zero, 0);
                instance.Dispatch(effSetSampleRate, 0, IntPtr.Zero, IntPtr.Zero, sampleRate);
                instance.Dispatch(effSetBlockSize, 0, (IntPtr)blockSize, IntPtr.Zero, 0);
                instance.Dispatch(effMainsChanged, 0, (IntPtr)1, IntPtr.Zero, 0);

                // Read plugin info
                instance.PluginName = instance.GetStringInfo(effGetEffectName);
                instance.VendorName = instance.GetStringInfo(effGetVendorString);
                instance.ProductName = instance.GetStringInfo(effGetProductString);

                // Allocate audio buffers
                instance.AllocateBuffers();

                // Allocate MIDI event buffer
                instance.AllocateMidiBuffers();

                return instance;
            }
            catch
            {
                instance.Dispose();
                throw;
            }
        }

        private void AllocateBuffers()
        {
            int numOutputs = Math.Max(_effect->NumOutputs, 2);
            _outputBufferHandles = new IntPtr[numOutputs];
            _outputPtrs = new IntPtr[numOutputs];

            for (int i = 0; i < numOutputs; i++)
            {
                _outputBufferHandles[i] = Marshal.AllocHGlobal(_blockSize * sizeof(float));
                _outputPtrs[i] = _outputBufferHandles[i]; // IntPtr, cast to float* when used
            }

            int numInputs = Math.Max(_effect->NumInputs, 0);
            _inputBufferHandles = new IntPtr[numInputs];
            _inputPtrs = new IntPtr[numInputs];

            for (int i = 0; i < numInputs; i++)
            {
                _inputBufferHandles[i] = Marshal.AllocHGlobal(_blockSize * sizeof(float));
                _inputPtrs[i] = _inputBufferHandles[i];

                // Zero input buffers
                float* buf = (float*)_inputPtrs[i];
                for (int f = 0; f < _blockSize; f++)
                    buf[f] = 0f;
            }
        }

        private void AllocateMidiBuffers()
        {
            // Pre-allocate a buffer for VstEvents + event pointer array
            int headerSize = sizeof(VstEvents);
            int ptrArraySize = MaxMidiEventsPerBlock * IntPtr.Size;
            _vstEventsBuffer = Marshal.AllocHGlobal(headerSize + ptrArraySize);

            // Pre-allocate individual MIDI event structs
            _midiEventPtrs = new IntPtr[MaxMidiEventsPerBlock];
            for (int i = 0; i < MaxMidiEventsPerBlock; i++)
            {
                _midiEventPtrs[i] = Marshal.AllocHGlobal(sizeof(VstMidiEvent));
            }
        }

        // ---- MIDI input ----

        public void SendNoteOn(int noteNumber, int velocity)
        {
            lock (_midiLock)
            {
                _pendingMidiEvents.Add(((byte)0x90, (byte)noteNumber, (byte)velocity));
            }
        }

        public void SendNoteOff(int noteNumber)
        {
            lock (_midiLock)
            {
                _pendingMidiEvents.Add(((byte)0x80, (byte)noteNumber, 0));
            }
        }

        public void SendControlChange(int controller, int value)
        {
            lock (_midiLock)
            {
                _pendingMidiEvents.Add(((byte)0xB0, (byte)controller, (byte)value));
            }
        }

        /// <summary>
        /// Flush pending MIDI events to the plugin via the dispatcher.
        /// Builds VstEvents structure in pre-allocated memory — zero heap allocation.
        /// </summary>
        private void SendPendingMidiEvents()
        {
            List<(byte, byte, byte)> events;
            lock (_midiLock)
            {
                if (_pendingMidiEvents.Count == 0) return;
                events = new List<(byte, byte, byte)>(_pendingMidiEvents);
                _pendingMidiEvents.Clear();
            }

            int count = Math.Min(events.Count, MaxMidiEventsPerBlock);

            // Fill pre-allocated VstMidiEvent structs
            for (int i = 0; i < count; i++)
            {
                var (status, data1, data2) = events[i];
                var midiEvent = (VstMidiEvent*)_midiEventPtrs[i];

                midiEvent->Type = kVstMidiType;
                midiEvent->ByteSize = sizeof(VstMidiEvent);
                midiEvent->DeltaFrames = 0;
                midiEvent->Flags = 0;
                midiEvent->NoteLength = 0;
                midiEvent->NoteOffset = 0;
                midiEvent->MidiData0 = status;
                midiEvent->MidiData1 = data1;
                midiEvent->MidiData2 = data2;
                midiEvent->MidiData3 = 0;
                midiEvent->Detune = 0;
                midiEvent->NoteOffVelocity = 0;
                midiEvent->Reserved1 = 0;
                midiEvent->Reserved2 = 0;
            }

            // Build VstEvents header
            var vstEvents = (VstEvents*)_vstEventsBuffer;
            vstEvents->NumEvents = count;
            vstEvents->Reserved = IntPtr.Zero;

            // Write event pointers after the header
            IntPtr* eventPtrArray = (IntPtr*)((byte*)_vstEventsBuffer + sizeof(VstEvents));
            for (int i = 0; i < count; i++)
            {
                eventPtrArray[i] = _midiEventPtrs[i];
            }

            // Send to plugin
            Dispatch(effProcessEvents, 0, IntPtr.Zero, _vstEventsBuffer, 0);
        }

        // ---- Audio processing ----

        /// <summary>
        /// Process audio through the VST plugin.
        /// Sends pending MIDI events, then calls processReplacing.
        /// </summary>
        public void ProcessAudio(AudioBuffer output)
        {
            if (_processReplacing == null || _effect == null) return;

            int frames = Math.Min(output.FrameCount, _blockSize);

            // Send any queued MIDI events
            SendPendingMidiEvents();

            // Clear output buffers
            for (int i = 0; i < _outputPtrs.Length; i++)
            {
                float* buf = (float*)_outputPtrs[i];
                for (int f = 0; f < frames; f++)
                    buf[f] = 0f;
            }

            // Build float** arrays on the stack for the native call.
            // We can't use fixed() on IntPtr[] to get float**, so we
            // stackalloc a temporary float*[] and fill it from our IntPtrs.
            int numIn = _inputPtrs.Length;
            int numOut = _outputPtrs.Length;

            float** inPtrs = stackalloc float*[Math.Max(numIn, 1)];
            float** outPtrs = stackalloc float*[Math.Max(numOut, 1)];

            for (int i = 0; i < numIn; i++)
                inPtrs[i] = (float*)_inputPtrs[i];
            for (int i = 0; i < numOut; i++)
                outPtrs[i] = (float*)_outputPtrs[i];

            // Call the plugin's audio processing
            _processReplacing(_effectPtr, inPtrs, outPtrs, frames);

            // Interleave plugin output into our buffer
            float* dest = output.Ptr;
            int channels = output.Channels;

            if (numOut >= 2 && channels == 2)
            {
                float* left = (float*)_outputPtrs[0];
                float* right = (float*)_outputPtrs[1];
                for (int f = 0; f < frames; f++)
                {
                    dest[f * 2] += left[f];
                    dest[f * 2 + 1] += right[f];
                }
            }
            else if (numOut >= 1)
            {
                float* mono = (float*)_outputPtrs[0];
                for (int f = 0; f < frames; f++)
                {
                    for (int ch = 0; ch < channels; ch++)
                        dest[f * channels + ch] += mono[f];
                }
            }
        }

        // ---- Parameter access ----

        public void SetParameter(int index, float value)
        {
            if (_setParameter != null && _effect != null)
                _setParameter(_effectPtr, index, Math.Clamp(value, 0f, 1f));
        }

        public float GetParameter(int index)
        {
            if (_getParameter != null && _effect != null)
                return _getParameter(_effectPtr, index);
            return 0f;
        }

        /// <summary>
        /// Get the display name of a parameter.
        /// </summary>
        public string GetParameterName(int index)
            => GetIndexedStringInfo(effGetParamName, index);

        /// <summary>
        /// Get the display value of a parameter (e.g. "440 Hz", "-6.0 dB").
        /// </summary>
        public string GetParameterDisplay(int index)
            => GetIndexedStringInfo(effGetParamDisplay, index);

        /// <summary>
        /// Get the unit label of a parameter (e.g. "Hz", "dB", "%").
        /// </summary>
        public string GetParameterLabel(int index)
            => GetIndexedStringInfo(effGetParamLabel, index);

        /// <summary>
        /// Get all parameter info as a list (for auto-generating UI).
        /// </summary>
        public List<VstParameterInfo> GetAllParameters()
        {
            var list = new List<VstParameterInfo>();
            int count = NumParameters;

            for (int i = 0; i < count; i++)
            {
                list.Add(new VstParameterInfo
                {
                    Index = i,
                    Name = GetParameterName(i),
                    Value = GetParameter(i),
                    Display = GetParameterDisplay(i),
                    Label = GetParameterLabel(i)
                });
            }

            return list;
        }

        public void Reset()
        {
            Dispatch(effMainsChanged, 0, IntPtr.Zero, IntPtr.Zero, 0);
            Dispatch(effMainsChanged, 0, (IntPtr)1, IntPtr.Zero, 0);

            lock (_midiLock)
                _pendingMidiEvents.Clear();
        }

        // ---- Host callback ----

        /// <summary>
        /// Called BY the plugin when it needs information from the host.
        /// This is the reverse direction of the dispatcher — the plugin
        /// calls us with opcodes asking for tempo, sample rate, etc.
        /// </summary>
        private IntPtr HostCallback(IntPtr effect, int opcode, int index,
            IntPtr value, IntPtr ptr, float opt)
        {
            // Host callback opcodes
            const int audioMasterVersion = 1;
            const int audioMasterCurrentId = 2;
            const int audioMasterGetSampleRate = 16;
            const int audioMasterGetBlockSize = 17;
            const int audioMasterGetCurrentProcessLevel = 23;
            const int audioMasterGetAutomationState = 24;
            const int audioMasterGetVendorString = 32;
            const int audioMasterGetProductString = 33;
            const int audioMasterGetVendorVersion = 34;
            const int audioMasterCanDo = 37;
            const int audioMasterGetLanguage = 38;
            const int audioMasterGetTime = 7;

            switch (opcode)
            {
                case audioMasterVersion:
                    return (IntPtr)2400; // We support VST 2.4

                case audioMasterCurrentId:
                    return IntPtr.Zero;

                case audioMasterGetSampleRate:
                    return (IntPtr)_sampleRate;

                case audioMasterGetBlockSize:
                    return (IntPtr)_blockSize;

                case audioMasterGetCurrentProcessLevel:
                    return (IntPtr)2; // kVstProcessLevelRealtime

                case audioMasterGetAutomationState:
                    return (IntPtr)1; // Off

                case audioMasterGetVendorString:
                    if (ptr != IntPtr.Zero)
                        WriteStringToPtr(ptr, "WaveCraft");
                    return (IntPtr)1;

                case audioMasterGetProductString:
                    if (ptr != IntPtr.Zero)
                        WriteStringToPtr(ptr, "WaveCraft DAW");
                    return (IntPtr)1;

                case audioMasterGetVendorVersion:
                    return (IntPtr)1000;

                case audioMasterGetLanguage:
                    return (IntPtr)1; // English

                case audioMasterCanDo:
                    // Return 1 for capabilities we support
                    return (IntPtr)1;

                default:
                    return IntPtr.Zero;
            }
        }

        // ---- Utility helpers ----

        private IntPtr Dispatch(int opcode, int index, IntPtr value,
            IntPtr ptr, float opt)
        {
            if (_dispatcher == null || _effect == null) return IntPtr.Zero;
            return _dispatcher(_effectPtr, opcode, index, value, ptr, opt);
        }

        private string GetStringInfo(int opcode)
        {
            IntPtr buffer = Marshal.AllocHGlobal(256);
            try
            {
                // Zero the buffer
                for (int i = 0; i < 256; i++)
                    ((byte*)buffer)[i] = 0;

                Dispatch(opcode, 0, IntPtr.Zero, buffer, 0);
                return Marshal.PtrToStringAnsi(buffer) ?? "Unknown";
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private string GetIndexedStringInfo(int opcode, int index)
        {
            IntPtr buffer = Marshal.AllocHGlobal(256);
            try
            {
                for (int i = 0; i < 256; i++)
                    ((byte*)buffer)[i] = 0;

                Dispatch(opcode, index, IntPtr.Zero, buffer, 0);
                return Marshal.PtrToStringAnsi(buffer) ?? "";
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static void WriteStringToPtr(IntPtr ptr, string value)
        {
            byte* dest = (byte*)ptr;
            int len = Math.Min(value.Length, 63);
            for (int i = 0; i < len; i++)
                dest[i] = (byte)value[i];
            dest[len] = 0; // Null terminator
        }

        // ---- Cleanup ----

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Close the plugin
            if (_dispatcher != null && _effect != null)
            {
                try
                {
                    Dispatch(effMainsChanged, 0, IntPtr.Zero, IntPtr.Zero, 0);
                    Dispatch(effClose, 0, IntPtr.Zero, IntPtr.Zero, 0);
                }
                catch { /* Best effort cleanup */ }
            }

            // Free audio buffers
            foreach (var handle in _outputBufferHandles)
                if (handle != IntPtr.Zero) Marshal.FreeHGlobal(handle);
            foreach (var handle in _inputBufferHandles)
                if (handle != IntPtr.Zero) Marshal.FreeHGlobal(handle);

            // Free MIDI buffers
            if (_vstEventsBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(_vstEventsBuffer);
            foreach (var ptr in _midiEventPtrs)
                if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);

            // Release host callback pin
            if (_hostCallbackHandle.IsAllocated)
                _hostCallbackHandle.Free();

            // Unload the native DLL
            if (_libraryHandle != IntPtr.Zero)
                FreeLibrary(_libraryHandle);

            _effect = null;
            _dispatcher = null;
            _processReplacing = null;
        }
    }

    /// <summary>
    /// Info about a single VST plugin parameter.
    /// </summary>
    public class VstParameterInfo
    {
        public int Index { get; init; }
        public string Name { get; init; } = "";
        public float Value { get; set; }
        public string Display { get; init; } = "";
        public string Label { get; init; } = "";
    }
}