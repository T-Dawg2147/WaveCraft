using System.Windows.Input;
using WaveCraft.Core.Audio;
using WaveCraft.Mvvm;
using WaveCraft.Services;
using WaveCraft.Core.Midi;
using WaveCraft.Core.Tracks;
using WaveCraft.Views;
using System.IO;

namespace WaveCraft.ViewModels
{
    /// <summary>
    /// The root ViewModel — orchestrates all child ViewModels,
    /// manages the audio engine lifecycle, and handles global commands.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly IProjectService _projectService;
        private readonly IDialogService _dialogService;
        private readonly IEventAggregator _events;
        private AudioEngine? _engine;

        private string _title = "WaveCraft — Untitled";
        private string _statusText = "Ready";

        private PianoRollViewModel? _pianoRollVm;
        private VstBrowserViewModel? _vstBrowserVm;

        public TransportViewModel Transport { get; }
        public MixerViewModel Mixer { get; }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        // ---- Global commands ----
        public ICommand NewProjectCommand { get; }
        public ICommand OpenProjectCommand { get; }
        public ICommand SaveProjectCommand { get; }
        public ICommand SaveProjectAsCommand { get; }
        public ICommand ExportWavCommand { get; }
        public ICommand ExitCommand { get; }

        public ICommand AddMidiTrackCommand { get; }
        public ICommand OpenPianoRollCommand { get; }
        public ICommand OpenVstBrowserCommand { get; }
        public ICommand ImportMidiCommand { get; }
        public ICommand ExportMidiCommand { get; }

        public MainViewModel(IProjectService projectService,
            IDialogService dialogService, IEventAggregator events)
        {
            _projectService = projectService;
            _dialogService = dialogService;
            _events = events;

            Transport = new TransportViewModel(projectService, events);
            Mixer = new MixerViewModel(projectService, dialogService, events);

            NewProjectCommand = new RelayCommand(NewProject);
            OpenProjectCommand = new RelayCommand(OpenProject);
            SaveProjectCommand = new RelayCommand(SaveProject);
            SaveProjectAsCommand = new RelayCommand(SaveProjectAs);
            ExportWavCommand = new RelayCommand(ExportWav);
            ExitCommand = new RelayCommand(() =>
                System.Windows.Application.Current.Shutdown());

            AddMidiTrackCommand = new RelayCommand(AddMidiTrack);
            OpenPianoRollCommand = new RelayCommand(OpenPianoRoll);
            OpenVstBrowserCommand = new RelayCommand(OpenVstBrowser);
            ImportMidiCommand = new RelayCommand(ImportMidi);
            ExportMidiCommand = new RelayCommand(ExportMidi);

            _projectService.ProjectChanged += OnProjectChanged;

            // Initialise with a default project
            InitializeEngine();
            NewProject();
        }

        private void InitializeEngine()
        {
            _engine?.Dispose();

            var project = _projectService.CurrentProject;
            _engine = new AudioEngine(
                project.Mixer, _events,
                project.SampleRate, project.Channels, 1024);

            Transport.SetEngine(_engine);
            _engine.Start();
        }

        private void OnProjectChanged(Core.Project.DawProject project)
        {
            Title = $"WaveCraft — {project.Name}";
            StatusText = project.FilePath != null
                ? $"Loaded: {project.FilePath}"
                : "New project";
            InitializeEngine();
        }

        private void NewProject()
        {
            _projectService.NewProject("Untitled");

            // Add a default track
            _projectService.CurrentProject.Mixer.AddTrack("Track 1");

            _events.Publish(new ProjectChanged(null));
            Mixer.Tracks.Clear();

            // Re-init mixer VM
            OnProjectChanged(_projectService.CurrentProject);
            StatusText = "New project created";
        }

        private void OpenProject()
        {
            var path = _dialogService.ShowOpenFileDialog(
                "WaveCraft Project|*.wcft", "Open Project");
            if (path == null) return;

            try
            {
                _projectService.LoadProject(path);
                StatusText = $"Loaded: {path}";
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Failed to load project:\n{ex.Message}");
            }
        }

        private void SaveProject()
        {
            var project = _projectService.CurrentProject;
            if (project.FilePath != null)
            {
                _projectService.SaveProject(project.FilePath);
                StatusText = $"Saved: {project.FilePath}";
            }
            else
            {
                SaveProjectAs();
            }
        }

        private void SaveProjectAs()
        {
            var path = _dialogService.ShowSaveFileDialog(
                "WaveCraft Project|*.wcft", "Save Project");
            if (path == null) return;

            try
            {
                _projectService.SaveProject(path);
                Title = $"WaveCraft — {_projectService.CurrentProject.Name}";
                StatusText = $"Saved: {path}";
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Failed to save project:\n{ex.Message}");
            }
        }

