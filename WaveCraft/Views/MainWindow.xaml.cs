using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WaveCraft.ViewModels;

namespace WaveCraft.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnTimelineSeek(double normalizedPosition)
        {
            if (DataContext is MainViewModel vm)
            {
                // The PlayheadPosition binding handles the seek via
                // the TransportViewModel's setter. But we also call
                // SeekToFrame directly for immediate response.
                var project = vm.Transport;
                // PlayheadPosition setter already calls engine.Seek(),
                // so this is handled automatically by the two-way binding.
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.Dispose();

            base.OnClosing(e);
        }
    }
}