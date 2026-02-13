namespace WaveCraft.Services
{
    /// <summary>
    /// Abstraction for file dialogs — allows ViewModels to open dialogs
    /// without knowing about WPF Window classes (testable!).
    /// </summary>
    public interface IDialogService
    {
        string? ShowOpenFileDialog(string filter, string title = "Open File");
        string[]? ShowOpenFilesDialog(string filter, string title = "Open Files");
        string? ShowSaveFileDialog(string filter, string title = "Save File",
            string? defaultName = null);
        bool ShowConfirmDialog(string message, string title = "Confirm");
        void ShowError(string message, string title = "Error");
        void ShowInfo(string message, string title = "Information");
    }
}