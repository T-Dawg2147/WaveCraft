using WaveCraft.Core.Project;

namespace WaveCraft.Services
{
    public class ProjectService : IProjectService
    {
        public DawProject CurrentProject { get; private set; }
        public event Action<DawProject>? ProjectChanged;

        public ProjectService()
        {
            CurrentProject = new DawProject();
        }

        public void NewProject(string name = "Untitled", int sampleRate = 44100,
            float bpm = 120f)
        {
            CurrentProject?.Dispose();
            CurrentProject = new DawProject
            {
                Name = name,
                SampleRate = sampleRate,
                Bpm = bpm
            };
            ProjectChanged?.Invoke(CurrentProject);
        }

        public void SaveProject(string filePath)
        {
            ProjectSerializer.Save(CurrentProject, filePath);
        }

        public void LoadProject(string filePath)
        {
            CurrentProject?.Dispose();
            CurrentProject = ProjectSerializer.Load(filePath);
            ProjectChanged?.Invoke(CurrentProject);
        }
    }
}