        private void ExportWav()
        {
            var path = _dialogService.ShowSaveFileDialog(
                "WAV Audio|*.wav", "Export Audio",
                _projectService.CurrentProject.Name + ".wav");
            if (path == null) return;

            try
            {
                var project = _projectService.CurrentProject;
                long totalFrames = project.TotalFrames;

                if (totalFrames == 0)
                {
                    _dialogService.ShowInfo("Nothing to export — add some audio first.");
                    return;
                }

                // Offline render — process the entire project at once
                StatusText = "Exporting...";
                var output = project.Mixer.RenderBlock(0, (int)totalFrames,
                    project.Channels, project.SampleRate);

                var format = new AudioFormat(project.SampleRate, project.Channels, 16);
                WavFileWriter.SaveToFile(path, output, format);

                StatusText = $"Exported: {path}";
                _dialogService.ShowInfo($"Exported successfully!\n{path}");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Export failed:\n{ex.Message}");
            }
        }

        #region Midi Functions

        private void AddMidiTrack()
        {
            var project = _projectService.CurrentProject;
            
            // Add track to the mixer
            var midiTrack = project.Mixer.AddMidiTrack(
                $"MIDI {Mixer.AllTracks.Count + 1}", 
                project.SampleRate);

            // Create a default empty clip
            var clip = new MidiClip
            {
                Name = "Pattern 1",
                StartTick = 0,
                LengthTicks = MidiConstants.WholeNote * 4 // 4 bars
            };
            midiTrack.Clips.Add(clip);

            // Refresh the mixer UI to show the new track
            Mixer.RefreshTracks();
            
            StatusText = $"Added MIDI track: {midiTrack.Name}";

            // Open the piano roll for this clip
            OpenPianoRollForClip(clip);
        }

        private void ImportMidi()
        {
            var path = _dialogService.ShowOpenFileDialog(
                "MIDI Files|*.mid;*.midi", "Import MIDI File");
            if (path == null) return;

            try
            {
                var clips = MidiFileReader.LoadFromFile(path);

                if (clips.Count == 0)
                {
                    _dialogService.ShowInfo("No MIDI tracks found in the file.");
                    return;
                }

                // Open the first clip in the piano roll
                OpenPianoRollForClip(clips[0]);
                StatusText = $"Imported {clips.Count} MIDI track(s) from {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Failed to import MIDI:\n{ex.Message}");
            }
        }

        private void ExportMidi()
        {
            if (_pianoRollVm?.Clip == null)
            {
                _dialogService.ShowInfo("No MIDI clip to export. Open the piano roll first.");
                return;
            }

            var path = _dialogService.ShowSaveFileDialog(
                "MIDI File|*.mid", "Export MIDI",
                _pianoRollVm.Clip.Name + ".mid");
            if (path == null) return;

            try
            {
                var clips = new List<MidiClip> { _pianoRollVm.Clip };
                MidiFileWriter.SaveToFile(path, clips);
                StatusText = $"Exported MIDI: {path}";
                _dialogService.ShowInfo($"MIDI exported successfully!\n{path}");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Failed to export MIDI:\n{ex.Message}");
            }
        }

        #endregion

        #region Vst Functions

        private void OpenPianoRoll()
        {
            // Open piano roll for the first MIDI clip found
            // In a full implementation, this would open for the selected clip
            var clip = new MidiClip
            {
                Name = "New Pattern",
                LengthTicks = MidiConstants.WholeNote * 4
            };

            OpenPianoRollForClip(clip);
        }

        private void OpenPianoRollForClip(MidiClip clip)
        {
            _pianoRollVm = new PianoRollViewModel { Clip = clip };

            var window = new PianoRollWindow
            {
                DataContext = _pianoRollVm,
                Owner = System.Windows.Application.Current.MainWindow
            };

            window.Show();
            StatusText = $"Editing: {clip.Name}";
        }

        // ---- VST Browser ----

        private void OpenVstBrowser()
        {
            _vstBrowserVm = new VstBrowserViewModel(_dialogService);

            var window = new VstBrowserWindow
            {
                DataContext = _vstBrowserVm,
                Owner = System.Windows.Application.Current.MainWindow
            };

            if (window.ShowDialog() == true && window.SelectedPlugin != null)
            {
                var pluginInfo = window.SelectedPlugin;
                StatusText = $"Loaded VST: {pluginInfo.Name} by {pluginInfo.Vendor}";

                // In a full implementation, you would:
                // 1. Load the plugin instance
                // 2. Assign it to the selected MIDI track's VstPlugin property
                // 3. The MidiTrack.Render() method will automatically use it
                try
                {
                    var instance = VstPluginInstance.LoadPlugin(
                        pluginInfo.FilePath,
                        _projectService.CurrentProject.SampleRate);

                    if (instance != null)
                    {
                        _dialogService.ShowInfo(
                            $"VST Plugin loaded successfully!\n\n" +
                            $"Name: {instance.PluginName}\n" +
                            $"Vendor: {instance.VendorName}\n" +
                            $"Parameters: {instance.NumParameters}\n" +
                            $"Outputs: {instance.NumOutputs}\n\n" +
                            "Assign this to a MIDI track to use it as an instrument.");
                    }
                }
                catch (Exception ex)
                {
                    _dialogService.ShowError($"Failed to load VST:\n{ex.Message}");
                }
            }
        }

        #endregion

        protected override void OnDispose()
        {
            _engine?.Dispose();
            _projectService.CurrentProject?.Dispose();
        }
    }
}