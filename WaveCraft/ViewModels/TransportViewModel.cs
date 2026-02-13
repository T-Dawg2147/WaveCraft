using System.Windows.Input;
using System.Windows.Threading;
using WaveCraft.Core.Audio;
using WaveCraft.Mvvm;
using WaveCraft.Services;

namespace WaveCraft.ViewModels
{
    /// <summary>
    /// ViewModel for the transport controls (play, pause, stop, seek)
    /// and the position display.
    ///
    /// FIX: Position display now updates immediately on stop/seek/pause,
    /// not just when meter data arrives from the audio engine.
    /// </summary>
    public class TransportViewModel : ViewModelBase
    {
        private readonly IProjectService _projectService;
        private readonly IEventAggregator _events;
        private AudioEngine? _engine;

        private string _positionText = "00:00.000";
        private string _barBeatText = "1:01:000";
        private string _durationText = "00:00.000";
        private bool _isPlaying;
        private bool _isPaused;
        private float _bpm = 120f;

        // The current playback position in frames — always kept in sync
        private long _currentFrame;

        // Playhead position as a normalised value (0.0 to 1.0) for UI binding
        private double _playheadPosition;

        // UI polling timer
        private readonly DispatcherTimer _pollTimer;

        // Meter levels
        private float _leftPeak;
        private float _rightPeak;
        private float _leftRms;
        private float _rightRms;

        // ---- Properties ----

        public string PositionText
        {
            get => _positionText;
            set => SetProperty(ref _positionText, value);
        }

        public string BarBeatText
        {
            get => _barBeatText;
            set => SetProperty(ref _barBeatText, value);
        }

        public string DurationText
        {
            get => _durationText;
            set => SetProperty(ref _durationText, value);
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set => SetProperty(ref _isPlaying, value);
        }

        public bool IsPaused
        {
            get => _isPaused;
            set => SetProperty(ref _isPaused, value);
        }

        public float Bpm
        {
            get => _bpm;
            set
            {
                if (SetProperty(ref _bpm, Math.Clamp(value, 20f, 300f)))
                    _projectService.CurrentProject.Bpm = _bpm;
            }
        }

        /// <summary>
        /// Current frame position — can be set by the UI to seek.
        /// </summary>
        public long CurrentFrame
        {
            get => _currentFrame;
            set
            {
                if (SetProperty(ref _currentFrame, Math.Max(0, value)))
                {
                    RefreshPositionDisplay();
                    OnPropertyChanged(nameof(PlayheadPosition));
                }
            }
        }

        /// <summary>
        /// Playhead position as a 0.0–1.0 normalised value.
        /// Bound to the playhead slider/indicator in the timeline.
        /// </summary>
        public double PlayheadPosition
        {
            get => _playheadPosition;
            set
            {
                double clamped = Math.Clamp(value, 0.0, 1.0);
                if (SetProperty(ref _playheadPosition, clamped))
                {
                    // Convert normalised position to frame and seek
                    long totalFrames = GetTotalFrames();
                    if (totalFrames > 0)
                    {
                        _currentFrame = (long)(clamped * totalFrames);
                        _engine?.Seek(_currentFrame);
                        RefreshPositionDisplay();
                        OnPropertyChanged(nameof(CurrentFrame));
                    }
                }
            }
        }

        /// <summary>
        /// Current position as a TimeSpan (for binding).
        /// </summary>
        public TimeSpan CurrentTime =>
            TimeSpan.FromSeconds((double)_currentFrame /
                _projectService.CurrentProject.SampleRate);

        /// <summary>
        /// Total project duration as a TimeSpan.
        /// </summary>
        public TimeSpan TotalDuration =>
            _projectService.CurrentProject.TotalDuration;

        public float LeftPeak
        {
            get => _leftPeak;
            set => SetProperty(ref _leftPeak, value);
        }

        public float RightPeak
        {
            get => _rightPeak;
            set => SetProperty(ref _rightPeak, value);
        }

