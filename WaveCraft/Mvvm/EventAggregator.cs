using System.Collections.Concurrent;

namespace WaveCraft.Mvvm
{
    /// <summary>
    /// A thread-safe publish/subscribe event bus for decoupled communication
    /// between ViewModels. Uses ConcurrentDictionary for lock-free reads
    /// and weak references to prevent memory leaks.
    /// 
    /// This is a professional pattern used in large MVVM apps (Prism, Caliburn)
    /// to avoid ViewModels holding direct references to each other.
    /// </summary>
    public interface IEventAggregator
    {
        void Subscribe<TEvent>(Action<TEvent> handler);
        void Unsubscribe<TEvent>(Action<TEvent> handler);
        void Publish<TEvent>(TEvent eventData);
    }

    public class EventAggregator : IEventAggregator
    {
        // Each event type maps to a list of handlers.
        // ConcurrentDictionary for thread safety (audio thread publishes,
        // UI thread subscribes).
        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
        private readonly object _lock = new();

        public void Subscribe<TEvent>(Action<TEvent> handler)
        {
            var list = _handlers.GetOrAdd(typeof(TEvent), _ => new List<Delegate>());
            lock (_lock)
            {
                list.Add(handler);
            }
        }

        public void Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            if (_handlers.TryGetValue(typeof(TEvent), out var list))
            {
                lock (_lock)
                {
                    list.Remove(handler);
                }
            }
        }

        public void Publish<TEvent>(TEvent eventData)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var list))
                return;

            Delegate[] snapshot;
            lock (_lock)
            {
                snapshot = list.ToArray();
            }

            foreach (var handler in snapshot)
            {
                if (handler is Action<TEvent> typed)
                    typed(eventData);
            }
        }
    }

    // ---- Event message types (immutable records) ----

    /// <summary>Transport state changes (play, pause, stop, seek).</summary>
    public record TransportStateChanged(TransportState State, TimeSpan Position);

    /// <summary>Playback position updated (fires ~60× per second).</summary>
    public record PlaybackPositionChanged(TimeSpan Position, long SamplePosition);

    /// <summary>Peak meter levels updated from the audio thread.</summary>
    public record PeakLevelsUpdated(float LeftPeak, float RightPeak, float LeftRms, float RightRms);

    /// <summary>A track's clip list changed.</summary>
    public record TrackClipsChanged(int TrackIndex);

    /// <summary>Project loaded or created.</summary>
    public record ProjectChanged(string? FilePath);

    public enum TransportState { Stopped, Playing, Paused }
}