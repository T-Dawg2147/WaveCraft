using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using WaveCraft.Core.Analysis;
using WaveCraft.Core.Audio;
using WaveCraft.Core.Tracks;
using WaveCraft.Mvvm;
using WaveCraft.Services;

namespace WaveCraft.ViewModels
{
    /// <summary>
    /// ViewModel for a single audio track.
    /// Wraps the AudioTrack model and exposes bindable properties + commands.
    /// </summary>
    public class TrackViewModel : ViewModelBase
    {
        private readonly AudioTrack _track;
        private readonly IDialogService _dialogService;
        private readonly IProjectService _projectService;
        private readonly IEventAggregator _events;

        private string _name;
        private float _volume;
        private float _pan;
        private bool _isMuted;
        private bool _isSoloed;
        private float _peakLevel;

        public AudioTrack Model => _track;
        public ObservableCollection<ClipViewModel> Clips { get; } = new();

        public string Name
        {
            get => _name;
            set { if (SetProperty(ref _name, value)) _track.Name = value; }
        }

        public float Volume
        {
            get => _volume;
            set
            {
                if (SetProperty(ref _volume, Math.Clamp(value, 0f, 2f)))
                {
                    _track.Volume = _volume;
                    OnPropertyChanged(nameof(VolumeDb));
                }
            }
        }

        public string VolumeDb
        {
            get
            {
                if (_volume < 0.0001f) return "-∞ dB";
                float db = 20f * MathF.Log10(_volume);
                return $"{db:F1} dB";
            }
        }

        public float Pan
        {
            get => _pan;
            set { if (SetProperty(ref _pan, Math.Clamp(value, -1f, 1f))) _track.Pan = _pan; }
        }

        public string PanLabel
        {
            get
            {
                if (MathF.Abs(_pan) < 0.01f) return "C";
                return _pan < 0 ? $"L{(int)(-_pan * 100)}" : $"R{(int)(_pan * 100)}";
            }
        }

        public bool IsMuted
        {
            get => _isMuted;
            set { if (SetProperty(ref _isMuted, value)) _track.IsMuted = value; }
        }

        public bool IsSoloed
        {
            get => _isSoloed;
            set { if (SetProperty(ref _isSoloed, value)) _track.IsSoloed = value; }
        }

        public float PeakLevel
        {
            get => _peakLevel;
            set => SetProperty(ref _peakLevel, value);
        }

        // ---- Commands ----
        public ICommand ImportAudioCommand { get; }
        public ICommand RemoveClipCommand { get; }
        public ICommand ToggleMuteCommand { get; }
        public ICommand ToggleSoloCommand { get; }

        public TrackViewModel(AudioTrack track, IDialogService dialogService,
            IProjectService projectService, IEventAggregator events)
        {
            _track = track;
            _dialogService = dialogService;
            _projectService = projectService;
            _events = events;

            _name = track.Name;
            _volume = track.Volume;
            _pan = track.Pan;
            _isMuted = track.IsMuted;
            _isSoloed = track.IsSoloed;

            ImportAudioCommand = new RelayCommand(ImportAudio);
            RemoveClipCommand = new RelayCommand(p => RemoveClip(p as ClipViewModel));
            ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
            ToggleSoloCommand = new RelayCommand(() => IsSoloed = !IsSoloed);

            // Load existing clips
            foreach (var clip in track.Clips)
                Clips.Add(new ClipViewModel(clip, _projectService.CurrentProject.SampleRate));
        }

        private void ImportAudio()
        {
            var path = _dialogService.ShowOpenFileDialog(
                "Audio Files|*.wav;*.wave", "Import Audio");
            if (path == null) return;

            try
            {
                var (buffer, format) = WavFileReader.LoadFromFile(path);

                var clip = new AudioClip
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    SourceBuffer = buffer,
                    StartFrame = GetNextAvailableFrame()
                };

                _track.Clips.Add(clip);
                Clips.Add(new ClipViewModel(clip,
                    _projectService.CurrentProject.SampleRate));

                _events.Publish(new TrackClipsChanged(0));
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Failed to import audio:\n{ex.Message}");
            }
        }

        private long GetNextAvailableFrame()
        {
            long maxEnd = 0;
            foreach (var clip in _track.Clips)
            {
                if (clip.EndFrame > maxEnd) maxEnd = clip.EndFrame;
            }
            return maxEnd;
        }

        private void RemoveClip(ClipViewModel? clipVm)
        {
            if (clipVm == null) return;
            _track.Clips.Remove(clipVm.Model);
            Clips.Remove(clipVm);
            _events.Publish(new TrackClipsChanged(0));
        }
    }

    /// <summary>
    /// ViewModel for a single audio clip on a track.
    /// </summary>
    public class ClipViewModel : ViewModelBase
    {
        private readonly AudioClip _clip;
        private WaveformData? _waveformData;

        public AudioClip Model => _clip;

        public string Name
        {
            get => _clip.Name;
            set { _clip.Name = value; OnPropertyChanged(); }
        }

        public long StartFrame
        {
            get => _clip.StartFrame;
            set { _clip.StartFrame = value; OnPropertyChanged(); OnPropertyChanged(nameof(StartSeconds)); }
        }

        public double StartSeconds => (double)_clip.StartFrame / _sampleRate;
        public double DurationSeconds => (double)_clip.EffectiveDuration / _sampleRate;

        public float ClipVolume
        {
            get => _clip.Volume;
            set { _clip.Volume = Math.Clamp(value, 0f, 2f); OnPropertyChanged(); }
        }

        public WaveformData? WaveformData
        {
            get => _waveformData;
            private set => SetProperty(ref _waveformData, value);
        }

        private readonly int _sampleRate;

        public ClipViewModel(AudioClip clip, int sampleRate)
        {
            _clip = clip;
            _sampleRate = sampleRate;
            GenerateWaveform();
        }

        /// <summary>
        /// Generate waveform peak data for the clip's visual display.
        /// </summary>
        public void GenerateWaveform(int columns = 400)
        {
            if (_clip.SourceBuffer == null) return;
            WaveformData = WaveformGenerator.Generate(_clip.SourceBuffer, 0, columns);
        }
    }
}