        public float LeftRms
        {
            get => _leftRms;
            set => SetProperty(ref _leftRms, value);
        }

        public float RightRms
        {
            get => _rightRms;
            set => SetProperty(ref _rightRms, value);
        }

        // ---- Commands ----
        public ICommand PlayCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand SkipToStartCommand { get; }
        public ICommand SkipToEndCommand { get; }
        public ICommand SeekToFrameCommand { get; }

        // ---- Constructor ----

        public TransportViewModel(IProjectService projectService,
            IEventAggregator events)
        {
            _projectService = projectService;
            _events = events;

            PlayCommand = new RelayCommand(OnPlay, () => !_isPlaying);
            PauseCommand = new RelayCommand(OnPause, () => _isPlaying);
            StopCommand = new RelayCommand(OnStop);
            SkipToStartCommand = new RelayCommand(OnSkipToStart);
            SkipToEndCommand = new RelayCommand(OnSkipToEnd);
            SeekToFrameCommand = new RelayCommand(p =>
            {
                if (p is long frame) SeekToFrame(frame);
                else if (p is string s && long.TryParse(s, out long f)) SeekToFrame(f);
            });

            // Subscribe to transport events from the audio engine
            _events.Subscribe<TransportStateChanged>(OnTransportStateChanged);
            _events.Subscribe<PlaybackPositionChanged>(OnPlaybackPositionChanged);

            // Poll meter data at ~30 FPS
            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _pollTimer.Tick += PollMeterData;
            _pollTimer.Start();

            // Initial display
            RefreshPositionDisplay();
            RefreshDurationDisplay();
        }

        public void SetEngine(AudioEngine engine)
        {
            _engine = engine;
        }

        // ---- Transport actions ----

        private void OnPlay()
        {
            _engine?.Play();
            IsPlaying = true;
            IsPaused = false;
            RelayCommand.RaiseCanExecuteChanged();
        }

        private void OnPause()
        {
            _engine?.Pause();
            IsPlaying = false;
            IsPaused = true;

            // Immediately refresh the position from the engine
            if (_engine != null)
            {
                _currentFrame = _engine.PlaybackPosition;
                RefreshPositionDisplay();
                UpdatePlayheadFromFrame();
            }

            RelayCommand.RaiseCanExecuteChanged();
        }

        private void OnStop()
        {
            _engine?.Stop();
            IsPlaying = false;
            IsPaused = false;

            // Reset to beginning
            _currentFrame = 0;
            RefreshPositionDisplay();
            UpdatePlayheadFromFrame();

            // Reset meters
            LeftPeak = 0;
            RightPeak = 0;
            LeftRms = 0;
            RightRms = 0;

            RelayCommand.RaiseCanExecuteChanged();
        }

        private void OnSkipToStart()
        {
            SeekToFrame(0);
        }

        private void OnSkipToEnd()
        {
            SeekToFrame(GetTotalFrames());
        }

        /// <summary>
        /// Seek to an exact frame position. Updates engine, display, and playhead.
        /// </summary>
        public void SeekToFrame(long frame)
        {
            frame = Math.Max(0, frame);
            _engine?.Seek(frame);
            _currentFrame = frame;
            RefreshPositionDisplay();
            UpdatePlayheadFromFrame();
            OnPropertyChanged(nameof(CurrentFrame));
            OnPropertyChanged(nameof(CurrentTime));
        }

        /// <summary>
        /// Seek to a time position.
        /// </summary>
        public void SeekToTime(TimeSpan time)
        {
            int sampleRate = _projectService.CurrentProject.SampleRate;
            long frame = (long)(time.TotalSeconds * sampleRate);
            SeekToFrame(frame);
        }

        // ---- Position display helpers ----

        /// <summary>
        /// Refresh the position text displays from _currentFrame.
        /// Called after every seek, stop, pause, and during playback polling.
        /// </summary>
        private void RefreshPositionDisplay()
        {
            var project = _projectService.CurrentProject;
            double seconds = (double)_currentFrame / project.SampleRate;
            var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));

