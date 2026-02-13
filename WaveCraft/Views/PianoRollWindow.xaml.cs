using System.Windows;
using System.Windows.Input;
using WaveCraft.ViewModels;

namespace WaveCraft.Views
{
    public partial class PianoRollWindow : Window
    {
        public PianoRollWindow()
        {
            InitializeComponent();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (DataContext is not PianoRollViewModel vm) return;

            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            if (ctrl)
            {
                switch (e.Key)
                {
                    case Key.Z: vm.UndoCommand.Execute(null); e.Handled = true; break;
                    case Key.Y: vm.RedoCommand.Execute(null); e.Handled = true; break;
                    case Key.A: vm.SelectAllCommand.Execute(null); e.Handled = true; break;
                    case Key.D: vm.DuplicateSelectedCommand.Execute(null); e.Handled = true; break;
                    case Key.Q: vm.QuantiseSelectedCommand.Execute(null); e.Handled = true; break;
                }
            }
            else
            {
                switch (e.Key)
                {
                    case Key.D: vm.CurrentTool = PianoRollTool.Draw; e.Handled = true; break;
                    case Key.S: vm.CurrentTool = PianoRollTool.Select; e.Handled = true; break;
                    case Key.E: vm.CurrentTool = PianoRollTool.Erase; e.Handled = true; break;
                    case Key.V: vm.CurrentTool = PianoRollTool.Velocity; e.Handled = true; break;
                    case Key.Delete: vm.DeleteSelectedCommand.Execute(null); e.Handled = true; break;
                    case Key.Up: vm.TransposeUpCommand.Execute(null); e.Handled = true; break;
                    case Key.Down: vm.TransposeDownCommand.Execute(null); e.Handled = true; break;
                    case Key.Escape: vm.DeselectAllCommand.Execute(null); e.Handled = true; break;
                }
            }
        }
    }
}