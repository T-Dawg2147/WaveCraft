using System.Collections.Concurrent;
using WaveCraft.Core.Tracks;
using WaveCraft.Mvvm;

namespace WaveCraft.Core.Audio
{
    /// <summary>
    /// The real-time audio rendering engine. Runs on a dedicated thread
    /// and renders audio blocks at a fixed interval.
    ///
    /// PROFESSIONAL CONCEPT: The audio thread is the most time-critical
    /// thread in any audio application. It must produce blocks of audio
    /// at exact intervals (e.g., every 11.6ms for 512 frames at 44100Hz).
    /// Missing a deadline causes an audible glitch. Therefore:
    ///   - No heap allocations (no new, no LINQ, no closures)
    ///   - No locks (use lock-free queues for communication)
    ///   - No I/O (no file access, no console writes)
    ///   - No exceptions
    ///
    /// Communication with the UI thread uses ConcurrentQueue — a lock-free
    /// data structure that both threads can safely access simultaneously.
    /// </summary>
    public class AudioEngine : IDisposable
    {
        private readonly TrackMixer _mixer;
        private readonly IEventAggregator _events;
        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly int _bufferSize; // Frames per render block

        // Transport state
        private long _playbackPosition; // Current position in frames
        private volatile bool _isPlaying;
        private volatile bool _isPaused;

        // Render thread
        private Thread? _renderThread;
        private volatile bool _shouldStop;
        private readonly ManualResetEventSlim _playEvent = new(false);

        // Lock-free communication: audio thread → UI thread
        // The audio thread pushes meter data here; the UI thread polls it.
        private readonly ConcurrentQueue<MeterData> _meterQueue = new();

        // Lock-free communication: UI thread → audio thread
        // The UI sends seek/transport commands here.
        private readonly ConcurrentQueue<EngineCommand> _commandQueue = new();

        // The rendered audio output (double-buffered)
        private AudioBuffer? _outputBufferA;
        private AudioBuffer? _outputBufferB;
        private volatile int _activeBuffer; // 0 = A, 1 = B

        // Real audio output to speakers
        private WaveOutPlayer? _waveOutPlayer;

        public long PlaybackPosition => _playbackPosition;
        public bool IsPlaying => _isPlaying;
        public bool IsPaused => _isPaused;
        public int SampleRate => _sampleRate;
        public int BufferSize => _bufferSize;

        public AudioEngine(TrackMixer mixer, IEventAggregator events,
            int sampleRate = 44100, int channels = 2, int bufferSize = 1024)
        {
            _mixer = mixer;
            _events = events;
            _sampleRate = sampleRate;
            _channels = channels;
            _bufferSize = bufferSize;

            _outputBufferA = new AudioBuffer(bufferSize, channels);
            _outputBufferB = new AudioBuffer(bufferSize, channels);

            // Create the wave output player
            _waveOutPlayer = new WaveOutPlayer(sampleRate, channels, bufferSize, RenderAudioCallback);
        }

        /// <summary>
        /// Start the render thread.
        /// </summary>
        public void Start()
        {
            if (_renderThread != null) return;

            _shouldStop = false;
            _renderThread = new Thread(RenderLoop)
            {
                Name = "AudioRenderThread",
                IsBackground = true,
                Priority = ThreadPriority.Highest
            };
            _renderThread.Start();
        }

        public void Play()
        {
            _commandQueue.Enqueue(new EngineCommand(CommandType.Play));
            _waveOutPlayer?.Start();
        }

        public void Pause()
        {
            _commandQueue.Enqueue(new EngineCommand(CommandType.Pause));
            _waveOutPlayer?.Stop();
        }

        public void Stop()
        {
            _commandQueue.Enqueue(new EngineCommand(CommandType.Stop));
            _waveOutPlayer?.Stop();
        }

        public void Seek(long frame)
        {
            _commandQueue.Enqueue(new EngineCommand(CommandType.Seek, frame));
        }

        /// <summary>
        /// Called by the UI thread (~60Hz) to poll meter data.
        /// </summary>
        public MeterData? PollMeterData()
        {
            MeterData? latest = null;
            // Drain the queue, keep only the latest reading
            while (_meterQueue.TryDequeue(out var data))
                latest = data;
            return latest;
        }

        /// <summary>
        /// Get the current output buffer for playback.
        /// </summary>
        public AudioBuffer GetCurrentOutputBuffer()
            => _activeBuffer == 0 ? _outputBufferA! : _outputBufferB!;

