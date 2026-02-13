using System.Collections.ObjectModel;
using System.Windows.Input;
using WaveCraft.Core.Midi;
using WaveCraft.Mvvm;

namespace WaveCraft.ViewModels
{
    public enum PianoRollTool { Draw, Select, Erase, Velocity }

    /// <summary>
    /// ViewModel for the piano roll MIDI editor.
    /// Manages note display, editing, selection, and grid snapping.
    /// </summary>
    public class PianoRollViewModel : ViewModelBase
    {
        private MidiClip? _clip;
        private PianoRollTool _currentTool = PianoRollTool.Draw;
        private int _gridDivision = MidiConstants.EighthNote;
        private int _defaultVelocity = MidiConstants.DefaultVelocity;
        private int _defaultDuration = MidiConstants.EighthNote;
        private int _viewportStartTick;
        private int _viewportNoteRangeBottom = 36;  // C2
        private int _viewportNoteRangeTop = 96;     // C7
        private double _horizontalZoom = 1.0;
        private double _verticalZoom = 1.0;

        // Selection
        private readonly HashSet<Guid> _selectedNoteIds = new();

        // Undo
        private readonly Stack<List<MidiNote>> _undoStack = new();
        private readonly Stack<List<MidiNote>> _redoStack = new();
        private const int MaxUndo = 50;

        public ObservableCollection<MidiNoteViewModel> Notes { get; } = new();

        public MidiClip? Clip
        {
            get => _clip;
            set
            {
                _clip = value;
                OnPropertyChanged();
                RefreshNotes();
            }
        }

        public PianoRollTool CurrentTool
        {
            get => _currentTool;
            set { SetProperty(ref _currentTool, value); OnPropertyChanged(nameof(StatusText)); }
        }

        public int GridDivision
        {
            get => _gridDivision;
            set { SetProperty(ref _gridDivision, value); OnPropertyChanged(nameof(GridLabel)); }
        }

        public string GridLabel => _gridDivision switch
        {
            MidiConstants.WholeNote => "1/1",
            MidiConstants.HalfNote => "1/2",
            MidiConstants.QuarterNote => "1/4",
            MidiConstants.EighthNote => "1/8",
            MidiConstants.SixteenthNote => "1/16",
            MidiConstants.ThirtySecondNote => "1/32",
            _ => $"{_gridDivision}t"
        };

        public int DefaultVelocity
        {
            get => _defaultVelocity;
            set => SetProperty(ref _defaultVelocity, Math.Clamp(value, 1, 127));
        }

        public double HorizontalZoom
        {
            get => _horizontalZoom;
            set => SetProperty(ref _horizontalZoom, Math.Clamp(value, 0.1, 10.0));
        }

        public double VerticalZoom
        {
            get => _verticalZoom;
            set => SetProperty(ref _verticalZoom, Math.Clamp(value, 0.5, 4.0));
        }

        public int SelectedCount => _selectedNoteIds.Count;

        public string StatusText =>
            $"Tool: {_currentTool} | Grid: {GridLabel} | " +
            $"Notes: {_clip?.Notes.Count ?? 0} | " +
            $"Selected: {SelectedCount} | " +
            $"Vel: {_defaultVelocity}";

        // ---- Commands ----
        public ICommand SetToolCommand { get; }
        public ICommand SetGridCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand TransposeUpCommand { get; }
        public ICommand TransposeDownCommand { get; }
        public ICommand TransposeOctaveUpCommand { get; }
        public ICommand TransposeOctaveDownCommand { get; }
        public ICommand QuantiseSelectedCommand { get; }
        public ICommand QuantiseAllCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }
        public ICommand DuplicateSelectedCommand { get; }

        public PianoRollViewModel()
        {
            SetToolCommand = new RelayCommand(p =>
            {
                if (p is string s && Enum.TryParse<PianoRollTool>(s, out var tool))
                    CurrentTool = tool;
            });

            SetGridCommand = new RelayCommand(p =>
            {
                if (p is string s && int.TryParse(s, out int div))
                    GridDivision = div;
            });

            SelectAllCommand = new RelayCommand(SelectAll);
            DeselectAllCommand = new RelayCommand(DeselectAll);
            DeleteSelectedCommand = new RelayCommand(DeleteSelected, () => SelectedCount > 0);
            TransposeUpCommand = new RelayCommand(() => TransposeSelected(1));
            TransposeDownCommand = new RelayCommand(() => TransposeSelected(-1));
            TransposeOctaveUpCommand = new RelayCommand(() => TransposeSelected(12));
            TransposeOctaveDownCommand = new RelayCommand(() => TransposeSelected(-12));
            QuantiseSelectedCommand = new RelayCommand(QuantiseSelected);
            QuantiseAllCommand = new RelayCommand(QuantiseAllNotes);
            UndoCommand = new RelayCommand(Undo, () => _undoStack.Count > 0);
            RedoCommand = new RelayCommand(Redo, () => _redoStack.Count > 0);
            DuplicateSelectedCommand = new RelayCommand(DuplicateSelected, () => SelectedCount > 0);
        }

