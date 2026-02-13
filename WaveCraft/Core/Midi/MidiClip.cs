namespace WaveCraft.Core.Midi
{
    /// <summary>
    /// A MIDI clip containing a list of notes and control changes.
    /// This is the MIDI equivalent of AudioClip — it sits on a track
    /// at a specific position and contains musical events.
    ///
    /// PROFESSIONAL CONCEPT: MIDI clips are non-destructive. You can
    /// move, duplicate, split, and merge them without affecting the
    /// underlying note data. Operations like quantise and transpose
    /// create new note lists rather than mutating in place.
    /// </summary>
    public class MidiClip
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; set; } = "MIDI Clip";

        /// <summary>Position on the track timeline (in ticks).</summary>
        public long StartTick { get; set; }

        /// <summary>Total length of the clip (in ticks). 0 = auto from content.</summary>
        public long LengthTicks { get; set; }

        /// <summary>All notes in this clip (positions relative to clip start).</summary>
        public List<MidiNote> Notes { get; set; } = new();

        /// <summary>Control change events.</summary>
        public List<MidiControlChange> ControlChanges { get; set; } = new();

        /// <summary>Pitch bend events.</summary>
        public List<MidiPitchBend> PitchBends { get; set; } = new();

        /// <summary>End tick on the timeline.</summary>
        public long EndTick => StartTick + EffectiveLength;

        /// <summary>
        /// Effective length: if LengthTicks is set, use it;
        /// otherwise derive from the last note's end position.
        /// </summary>
        public long EffectiveLength
        {
            get
            {
                if (LengthTicks > 0) return LengthTicks;
                long maxEnd = 0;
                foreach (var note in Notes)
                {
                    if (note.EndTick > maxEnd) maxEnd = note.EndTick;
                }
                return maxEnd > 0 ? maxEnd : MidiConstants.WholeNote;
            }
        }

        // ---- Editing operations (all return new data — non-destructive) ----

        /// <summary>
        /// Add a note to the clip. Prevents overlapping notes on the
        /// same pitch by trimming or removing existing ones.
        /// </summary>
        public void AddNote(MidiNote note)
        {
            // Remove any fully overlapped notes on the same pitch
            Notes.RemoveAll(n =>
                n.NoteNumber == note.NoteNumber &&
                n.StartTick >= note.StartTick &&
                n.EndTick <= note.EndTick);

            Notes.Add(note);
            SortNotes();
        }

        /// <summary>
        /// Remove a note by its ID.
        /// </summary>
        public bool RemoveNote(Guid noteId)
            => Notes.RemoveAll(n => n.Id == noteId) > 0;

        /// <summary>
        /// Move a note to a new position and/or pitch.
        /// Returns the updated note (records are immutable, so we replace).
        /// </summary>
        public MidiNote? MoveNote(Guid noteId, long newStartTick, int newNoteNumber)
        {
            int index = Notes.FindIndex(n => n.Id == noteId);
            if (index < 0) return null;

            var old = Notes[index];
            var moved = old with
            {
                StartTick = Math.Max(0, newStartTick),
                NoteNumber = Math.Clamp(newNoteNumber, MidiConstants.MinNote,
                    MidiConstants.MaxNote)
            };

            Notes[index] = moved;
            SortNotes();
            return moved;
        }

        /// <summary>
        /// Resize a note's duration.
        /// </summary>
        public MidiNote? ResizeNote(Guid noteId, long newDuration)
        {
            int index = Notes.FindIndex(n => n.Id == noteId);
            if (index < 0) return null;

            var old = Notes[index];
            var resized = old with
            {
                DurationTicks = Math.Max(MidiConstants.ThirtySecondNote, newDuration)
            };

            Notes[index] = resized;
            return resized;
        }

        /// <summary>
        /// Change a note's velocity.
        /// </summary>
        public MidiNote? SetNoteVelocity(Guid noteId, int velocity)
        {
            int index = Notes.FindIndex(n => n.Id == noteId);
            if (index < 0) return null;

            var old = Notes[index];
            var updated = old with { Velocity = Math.Clamp(velocity, 1, 127) };
            Notes[index] = updated;
            return updated;
        }

        /// <summary>
        /// Transpose all selected notes by a semitone offset.
        /// </summary>
        public void Transpose(HashSet<Guid> selectedIds, int semitones)
        {
            for (int i = 0; i < Notes.Count; i++)
            {
                if (selectedIds.Contains(Notes[i].Id))
                {
                    int newNote = Math.Clamp(
                        Notes[i].NoteNumber + semitones,
                        MidiConstants.MinNote, MidiConstants.MaxNote);
                    Notes[i] = Notes[i] with { NoteNumber = newNote };
                }
            }
        }

        /// <summary>
        /// Quantise selected notes to a grid (snap start positions).
        /// </summary>
        public void Quantise(HashSet<Guid> selectedIds, int gridSize)
        {
            for (int i = 0; i < Notes.Count; i++)
            {
                if (selectedIds.Contains(Notes[i].Id))
                {
                    long quantised = MidiConstants.Quantise(
                        Notes[i].StartTick, gridSize);
                    Notes[i] = Notes[i] with { StartTick = quantised };
                }
            }
            SortNotes();
        }

        /// <summary>
        /// Quantise ALL notes in the clip.
        /// </summary>
        public void QuantiseAll(int gridSize)
        {
            for (int i = 0; i < Notes.Count; i++)
            {
                long quantised = MidiConstants.Quantise(
                    Notes[i].StartTick, gridSize);
                Notes[i] = Notes[i] with { StartTick = quantised };
            }
            SortNotes();
        }

        /// <summary>
        /// Duplicate the clip with new IDs for all notes.
        /// </summary>
        public MidiClip Duplicate(long newStartTick)
        {
            var clone = new MidiClip
            {
                Name = Name + " (Copy)",
                StartTick = newStartTick,
                LengthTicks = LengthTicks
            };

            foreach (var note in Notes)
            {
                clone.Notes.Add(note with { Id = Guid.NewGuid() });
            }

            return clone;
        }

        /// <summary>
        /// Get all notes that are active at a given tick position.
        /// Used during playback to determine which notes to trigger.
        /// </summary>
        public List<MidiNote> GetNotesAtTick(long tick)
        {
            var result = new List<MidiNote>();
            foreach (var note in Notes)
            {
                if (tick >= note.StartTick && tick < note.EndTick)
                    result.Add(note);
            }
            return result;
        }

        /// <summary>
        /// Get all notes that START within a tick range (for playback scheduling).
        /// </summary>
        public List<MidiNote> GetNoteOnEvents(long fromTick, long toTick)
        {
            var result = new List<MidiNote>();
            foreach (var note in Notes)
            {
                if (note.StartTick >= fromTick && note.StartTick < toTick)
                    result.Add(note);
            }
            return result;
        }

        /// <summary>
        /// Get all notes that END within a tick range (for note-off scheduling).
        /// </summary>
        public List<MidiNote> GetNoteOffEvents(long fromTick, long toTick)
        {
            var result = new List<MidiNote>();
            foreach (var note in Notes)
            {
                if (note.EndTick >= fromTick && note.EndTick < toTick)
                    result.Add(note);
            }
            return result;
        }

        private void SortNotes()
        {
            Notes.Sort((a, b) => a.StartTick.CompareTo(b.StartTick));
        }
    }
}