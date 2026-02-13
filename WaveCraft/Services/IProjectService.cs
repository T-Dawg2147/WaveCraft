using WaveCraft.Core.Project;

namespace WaveCraft.Services
{
    /// <summary>
    /// Manages the current project — loading, saving, creating.
    /// </summary>
    public interface IProjectService
    {
        DawProject CurrentProject { get; }
        event Action<DawProject>? ProjectChanged;

        void NewProject(string name = "Untitled", int sampleRate = 44100, float bpm = 120f);
        void SaveProject(string filePath);
        void LoadProject(string filePath);
    }
}