        // ---- Note creation / editing (called from the View) ----

        /// <summary>
        /// Draw a new note at the given position. Called when the user
        /// clicks on the piano roll grid in Draw mode.
        /// </summary>
        public void DrawNoteAt(long tick, int noteNumber)
        {
            if (_clip == null) return;

            SaveUndoState();

            long snappedTick = MidiConstants.Quantise(tick, _gridDivision);
            int clampedNote = Math.Clamp(noteNumber, MidiConstants.MinNote, MidiConstants.MaxNote);

            var newNote = new MidiNote
            {
                NoteNumber = clampedNote,
                Velocity = _defaultVelocity,
                StartTick = snappedTick,
                DurationTicks = _defaultDuration,
                Channel = 0
            };

            _clip.AddNote(newNote);
            RefreshNotes();
        }

        /// <summary>
        /// Erase a note by its ID.
        /// </summary>
        public void EraseNote(Guid noteId)
        {
            if (_clip == null) return;
            SaveUndoState();
            _clip.RemoveNote(noteId);
            _selectedNoteIds.Remove(noteId);
            RefreshNotes();
        }

        /// <summary>
        /// Move a note to a new position/pitch.
        /// </summary>
        public void MoveNote(Guid noteId, long newTick, int newNoteNumber)
        {
            if (_clip == null) return;

            long snappedTick = MidiConstants.Quantise(newTick, _gridDivision);
            _clip.MoveNote(noteId, snappedTick, newNoteNumber);
            RefreshNotes();
        }

        /// <summary>
        /// Resize a note by dragging its right edge.
        /// </summary>
        public void ResizeNote(Guid noteId, long newDuration)
        {
            if (_clip == null) return;

            long snappedDuration = Math.Max(
                MidiConstants.ThirtySecondNote,
                MidiConstants.Quantise(newDuration, _gridDivision));

            _clip.ResizeNote(noteId, snappedDuration);
            RefreshNotes();
        }

        /// <summary>
        /// Set the velocity of a note (used by the velocity tool).
        /// </summary>
        public void SetNoteVelocity(Guid noteId, int velocity)
        {
            if (_clip == null) return;
            _clip.SetNoteVelocity(noteId, velocity);
            RefreshNotes();
        }

        // ---- Selection ----

