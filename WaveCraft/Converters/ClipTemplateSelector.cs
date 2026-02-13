using System.Windows;
using System.Windows.Controls;
using WaveCraft.ViewModels;

namespace WaveCraft.Converters
{
    /// <summary>
    /// Selects the appropriate clip template based on clip type (Audio vs MIDI).
    /// </summary>
    public class ClipTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? AudioClipTemplate { get; set; }
        public DataTemplate? MidiClipTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is MidiClipViewModel)
                return MidiClipTemplate;
            if (item is ClipViewModel)
                return AudioClipTemplate;
            
            return base.SelectTemplate(item, container);
        }
    }
}
