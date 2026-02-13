namespace WaveCraft.ViewModels
{
    /// <summary>
    /// Common interface for track ViewModels (both audio and MIDI tracks).
    /// </summary>
    public interface ITrackViewModel
    {
        string Name { get; set; }
        float Volume { get; set; }
        string VolumeDb { get; }
        float Pan { get; set; }
        string PanLabel { get; }
        bool IsMuted { get; set; }
        bool IsSoloed { get; set; }
        float PeakLevel { get; set; }
        bool IsMidiTrack { get; }
    }
}
