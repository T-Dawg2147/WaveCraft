using System.Collections.ObjectModel;
using System.Windows.Input;
using WaveCraft.Core.Automation;
using WaveCraft.Core.Effects;
using WaveCraft.Mvvm;

namespace WaveCraft.ViewModels
{
    /// <summary>
    /// ViewModel for an effect chain — manages the list of effects
    /// and auto-generates parameter ViewModels via reflection.
    /// </summary>
    public class EffectChainViewModel : ViewModelBase
    {
        private readonly EffectChain _chain;
        private int _selectedIndex = -1;

        public ObservableCollection<EffectSlotViewModel> Slots { get; } = new();

        public int SelectedIndex
        {
            get => _selectedIndex;
            set { SetProperty(ref _selectedIndex, value); OnPropertyChanged(nameof(SelectedSlot)); }
        }

        public EffectSlotViewModel? SelectedSlot =>
            _selectedIndex >= 0 && _selectedIndex < Slots.Count
                ? Slots[_selectedIndex] : null;

        // ---- Commands ----
        public ICommand AddEffectCommand { get; }
        public ICommand RemoveEffectCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }

        // Available effect types
        public static readonly (string Name, Func<IAudioEffect> Factory)[] AvailableEffects =
        {
            ("Gain",       () => new GainEffect()),
            ("Delay",      () => new DelayEffect()),
            ("Reverb",     () => new ReverbEffect()),
            ("3-Band EQ",  () => new EqualizerEffect()),
            ("Compressor", () => new CompressorEffect()),
            ("Noise Gate", () => new NoiseGateEffect()),
            ("Fade",       () => new FadeEffect()),
        };

        public EffectChainViewModel(EffectChain chain)
        {
            _chain = chain;

            AddEffectCommand = new RelayCommand(p => AddEffect(p as string));
            RemoveEffectCommand = new RelayCommand(RemoveSelected, () => SelectedSlot != null);
            MoveUpCommand = new RelayCommand(MoveUp, () => _selectedIndex > 0);
            MoveDownCommand = new RelayCommand(MoveDown,
                () => _selectedIndex >= 0 && _selectedIndex < Slots.Count - 1);

            // Load any existing effects
            RefreshSlots();
        }

        private void AddEffect(string? effectName)
        {
            if (effectName == null) return;

            var factory = Array.Find(AvailableEffects, e => e.Name == effectName);
            if (factory.Factory == null) return;

            var effect = factory.Factory();
            _chain.AddEffect(effect);
            RefreshSlots();
            SelectedIndex = Slots.Count - 1;
        }

        private void RemoveSelected()
        {
            if (_selectedIndex < 0) return;
            _chain.RemoveAt(_selectedIndex);
            RefreshSlots();
            SelectedIndex = Math.Min(_selectedIndex, Slots.Count - 1);
        }

        private void MoveUp()
        {
            if (_selectedIndex <= 0) return;
            _chain.MoveEffect(_selectedIndex, _selectedIndex - 1);
            RefreshSlots();
            SelectedIndex--;
        }

        private void MoveDown()
        {
            if (_selectedIndex < 0 || _selectedIndex >= Slots.Count - 1) return;
            _chain.MoveEffect(_selectedIndex, _selectedIndex + 1);
            RefreshSlots();
            SelectedIndex++;
        }

        private void RefreshSlots()
        {
            Slots.Clear();
            foreach (var effect in _chain.Effects)
            {
                Slots.Add(new EffectSlotViewModel(effect));
            }
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// ViewModel for a single effect slot.
    /// Uses reflection + expression tree compilation to auto-discover parameters.
    /// </summary>
    public class EffectSlotViewModel : ViewModelBase
    {
        private readonly IAudioEffect _effect;
        private bool _isEnabled;

        public IAudioEffect Model => _effect;
        public string Name => _effect.Name;

        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (SetProperty(ref _isEnabled, value)) _effect.IsEnabled = value; }
        }

        /// <summary>
        /// Auto-generated parameter ViewModels — discovered via reflection,
        /// backed by compiled expression tree delegates for fast access.
        /// </summary>
        public ObservableCollection<EffectParameterViewModel> Parameters { get; } = new();

        public EffectSlotViewModel(IAudioEffect effect)
        {
            _effect = effect;
            _isEnabled = effect.IsEnabled;

            // Use AutomationCompiler to discover and compile all parameters
            var compiled = AutomationCompiler.CompileEffectParameters(effect);
            foreach (var param in compiled)
            {
                Parameters.Add(new EffectParameterViewModel(param));
            }
        }
    }

    /// <summary>
    /// ViewModel for a single effect parameter — drives a slider/knob in the UI.
    /// Uses the compiled expression tree delegates for get/set (zero reflection at runtime).
    /// </summary>
    public class EffectParameterViewModel : ViewModelBase
    {
        private readonly CompiledParameter _compiled;

        public string Name => _compiled.Name;
        public float MinValue => _compiled.MinValue;
        public float MaxValue => _compiled.MaxValue;
        public string Unit => _compiled.Unit;
        public bool IsLogarithmic => _compiled.IsLogarithmic;

        public float Value
        {
            get => _compiled.GetValue();
            set
            {
                _compiled.SetValue(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayText));
            }
        }

        public string DisplayText
        {
            get
            {
                float v = Value;
                return Unit switch
                {
                    "dB" => $"{v:F1} dB",
                    "ms" => v >= 1000 ? $"{v / 1000:F2} s" : $"{v:F0} ms",
                    "Hz" => v >= 1000 ? $"{v / 1000:F1} kHz" : $"{v:F0} Hz",
                    "%" => $"{v:F0}%",
                    ":1" => $"{v:F1}:1",
                    _ => $"{v:F2}"
                };
            }
        }

        public EffectParameterViewModel(CompiledParameter compiled)
        {
            _compiled = compiled;
        }

        public void ResetToDefault()
        {
            Value = _compiled.DefaultValue;
        }
    }
}