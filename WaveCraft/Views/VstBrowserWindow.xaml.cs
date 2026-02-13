using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WaveCraft.ViewModels;

namespace WaveCraft.Views
{
    /// <summary>
    /// Interaction logic for VstBrowserWindow.xaml
    /// </summary>
    public partial class VstBrowserWindow : Window
    {
        public VstPluginInfo? SelectedPlugin { get; private set; }

        public VstBrowserWindow()
        {
            InitializeComponent();
        }

        private void OnLoadPlugin_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is VstBrowserViewModel vm && vm.SelectedPlugin != null)
            {
                SelectedPlugin = vm.SelectedPlugin;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a pluging first.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