        /// <summary>
        /// Audio callback invoked by WaveOutPlayer when it needs more audio data.
        /// This is called from the audio driver's callback thread.
        /// </summary>
        private unsafe bool RenderAudioCallback(float[] buffer, int frameCount)
        {
            if (!_isPlaying || _isPaused)
            {
                // Return silence if not playing
                return false;
            }

            // Render the next block of audio
            var output = _mixer.RenderBlock(_playbackPosition, frameCount,
                _channels, _sampleRate);

            // Copy to the output buffer
            fixed (float* destPtr = buffer)
            {
                float* srcPtr = output.Ptr;
                int sampleCount = frameCount * _channels;
                for (int i = 0; i < sampleCount; i++)
                {
                    destPtr[i] = srcPtr[i];
                }
            }

            // Send meter data to the UI (lock-free)
            _meterQueue.Enqueue(new MeterData(
                _mixer.LastLeftPeak, _mixer.LastRightPeak,
                _mixer.LastLeftRms, _mixer.LastRightRms,
                _playbackPosition));

            // Publish position update
            _events.Publish(new PlaybackPositionChanged(
                TimeSpan.FromSeconds((double)_playbackPosition / _sampleRate),
                _playbackPosition));

            // Advance playback position
            _playbackPosition += frameCount;

            // Check if we've reached the end of the project
            long totalFrames = _mixer.GetTotalDurationFrames();
            if (totalFrames > 0 && _playbackPosition >= totalFrames)
            {
                _isPlaying = false;
                _playbackPosition = 0;
                _waveOutPlayer?.Stop();
                _events.Publish(new TransportStateChanged(
                    TransportState.Stopped, TimeSpan.Zero));
                return false;
            }

            return true;
        }

        /// <summary>
        /// The render loop — runs continuously on the audio thread.
        /// </summary>
        private void RenderLoop()
        {
            double msPerBlock = 1000.0 * _bufferSize / _sampleRate;

            while (!_shouldStop)
            {
                // Process any commands from the UI thread
                while (_commandQueue.TryDequeue(out var cmd))
                {
                    switch (cmd.Type)
                    {
                        case CommandType.Play:
                            _isPlaying = true;
                            _isPaused = false;
                            _playEvent.Set();
                            _events.Publish(new TransportStateChanged(
                                TransportState.Playing,
                                TimeSpan.FromSeconds((double)_playbackPosition / _sampleRate)));
                            break;

                        case CommandType.Pause:
                            _isPaused = true;
                            _isPlaying = false;
                            _playEvent.Reset();
                            _events.Publish(new TransportStateChanged(
                                TransportState.Paused,
                                TimeSpan.FromSeconds((double)_playbackPosition / _sampleRate)));
                            break;

                        case CommandType.Stop:
                            _isPlaying = false;
                            _isPaused = false;
                            _playbackPosition = 0;
                            _mixer.ResetAll();
                            _playEvent.Reset();
                            _events.Publish(new TransportStateChanged(
                                TransportState.Stopped, TimeSpan.Zero));
                            break;

                        case CommandType.Seek:
                            _playbackPosition = cmd.SeekFrame;
                            _mixer.ResetAll();
                            break;
                    }
                }

                if (!_isPlaying)
                {
                    // Wait until we get a Play command
                    _playEvent.Wait(100);
                    continue;
                }

                // Render the next block of audio
                var output = _mixer.RenderBlock(_playbackPosition, _bufferSize,
                    _channels, _sampleRate);

                // Copy to the inactive buffer, then swap
                var targetBuffer = _activeBuffer == 0 ? _outputBufferB! : _outputBufferA!;
                targetBuffer.CopyFrom(output);
                _activeBuffer = _activeBuffer == 0 ? 1 : 0;

                // Send meter data to the UI (lock-free)
                _meterQueue.Enqueue(new MeterData(
                    _mixer.LastLeftPeak, _mixer.LastRightPeak,
                    _mixer.LastLeftRms, _mixer.LastRightRms,
                    _playbackPosition));

                // Publish position update
                _events.Publish(new PlaybackPositionChanged(
                    TimeSpan.FromSeconds((double)_playbackPosition / _sampleRate),
                    _playbackPosition));

                // Advance playback position
                _playbackPosition += _bufferSize;

                // Check if we've reached the end of the project
                long totalFrames = _mixer.GetTotalDurationFrames();
                if (totalFrames > 0 && _playbackPosition >= totalFrames)
                {
                    _isPlaying = false;
                    _playbackPosition = 0;
                    _playEvent.Reset();
                    _events.Publish(new TransportStateChanged(
                        TransportState.Stopped, TimeSpan.Zero));
                }

                // Sleep to approximate real-time
                // In a real DAW, this would be driven by the audio driver's callback.
                Thread.Sleep(Math.Max(1, (int)(msPerBlock * 0.8)));
            }
        }

        public void Dispose()
        {
            _shouldStop = true;
            _playEvent.Set(); // Wake the thread if it's waiting
            _waveOutPlayer?.Dispose();
            _renderThread?.Join(2000);
            _outputBufferA?.Dispose();
            _outputBufferB?.Dispose();
            _playEvent.Dispose();
        }
    }

    // ---- Lock-free message types ----

    public record MeterData(
        float LeftPeak, float RightPeak,
        float LeftRms, float RightRms,
        long PlaybackFrame);

    public record EngineCommand(CommandType Type, long SeekFrame = 0);

    public enum CommandType { Play, Pause, Stop, Seek }
}