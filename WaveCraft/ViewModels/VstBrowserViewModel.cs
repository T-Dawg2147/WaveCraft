using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using WaveCraft.Core.Tracks;
using WaveCraft.Mvvm;
using WaveCraft.Services;

namespace WaveCraft.ViewModels
{
    /// <summary>
    /// ViewModel for browsing, loading, and managing VST plugins.
    /// Scans directories for .dll files and attempts to load them.
    /// </summary>
    public class VstBrowserViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogService;
        private string _scanPath = @"C:\Program Files\VstPlugins";
        private bool _isScanning;
        private string _scanStatus = "Ready";
        private int _selectedIndex = -1;

        public ObservableCollection<VstPluginInfo> DiscoveredPlugins { get; } = new();
        public ObservableCollection<string> ScanPaths { get; } = new();

        public string ScanPath
        {
            get => _scanPath;
            set => SetProperty(ref _scanPath, value);
        }

        public bool IsScanning
        {
            get => _isScanning;
            set => SetProperty(ref _isScanning, value);
        }

        public string ScanStatus
        {
            get => _scanStatus;
            set => SetProperty(ref _scanStatus, value);
        }

        public int SelectedIndex
        {
            get => _selectedIndex;
            set { SetProperty(ref _selectedIndex, value); OnPropertyChanged(nameof(SelectedPlugin)); }
        }

        public VstPluginInfo? SelectedPlugin =>
            _selectedIndex >= 0 && _selectedIndex < DiscoveredPlugins.Count
                ? DiscoveredPlugins[_selectedIndex] : null;

        // ---- Commands ----
        public ICommand ScanDirectoryCommand { get; }
        public ICommand AddScanPathCommand { get; }
        public ICommand BrowseForPluginCommand { get; }
        public ICommand ScanAllPathsCommand { get; }

        public VstBrowserViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;

            ScanDirectoryCommand = new RelayCommand(ScanDirectory, () => !_isScanning);
            AddScanPathCommand = new RelayCommand(AddScanPath);
            BrowseForPluginCommand = new RelayCommand(BrowseForPlugin);
            ScanAllPathsCommand = new RelayCommand(ScanAllPaths, () => !_isScanning);

            // Default scan paths
            ScanPaths.Add(@"C:\Program Files\VstPlugins");
            ScanPaths.Add(@"C:\Program Files\Common Files\VST2");
            ScanPaths.Add(@"C:\Program Files (x86)\VstPlugins");
        }

        private async void ScanDirectory()
        {
            if (!Directory.Exists(_scanPath))
            {
                _dialogService.ShowError($"Directory not found:\n{_scanPath}");
                return;
            }

            IsScanning = true;
            ScanStatus = $"Scanning {_scanPath}...";

            await Task.Run(() =>
            {
                try
                {
                    var dllFiles = Directory.GetFiles(_scanPath, "*.dll",
                        SearchOption.AllDirectories);

                    int scanned = 0;
                    foreach (string dllPath in dllFiles)
                    {
                        scanned++;
                        ScanStatus = $"Scanning ({scanned}/{dllFiles.Length}): " +
                                     $"{Path.GetFileName(dllPath)}";

                        var info = ProbePlugin(dllPath);
                        if (info != null)
                        {
                            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                            {
                                // Avoid duplicates
                                if (!DiscoveredPlugins.Any(p => p.FilePath == info.FilePath))
                                    DiscoveredPlugins.Add(info);
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        _dialogService.ShowError($"Scan error:\n{ex.Message}");
                    });
                }
            });

            ScanStatus = $"Scan complete. Found {DiscoveredPlugins.Count} plugins.";
            IsScanning = false;
        }

        /// <summary>
        /// Attempt to load a DLL and read its VST info without keeping it loaded.
        /// Returns null if the DLL is not a valid VST plugin.
        /// </summary>
        private VstPluginInfo? ProbePlugin(string dllPath)
        {
            try
            {
                // Quick validation: try to load and immediately close
                var plugin = VstPluginInstance.LoadPlugin(dllPath);
                if (plugin == null) return null;

                var info = new VstPluginInfo
                {
                    FilePath = dllPath,
                    FileName = Path.GetFileName(dllPath),
                    Name = plugin.PluginName,
                    Vendor = plugin.VendorName,
                    NumParameters = plugin.NumParameters,
                    NumInputs = plugin.NumInputs,
                    NumOutputs = plugin.NumOutputs,
                    IsInstrument = plugin.NumInputs == 0 && plugin.NumOutputs > 0
                };

                plugin.Dispose();
                return info;
            }
            catch
            {
                // Not a valid VST plugin — skip silently
                return null;
            }
        }

        private void AddScanPath()
        {
            // Use a folder browser via the dialog service
            // For simplicity, we'll let the user type a path
            if (!string.IsNullOrWhiteSpace(_scanPath) &&
                !ScanPaths.Contains(_scanPath))
            {
                ScanPaths.Add(_scanPath);
            }
        }

        private void BrowseForPlugin()
        {
            var path = _dialogService.ShowOpenFileDialog(
                "VST Plugins|*.dll", "Select VST Plugin");
            if (path == null) return;

            var info = ProbePlugin(path);
            if (info != null)
            {
                if (!DiscoveredPlugins.Any(p => p.FilePath == info.FilePath))
                    DiscoveredPlugins.Add(info);

                _dialogService.ShowInfo(
                    $"Plugin loaded successfully!\n\n" +
                    $"Name: {info.Name}\n" +
                    $"Vendor: {info.Vendor}\n" +
                    $"Parameters: {info.NumParameters}\n" +
                    $"Type: {(info.IsInstrument ? "Instrument" : "Effect")}");
            }
            else
            {
                _dialogService.ShowError(
                    "The selected file is not a valid VST2 plugin.");
            }
        }

        private async void ScanAllPaths()
        {
            foreach (string path in ScanPaths.ToList())
            {
                if (Directory.Exists(path))
                {
                    ScanPath = path;
                    ScanDirectory();

                    // Wait for the scan to complete
                    while (IsScanning)
                        await Task.Delay(100);
                }
            }
        }
    }

    /// <summary>
    /// Information about a discovered VST plugin.
    /// </summary>
    public class VstPluginInfo : ViewModelBase
    {
        public string FilePath { get; init; } = "";
        public string FileName { get; init; } = "";
        public string Name { get; init; } = "Unknown";
        public string Vendor { get; init; } = "Unknown";
        public int NumParameters { get; init; }
        public int NumInputs { get; init; }
        public int NumOutputs { get; init; }
        public bool IsInstrument { get; init; }

        public string TypeLabel => IsInstrument ? "🎹 Instrument" : "🔧 Effect";
    }
}