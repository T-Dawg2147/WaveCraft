using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using WaveCraft.Core.Instruments;
using WaveCraft.Mvvm;
using WaveCraft.Services;

namespace WaveCraft.ViewModels
{
    public class InstrumentSelectorViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogService;
        private readonly int _sampleRate;
        private int _selectedIndex = -1;
        private IInstrument? _activeInstrument;
        private string _statusText = "No instrument selected";

        public ObservableCollection<InstrumentEntry> Instruments { get; } = new();

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (SetProperty(ref _selectedIndex, value))
                {
                    OnPropertyChanged(nameof(SelectedEntry));
                    if (SelectedEntry != null)
                    {
                        _activeInstrument = SelectedEntry.Instrument;
                        StatusText = $"Active: {SelectedEntry.Name} ({SelectedEntry.CategoryLabel})";
                    }
                }
            }
        }

        public InstrumentEntry? SelectedEntry =>
            _selectedIndex >= 0 && _selectedIndex < Instruments.Count
                ? Instruments[_selectedIndex] : null;

        public IInstrument? ActiveInstrument => _activeInstrument;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        // ---- Commands ----
        public ICommand LoadSoundFontCommand { get; }
        public ICommand LoadVstCommand { get; }
        public ICommand RemoveInstrumentsCommand { get; }
        public ICommand PreviewNoteCommand { get; }

        public event Action<IInstrument> InstrumentChanged;

        public InstrumentSelectorViewModel(IDialogService dialogService, int sampleRate = 44100)
        {
            _dialogService = dialogService;
            _sampleRate = sampleRate;

            LoadSoundFontCommand = new RelayCommand(LoadSoundFont);
            LoadVstCommand = new RelayCommand(LoadVst);
            RemoveInstrumentsCommand = new RelayCommand(RemoveSelected);
            PreviewNoteCommand = new RelayCommand(p => PreviewNote(p as string));

            LoadBuiltInPresets();
        }

        private void LoadBuiltInPresets()
        {
            var presets = SynthInstrument.CreatePresets(_sampleRate);
            foreach (var synth in presets)
            {
                Instruments.Add(new InstrumentEntry()
                {
                    Name = synth.Name,
                    Instrument = synth,
                    CategoryLabel = "Built-In Synth",
                    IsRemovable = false
                });
            }

            if (Instruments.Count > 0)
                SelectedIndex = 0;
        }

        private void LoadSoundFont()
        {
            var path = _dialogService.ShowOpenFileDialog(
                            "SoundFont Files|*.sf2;*.SF2", "Load SoundFont");
            if (path == null) return;

            try
            {
                var sf = new SoundFontInstrument(_sampleRate);
                sf.LoadFromFile(path);

                if (!sf.IsReady)
                {
                    _dialogService.ShowError("SoundFont loaded but contains no usable samples.");
                    sf.Dispose();
                    return;
                }

                // Add each preset as a separate instrument entry
                for (int i = 0; i < sf.Presets.Count; i++)
                {
                    var preset = sf.Presets[i];

                    // Create a separate SoundFont instance for each preset
                    // (shares the same sample data concept, but each has its own voice state)
                    SoundFontInstrument instance;
                    if (i == 0)
                    {
                        instance = sf;
                    }
                    else
                    {
                        // For additional presets, create a new instance
                        // In a production app, you'd share the sample data
                        instance = new SoundFontInstrument(_sampleRate);
                        instance.LoadFromFile(path);
                        instance.SelectedPresetIndex = i;
                    }

                    Instruments.Add(new InstrumentEntry
                    {
                        Name = $"{preset.Name} [{preset.Bank}:{preset.PresetNumber}]",
                        Instrument = instance,
                        CategoryLabel = $"🎹 SoundFont: {Path.GetFileNameWithoutExtension(path)}",
                        FilePath = path,
                        IsRemovable = true
                    });
                }

                StatusText = $"Loaded SoundFont: {sf.Name} ({sf.Presets.Count} presets)";
                _dialogService.ShowInfo(
                    $"SoundFont loaded successfully!\n\n" +
                    $"Name: {sf.Name}\n" +
                    $"Presets: {sf.Presets.Count}\n" +
                    $"Samples: {sf.Presets.Sum(p => p.Zones.Count)} zones");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Failed to load SoundFont:\n{ex.Message}");
            }
        }

        private void LoadVst()
        {
            var path = _dialogService.ShowOpenFileDialog(
                "VST Plugins|*.dll", "Load VST Instrument");
            if (path == null) return;

            try
            {
                var vstInstrument = VstInstrument.LoadFromFile(path, _sampleRate);
                if (vstInstrument == null)
                {
                    _dialogService.ShowError("Failed to load VST plugin.");
                    return;
                }

                Instruments.Add(new InstrumentEntry
                {
                    Name = vstInstrument.Name,
                    Instrument = vstInstrument,
                    CategoryLabel = $"🔌 VST: {vstInstrument.Plugin.VendorName}",
                    FilePath = path,
                    IsRemovable = true
                });

                StatusText = $"Loaded VST: {vstInstrument.Name}";
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Failed to load VST:\n{ex.Message}");
            }
        }

        private void RemoveSelected()
        {
            if (SelectedEntry == null || !SelectedEntry.IsRemovable) return;

            SelectedEntry.Instrument?.Dispose();
            Instruments.RemoveAt(_selectedIndex);

            SelectedIndex = Math.Min(_selectedIndex, Instruments.Count - 1);
            RelayCommand.RaiseCanExecuteChanged();
        }

        private void PreviewNote(string? noteStr)
        {
            if (_activeInstrument == null) return;

            int noteNumber = 60; // Middle C default
            if (noteStr != null && int.TryParse(noteStr, out int parsed))
                noteNumber = parsed;

            // Play note for a short duration using a background task
            _activeInstrument.NoteOn(noteNumber, 100);

            Task.Delay(500).ContinueWith(_ =>
            {
                _activeInstrument.NoteOff(noteNumber);
            });
        }

        public void ApplySelection()
        {
            InstrumentChanged?.Invoke(_activeInstrument);
        }

    }

    public class InstrumentEntry : ViewModelBase
    {
        public string Name { get; init; } = "";
        public IInstrument? Instrument { get; init; }
        public string CategoryLabel { get; init; } = "";
        public string? FilePath { get; init; }
        public bool IsRemovable { get; init; } = true;
    }
}