        public void ToggleNoteSelection(Guid noteId)
        {
            if (_selectedNoteIds.Contains(noteId))
                _selectedNoteIds.Remove(noteId);
            else
                _selectedNoteIds.Add(noteId);

            UpdateNoteSelectionState();
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(StatusText));
        }

        public void SetNoteSelected(Guid noteId, bool selected)
        {
            if (selected)
                _selectedNoteIds.Add(noteId);
            else
                _selectedNoteIds.Remove(noteId);

            UpdateNoteSelectionState();
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(StatusText));
        }

        public bool IsNoteSelected(Guid noteId) => _selectedNoteIds.Contains(noteId);

        private void SelectAll()
        {
            if (_clip == null) return;
            _selectedNoteIds.Clear();
            foreach (var note in _clip.Notes)
                _selectedNoteIds.Add(note.Id);
            UpdateNoteSelectionState();
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(StatusText));
        }

        private void DeselectAll()
        {
            _selectedNoteIds.Clear();
            UpdateNoteSelectionState();
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(StatusText));
        }

        // ---- Bulk operations ----

        private void DeleteSelected()
        {
            if (_clip == null || _selectedNoteIds.Count == 0) return;
            SaveUndoState();

            foreach (var id in _selectedNoteIds.ToList())
                _clip.RemoveNote(id);

            _selectedNoteIds.Clear();
            RefreshNotes();
        }

        private void TransposeSelected(int semitones)
        {
            if (_clip == null || _selectedNoteIds.Count == 0) return;
            SaveUndoState();
            _clip.Transpose(_selectedNoteIds, semitones);
            RefreshNotes();
        }

        private void QuantiseSelected()
        {
            if (_clip == null) return;
            SaveUndoState();

            if (_selectedNoteIds.Count > 0)
                _clip.Quantise(_selectedNoteIds, _gridDivision);
            else
                _clip.QuantiseAll(_gridDivision);

            RefreshNotes();
        }

        private void QuantiseAllNotes()
        {
            if (_clip == null) return;
            SaveUndoState();
            _clip.QuantiseAll(_gridDivision);
            RefreshNotes();
        }

        private void DuplicateSelected()
        {
            if (_clip == null || _selectedNoteIds.Count == 0) return;
            SaveUndoState();

            // Find the range of selected notes
            long maxEnd = 0;
            var selectedNotes = new List<MidiNote>();
            foreach (var note in _clip.Notes)
            {
                if (_selectedNoteIds.Contains(note.Id))
                {
                    selectedNotes.Add(note);
                    if (note.EndTick > maxEnd) maxEnd = note.EndTick;
                }
            }

            long minStart = selectedNotes.Min(n => n.StartTick);
            long offset = maxEnd - minStart;

            // Create duplicates shifted forward
            _selectedNoteIds.Clear();
            foreach (var note in selectedNotes)
            {
                var dup = note with
                {
                    Id = Guid.NewGuid(),
                    StartTick = note.StartTick + offset
                };
                _clip.AddNote(dup);
                _selectedNoteIds.Add(dup.Id);
            }

            RefreshNotes();
        }

        // ---- Undo / Redo ----

        private void SaveUndoState()
        {
            if (_clip == null) return;
            _undoStack.Push(new List<MidiNote>(_clip.Notes));
            _redoStack.Clear();

            if (_undoStack.Count > MaxUndo)
            {
                var items = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = items.Length - 2; i >= 0; i--)
                    _undoStack.Push(items[i]);
            }

            RelayCommand.RaiseCanExecuteChanged();
        }

        private void Undo()
        {
            if (_clip == null || _undoStack.Count == 0) return;
            _redoStack.Push(new List<MidiNote>(_clip.Notes));
            _clip.Notes.Clear();
            _clip.Notes.AddRange(_undoStack.Pop());
            RefreshNotes();
            RelayCommand.RaiseCanExecuteChanged();
        }

        private void Redo()
        {
            if (_clip == null || _redoStack.Count == 0) return;
            _undoStack.Push(new List<MidiNote>(_clip.Notes));
            _clip.Notes.Clear();
            _clip.Notes.AddRange(_redoStack.Pop());
            RefreshNotes();
            RelayCommand.RaiseCanExecuteChanged();
        }

        // ---- Note ViewModel management ----

        private void RefreshNotes()
        {
            Notes.Clear();
            if (_clip == null) return;

            foreach (var note in _clip.Notes)
            {
                Notes.Add(new MidiNoteViewModel(note,
                    _selectedNoteIds.Contains(note.Id)));
            }

            OnPropertyChanged(nameof(StatusText));
        }

        private void UpdateNoteSelectionState()
        {
            foreach (var noteVm in Notes)
                noteVm.IsSelected = _selectedNoteIds.Contains(noteVm.Id);
        }
    }

    /// <summary>
    /// ViewModel for a single MIDI note in the piano roll.
    /// </summary>
    public class MidiNoteViewModel : ViewModelBase
    {
        private readonly MidiNote _note;
        private bool _isSelected;

        public Guid Id => _note.Id;
        public int NoteNumber => _note.NoteNumber;
        public string NoteName => _note.NoteName;
        public int Velocity => _note.Velocity;
        public long StartTick => _note.StartTick;
        public long DurationTicks => _note.DurationTicks;
        public long EndTick => _note.EndTick;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        /// Velocity as opacity (0.3 to 1.0) for visual display.
        /// </summary>
        public double VelocityOpacity =>
            0.3 + (Velocity / 127.0 * 0.7);

        /// <summary>
        /// Colour hue based on velocity (green = soft, red = hard).
        /// </summary>
        public string VelocityColor
        {
            get
            {
                float t = Velocity / 127f;
                int r = (int)(t * 255);
                int g = (int)((1f - t * 0.5f) * 200);
                int b = (int)((1f - t) * 100 + 80);
                return $"#{r:X2}{g:X2}{b:X2}";
            }
        }

        public MidiNoteViewModel(MidiNote note, bool isSelected = false)
        {
            _note = note;
            _isSelected = isSelected;
        }
    }
}