using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ChronoLux.Project
{
    public static class ProjectManager
    {
        private static string ProjectFolder => Path.Combine(Application.persistentDataPath, "Projects");
        public static string ModelFolder => Path.Combine(Application.persistentDataPath, "Models");

        static ProjectManager()
        {
            if (!Directory.Exists(ProjectFolder)) Directory.CreateDirectory(ProjectFolder);
            if (!Directory.Exists(ModelFolder)) Directory.CreateDirectory(ModelFolder);
        }

        public static void SaveProject(ChronoProject project)
        {
            string json = JsonUtility.ToJson(project, true);
            string path = Path.Combine(ProjectFolder, project.projectName + ".json");
            File.WriteAllText(path, json);
            Debug.Log($"[ProjectManager] Saved project to {path}");
        }

        public static ChronoProject LoadProject(string name)
        {
            string path = Path.Combine(ProjectFolder, name + ".json");
            if (!File.Exists(path)) return null;

            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<ChronoProject>(json);
        }

        public static List<string> GetAvailableProjects()
        {
            var projects = new List<string>();
            var files = Directory.GetFiles(ProjectFolder, "*.json");
            foreach (var file in files) projects.Add(Path.GetFileNameWithoutExtension(file));
            return projects;
        }

        public static List<string> GetAvailableModels()
        {
            var models = new List<string>();
            if (!Directory.Exists(ModelFolder)) return models;

            var files = Directory.GetFiles(ModelFolder, "*.obj");
            foreach (var file in files) models.Add(Path.GetFileName(file));
            return models;
        }
    }
}
