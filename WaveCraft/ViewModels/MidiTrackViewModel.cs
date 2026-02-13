using System.Collections.ObjectModel;
using System.Windows.Input;
using WaveCraft.Core.Tracks;
using WaveCraft.Core.Midi;
using WaveCraft.Mvvm;
using WaveCraft.Services;

namespace WaveCraft.ViewModels
{
    /// <summary>
    /// ViewModel for a single MIDI track.
    /// Wraps the MidiTrack model and exposes bindable properties + commands.
    /// </summary>
    public class MidiTrackViewModel : ViewModelBase, ITrackViewModel
    {
        private readonly MidiTrack _track;
        private readonly IDialogService _dialogService;
        private readonly IProjectService _projectService;
        private readonly IEventAggregator _events;

        private string _name;
        private float _volume;
        private float _pan;
        private bool _isMuted;
        private bool _isSoloed;
        private float _peakLevel;

        public MidiTrack Model => _track;
        public ObservableCollection<MidiClipViewModel> Clips { get; } = new();

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

        public bool IsMidiTrack => true;

        // ---- Commands ----
        public ICommand OpenPianoRollCommand { get; }
        public ICommand RemoveClipCommand { get; }
        public ICommand ToggleMuteCommand { get; }
        public ICommand ToggleSoloCommand { get; }

        public MidiTrackViewModel(MidiTrack track, IDialogService dialogService,
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

            OpenPianoRollCommand = new RelayCommand(OpenPianoRoll);
            RemoveClipCommand = new RelayCommand(p => RemoveClip(p as MidiClipViewModel));
            ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
            ToggleSoloCommand = new RelayCommand(() => IsSoloed = !IsSoloed);

            // Load existing clips
            foreach (var clip in track.Clips)
                Clips.Add(new MidiClipViewModel(clip, _projectService.CurrentProject.Bpm));
        }

        private void OpenPianoRoll()
        {
            // Get the first clip if available
            if (_track.Clips.Count > 0)
            {
                var clip = _track.Clips[0];
                var pianoRollVm = new PianoRollViewModel { Clip = clip };
                var window = new Views.PianoRollWindow { DataContext = pianoRollVm };
                window.Show();
            }
        }

        private void RemoveClip(MidiClipViewModel? clipVm)
        {
            if (clipVm == null) return;
            _track.Clips.Remove(clipVm.Model);
            Clips.Remove(clipVm);
            _events.Publish(new TrackClipsChanged(0));
        }
    }

    /// <summary>
    /// ViewModel for a single MIDI clip on a track.
    /// </summary>
    public class MidiClipViewModel : ViewModelBase
    {
        private readonly MidiClip _clip;
        private readonly float _bpm;

        public MidiClip Model => _clip;

        public string Name
        {
            get => _clip.Name;
            set { _clip.Name = value; OnPropertyChanged(); }
        }

        public long StartTick
        {
            get => _clip.StartTick;
            set { _clip.StartTick = value; OnPropertyChanged(); OnPropertyChanged(nameof(StartSeconds)); }
        }

        public long LengthTicks
        {
            get => _clip.LengthTicks;
            set { _clip.LengthTicks = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationSeconds)); }
        }

        public double StartSeconds => MidiConstants.TicksToSeconds(_clip.StartTick, _bpm);
        public double DurationSeconds => MidiConstants.TicksToSeconds(_clip.LengthTicks, _bpm);

        public MidiClipViewModel(MidiClip clip, float bpm)
        {
            _clip = clip;
            _bpm = bpm;
        }

        /// <summary>
        /// Get a simple preview of notes for visualization.
        /// Returns a list of (noteNumber, startTick, duration) tuples.
        /// </summary>
        public List<(int noteNumber, long startTick, long duration)> GetNotePreview()
        {
            var preview = new List<(int, long, long)>();
            foreach (var note in _clip.Notes)
            {
                preview.Add((note.NoteNumber, note.StartTick, note.DurationTicks));
            }
            return preview;
        }
    }
}
