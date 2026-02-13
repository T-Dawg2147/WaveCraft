using System.Configuration;
using System.Data;
using System.Windows;
using WaveCraft.Mvvm;
using WaveCraft.Services;
using WaveCraft.ViewModels;

namespace WaveCraft
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            IEventAggregator events = new EventAggregator();
            IDialogService dialogs = new DialogService();
            IProjectService project = new ProjectService();

            var mainVm = new MainViewModel(project, dialogs, events);

            var mainWindow = new Views.MainWindow()
            {
                DataContext = mainVm
            };

            MainWindow = mainWindow;
            mainWindow.Show();
        }
    }

}
