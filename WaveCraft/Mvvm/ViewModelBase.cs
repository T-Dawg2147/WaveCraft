using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WaveCraft.Mvvm
{
    /// <summary>
    /// Base ViewModel with INotifyPropertyChanged, disposable support,
    /// and property-set helper that minimises boilerplate.
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged, IDisposable
    {
        private bool _disposed;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetProperty<T>(ref T field, T value,
            [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        /// <summary>
        /// Sets a property AND raises change for dependent properties.
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value,
            string[] dependentProperties,
            [CallerMemberName] string? name = null)
        {
            if (!SetProperty(ref field, value, name))
                return false;
            foreach (var dep in dependentProperties)
                OnPropertyChanged(dep);
            return true;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                OnDispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        protected virtual void OnDispose() { }
    }
}