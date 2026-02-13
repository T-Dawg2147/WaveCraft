using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Effects
{
    /// <summary>
    /// An ordered pipeline of audio effects. Audio passes through each
    /// effect in sequence: Input → Effect1 → Effect2 → ... → Output.
    ///
    /// PROFESSIONAL CONCEPT: This is the Chain of Responsibility pattern.
    /// Each effect modifies the buffer, then passes it to the next.
    /// </summary>
    public class EffectChain : IDisposable
    {
        private readonly List<IAudioEffect> _effects = new();
        private readonly object _lock = new();

        public IReadOnlyList<IAudioEffect> Effects
        {
            get
            {
                lock (_lock) return _effects.ToList().AsReadOnly();
            }
        }

        public int Count
        {
            get { lock (_lock) return _effects.Count; }
        }

        public void AddEffect(IAudioEffect effect)
        {
            lock (_lock) _effects.Add(effect);
        }

        public void InsertEffect(int index, IAudioEffect effect)
        {
            lock (_lock) _effects.Insert(index, effect);
        }

        public void RemoveEffect(IAudioEffect effect)
        {
            lock (_lock) _effects.Remove(effect);
        }

        public void RemoveAt(int index)
        {
            lock (_lock)
            {
                if (index >= 0 && index < _effects.Count)
                {
                    _effects[index].Dispose();
                    _effects.RemoveAt(index);
                }
            }
        }

        public void MoveEffect(int fromIndex, int toIndex)
        {
            lock (_lock)
            {
                if (fromIndex < 0 || fromIndex >= _effects.Count) return;
                if (toIndex < 0 || toIndex >= _effects.Count) return;

                var effect = _effects[fromIndex];
                _effects.RemoveAt(fromIndex);
                _effects.Insert(toIndex, effect);
            }
        }

        /// <summary>
        /// Process audio through the entire chain.
        /// Called from the audio thread — must be fast.
        /// </summary>
        public void Process(AudioBuffer buffer, int sampleRate)
        {
            // Take a snapshot of the list to avoid holding the lock
            // during processing (the lock is only for structural changes).
            IAudioEffect[] snapshot;
            lock (_lock)
            {
                snapshot = _effects.ToArray();
            }

            foreach (var effect in snapshot)
            {
                if (effect.IsEnabled)
                    effect.Process(buffer, sampleRate);
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                foreach (var effect in _effects)
                    effect.Reset();
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var effect in _effects)
                    effect.Dispose();
                _effects.Clear();
            }
        }
    }
}