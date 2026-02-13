using System.Collections.ObjectModel;
using System.Windows.Input;
using WaveCraft.Core.Tracks;
using WaveCraft.Mvvm;
using WaveCraft.Services;

namespace WaveCraft.ViewModels
{
    /// <summary>
    /// ViewModel for the mixer panel — manages all tracks,
    /// master volume, and master effects.
    /// </summary>
    public class MixerViewModel : ViewModelBase
    {
        private readonly IProjectService _projectService;
        private readonly IDialogService _dialogService;
        private readonly IEventAggregator _events;
        private TrackMixer _mixer;

        private float _masterVolume = 1.0f;
        private int _selectedTrackIndex = -1;

        public ObservableCollection<TrackViewModel> Tracks { get; } = new();
        public EffectChainViewModel MasterEffects { get; private set; }

        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                if (SetProperty(ref _masterVolume, Math.Clamp(value, 0f, 2f)))
                {
                    _mixer.MasterVolume = _masterVolume;
                    OnPropertyChanged(nameof(MasterVolumeDb));
                }
            }
        }

        public string MasterVolumeDb
        {
            get
            {
                if (_masterVolume < 0.0001f) return "-∞ dB";
                return $"{20f * MathF.Log10(_masterVolume):F1} dB";
            }
        }

        public int SelectedTrackIndex
        {
            get => _selectedTrackIndex;
            set { SetProperty(ref _selectedTrackIndex, value); OnPropertyChanged(nameof(SelectedTrack)); }
        }

        public TrackViewModel? SelectedTrack =>
            _selectedTrackIndex >= 0 && _selectedTrackIndex < Tracks.Count
                ? Tracks[_selectedTrackIndex] : null;

        // ---- Commands ----
        public ICommand AddTrackCommand { get; }
        public ICommand RemoveTrackCommand { get; }
        public ICommand DuplicateTrackCommand { get; }

        public MixerViewModel(IProjectService projectService,
            IDialogService dialogService, IEventAggregator events)
        {
            _projectService = projectService;
            _dialogService = dialogService;
            _events = events;
            _mixer = projectService.CurrentProject.Mixer;

            MasterEffects = new EffectChainViewModel(_mixer.MasterEffects);

            AddTrackCommand = new RelayCommand(AddTrack);
            RemoveTrackCommand = new RelayCommand(RemoveSelectedTrack, () => Tracks.Count > 1);
            DuplicateTrackCommand = new RelayCommand(DuplicateTrack, () => SelectedTrack != null);

            // Subscribe to project changes
            _projectService.ProjectChanged += OnProjectChanged;

            RefreshTracks();
        }

        private void OnProjectChanged(Core.Project.DawProject project)
        {
            _mixer = project.Mixer;
            MasterEffects = new EffectChainViewModel(_mixer.MasterEffects);
            OnPropertyChanged(nameof(MasterEffects));
            RefreshTracks();
        }

        private void RefreshTracks()
        {
            Tracks.Clear();
            foreach (var track in _mixer.Tracks)
            {
                Tracks.Add(new TrackViewModel(track, _dialogService,
                    _projectService, _events));
            }

            if (Tracks.Count > 0 && _selectedTrackIndex < 0)
                SelectedTrackIndex = 0;
        }

        private void AddTrack()
        {
            int num = Tracks.Count + 1;
            var track = _mixer.AddTrack($"Track {num}");
            Tracks.Add(new TrackViewModel(track, _dialogService,
                _projectService, _events));
            SelectedTrackIndex = Tracks.Count - 1;
            RelayCommand.RaiseCanExecuteChanged();
        }

        private void RemoveSelectedTrack()
        {
            if (SelectedTrack == null || Tracks.Count <= 1) return;

            var trackVm = SelectedTrack;
            _mixer.RemoveTrack(trackVm.Model);
            Tracks.Remove(trackVm);
            SelectedTrackIndex = Math.Min(_selectedTrackIndex, Tracks.Count - 1);
            RelayCommand.RaiseCanExecuteChanged();
        }

        private void DuplicateTrack()
        {
            if (SelectedTrack == null) return;

            var source = SelectedTrack.Model;
            var newTrack = _mixer.AddTrack($"{source.Name} (Copy)");
            newTrack.Volume = source.Volume;
            newTrack.Pan = source.Pan;

            Tracks.Add(new TrackViewModel(newTrack, _dialogService,
                _projectService, _events));
            SelectedTrackIndex = Tracks.Count - 1;
        }
    }
}