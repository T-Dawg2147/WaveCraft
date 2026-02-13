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
using WaveCraft.Core.Instruments;
using WaveCraft.ViewModels;

namespace WaveCraft.Views
{
    /// <summary>
    /// Interaction logic for InstrumentSelectorWindow.xaml
    /// </summary>
    public partial class InstrumentSelectorWindow : Window
    {
        public IInstrument? ChosenInstrument { get; private set; }
        
        public InstrumentSelectorWindow()
        {
            InitializeComponent();
        }

        private void OnApply_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is InstrumentSelectorViewModel vm)
            {
                ChosenInstrument = vm.ActiveInstrument;
                vm.ApplySelection();
                DialogResult = true;
            }
            Close();
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
