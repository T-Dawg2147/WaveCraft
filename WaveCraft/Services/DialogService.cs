using Microsoft.Win32;
using System.Windows;

namespace WaveCraft.Services
{
    public class DialogService : IDialogService
    {
        public string? ShowOpenFileDialog(string filter, string title = "Open File")
        {
            var dlg = new OpenFileDialog { Filter = filter, Title = title };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        public string[]? ShowOpenFilesDialog(string filter, string title = "Open Files")
        {
            var dlg = new OpenFileDialog
            {
                Filter = filter,
                Title = title,
                Multiselect = true
            };
            return dlg.ShowDialog() == true ? dlg.FileNames : null;
        }

        public string? ShowSaveFileDialog(string filter, string title = "Save File",
            string? defaultName = null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = filter,
                Title = title,
                FileName = defaultName ?? ""
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        public bool ShowConfirmDialog(string message, string title = "Confirm")
            => MessageBox.Show(message, title, MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;

        public void ShowError(string message, string title = "Error")
            => MessageBox.Show(message, title, MessageBoxButton.OK,
                MessageBoxImage.Error);

        public void ShowInfo(string message, string title = "Information")
            => MessageBox.Show(message, title, MessageBoxButton.OK,
                MessageBoxImage.Information);
    }
}