            PositionText = $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
            BarBeatText = project.FrameToBarBeatTick(_currentFrame);

            RefreshDurationDisplay();
        }

        private void RefreshDurationDisplay()
        {
            var duration = _projectService.CurrentProject.TotalDuration;
            DurationText = $"{(int)duration.TotalMinutes:D2}:" +
                           $"{duration.Seconds:D2}.{duration.Milliseconds:D3}";
        }

        /// <summary>
        /// Update the normalised playhead position from the current frame.
        /// Does NOT trigger a seek (avoids feedback loop).
        /// </summary>
        private void UpdatePlayheadFromFrame()
        {
            long total = GetTotalFrames();
            double normalized = total > 0 ? (double)_currentFrame / total : 0;
            // Set the backing field directly to avoid triggering SeekToFrame
            SetProperty(ref _playheadPosition, Math.Clamp(normalized, 0, 1),
                nameof(PlayheadPosition));
        }

        private long GetTotalFrames()
        {
            long frames = _projectService.CurrentProject.TotalFrames;
            // Ensure a minimum length so the playhead has room to move
            // even with empty projects (default: 30 seconds)
            if (frames <= 0)
                frames = _projectService.CurrentProject.SampleRate * 30;
            return frames;
        }

        // ---- Event handlers ----

        private void OnTransportStateChanged(TransportStateChanged e)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                IsPlaying = e.State == TransportState.Playing;
                IsPaused = e.State == TransportState.Paused;

                if (e.State == TransportState.Stopped)
                {
                    _currentFrame = 0;
                    RefreshPositionDisplay();
                    UpdatePlayheadFromFrame();
                    LeftPeak = 0; RightPeak = 0;
                    LeftRms = 0; RightRms = 0;
                }

                RelayCommand.RaiseCanExecuteChanged();
            });
        }

        private void OnPlaybackPositionChanged(PlaybackPositionChanged e)
        {
            // This is called frequently from the audio thread.
            // We handle position updates in PollMeterData instead
            // to avoid flooding the UI thread.
        }

        /// <summary>
        /// Called ~30× per second on the UI thread.
        /// Reads meter data and updates position during playback.
        /// </summary>
        private void PollMeterData(object? sender, EventArgs e)
        {
            if (_engine == null) return;

            var meter = _engine.PollMeterData();

            if (meter != null)
            {
                // Update meters with smooth decay
                LeftPeak = Math.Max(meter.LeftPeak, _leftPeak * 0.85f);
                RightPeak = Math.Max(meter.RightPeak, _rightPeak * 0.85f);
                LeftRms = meter.LeftRms;
                RightRms = meter.RightRms;

                // Update position from the audio engine during playback
                if (_isPlaying)
                {
                    _currentFrame = meter.PlaybackFrame;
                    RefreshPositionDisplay();
                    UpdatePlayheadFromFrame();
                    OnPropertyChanged(nameof(CurrentFrame));
                    OnPropertyChanged(nameof(CurrentTime));
                }
            }
            else
            {
                // Decay meters when not receiving data
                LeftPeak *= 0.92f;
                RightPeak *= 0.92f;
                LeftRms *= 0.92f;
                RightRms *= 0.92f;

                if (LeftPeak < 0.001f) LeftPeak = 0;
                if (RightPeak < 0.001f) RightPeak = 0;
                if (LeftRms < 0.001f) LeftRms = 0;
                if (RightRms < 0.001f) RightRms = 0;
            }

            // Always refresh duration (tracks may have changed)
            RefreshDurationDisplay();
        }

        protected override void OnDispose()
        {
            _pollTimer.Stop();
            _events.Unsubscribe<TransportStateChanged>(OnTransportStateChanged);
            _events.Unsubscribe<PlaybackPositionChanged>(OnPlaybackPositionChanged);
        }
    }
}