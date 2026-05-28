using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using System.Threading.Tasks;
using ChronoLux.Library;
using UnityEngine.UIElements;

namespace ChronoLux.Project
{
    public class RuntimeModelLoader : MonoBehaviour
    {
        public Transform modelRoot;
        public LightDoseSimulator simulator;
        public MaterialLibrary materialLibrary;
        public Shader bakeShader;
        public Material artifactMaterial; // Material with HeatmapVisualizer shader

        [Header("UI Progress")]
        public VisualElement loadingScreen;
        public ProgressBar progressBar;
        public Label statusLabel;

        private GameObject _artifactInstance;
        private List<GameObject> _environmentInstances = new List<GameObject>();
        private Material _defaultMaterial;

        public IEnumerator LoadProjectModelsAsync(ChronoProject project)
        {
            if (loadingScreen != null) loadingScreen.style.display = DisplayStyle.Flex;
            UpdateProgress(0, "Initializing...");

            ClearExistingModels();

            if (project == null) {
                if (loadingScreen != null) loadingScreen.style.display = DisplayStyle.None;
                yield break;
            }

            // 1. Load Artifact (with migration for legacy projects)
#pragma warning disable CS0618
            string artifactFile = !string.IsNullOrEmpty(project.artifactFileName) ? project.artifactFileName : project.modelFileName;
#pragma warning restore CS0618
            
            if (!string.IsNullOrEmpty(artifactFile))
            {
                string path = Path.Combine(ProjectManager.ModelFolder, artifactFile);
                UpdateProgress(0.1f, $"Loading artifact: {artifactFile}");
                
                var parseTask = Task.Run(() => {
                    if (!File.Exists(path)) return null;
                    return SimpleObjLoader.ParseObj(File.ReadAllText(path));
                });
                
                while (!parseTask.IsCompleted) {
                    UpdateProgress(0.1f, "Parsing artifact geometry...");
                    yield return null;
                }

                if (parseTask.IsCompletedSuccessfully && parseTask.Result != null && parseTask.Result.Count > 0) {
                    var dataList = parseTask.Result;
                    _artifactInstance = new GameObject(Path.GetFileNameWithoutExtension(artifactFile));
                    if (_artifactInstance != null) {
                        _artifactInstance.transform.SetParent(modelRoot);
                        _artifactInstance.transform.localPosition = Vector3.zero;
                        _artifactInstance.transform.localScale = Vector3.one;

                        foreach (var data in dataList) {
                            GameObject child = CreateGameObjectFromData(data, artifactMaterial);
                            if (child == null) continue;
                            child.transform.SetParent(_artifactInstance.transform, false);
                            child.transform.localPosition = Vector3.zero;
                            SetupArtifactPart(child, project); 
                        }

                        SetupArtifactRoot(_artifactInstance);
                        
                        // 1. Normalize the raw mesh geometry (Centering + Scaling children to a 2m box)
                        CenterAndScaleModel(_artifactInstance, 2.0f);

                        // 2. Apply user-defined transformations on the root
                        _artifactInstance.transform.localPosition = project.artifactPosition;
                        if (project.artifactScale != Vector3.zero) 
                            _artifactInstance.transform.localScale = project.artifactScale;
                    }
                }
            }

            // 2. Load Environment
            if (project.environmentFileNames != null && project.environmentFileNames.Count > 0)
            {
                float step = 0.5f / project.environmentFileNames.Count;
                for (int i = 0; i < project.environmentFileNames.Count; i++)
                {
                    string fileName = project.environmentFileNames[i];
                    string path = Path.Combine(ProjectManager.ModelFolder, fileName);
                    UpdateProgress(0.5f + (i * step), $"Loading environment: {fileName}");

                    var parseTask = Task.Run(() => {
                        if (!File.Exists(path)) return null;
                        return SimpleObjLoader.ParseObj(File.ReadAllText(path));
                    });
                    
                    while (!parseTask.IsCompleted) yield return null;

                    if (parseTask.IsCompletedSuccessfully && parseTask.Result != null) {
                        var dataList = parseTask.Result;
                        GameObject envRoot = new GameObject(Path.GetFileNameWithoutExtension(fileName));
                        envRoot.transform.SetParent(modelRoot);
                        envRoot.transform.localPosition = Vector3.zero;
                        _environmentInstances.Add(envRoot);

                        foreach (var data in dataList) {
                            GameObject child = CreateGameObjectFromData(data, null);
                            if (child == null) continue;
                            child.transform.SetParent(envRoot.transform, false);
                            child.transform.localPosition = Vector3.zero;
                            SetupEnvironmentPart(child, project);
                        }
                    }
                }
            }

            CreateOutdoorReferenceSensors();

            UpdateProgress(1.0f, "Complete!");
            yield return new WaitForSeconds(0.5f);
            if (loadingScreen != null) loadingScreen.style.display = DisplayStyle.None;
        }

        private void CreateOutdoorReferenceSensors()
        {
            // First, destroy any existing dynamically created sensors
            var existing = new List<VirtualLuxSensor>(VirtualLuxSensor.AllSensors);
            foreach (var sensor in existing) {
                if (sensor != null && sensor.gameObject.name.StartsWith("AutoSensor_")) {
                    Destroy(Application.isPlaying ? (Object)sensor.gameObject : sensor.gameObject);
                }
            }

            // Find bounds of the entire scene
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            var renderers = modelRoot.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            }

            // Place sensors slightly above the highest point of the scene to ensure zero occlusion
            float spawnY = bounds.max.y + 2.0f;
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + 1.0f;

            Vector3 center = bounds.center;
            center.y = spawnY;

            // Create 5 sensors: 1 in center, 4 in a circle around the perimeter
            Vector3[] positions = new Vector3[] {
                center,
                center + new Vector3(radius, 0, 0),
                center + new Vector3(-radius, 0, 0),
                center + new Vector3(0, 0, radius),
                center + new Vector3(0, 0, -radius)
            };

            for (int i = 0; i < positions.Length; i++)
            {
                GameObject sensorObj = new GameObject($"AutoSensor_{i}");
                sensorObj.transform.SetParent(modelRoot);
                sensorObj.transform.position = positions[i];
                sensorObj.transform.rotation = Quaternion.identity; // Point straight UP (transform.up == Vector3.up)

                var luxSensor = sensorObj.AddComponent<VirtualLuxSensor>();
                luxSensor.showGizmo = true;
            }
        }

        private GameObject CreateGameObjectFromData(SimpleObjLoader.MeshData data, Material customMaterial)
        {
            Mesh mesh = SimpleObjLoader.CreateMesh(data);
            if (mesh == null) return null;

            GameObject go = new GameObject(data.name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            
            if (customMaterial != null) renderer.sharedMaterial = customMaterial;
            else {
                if (_defaultMaterial == null) _defaultMaterial = new Material(Shader.Find("HDRP/Lit"));
                renderer.sharedMaterial = _defaultMaterial;
            }
            return go;
        }

        private void UpdateProgress(float val, string status)
        {
            if (progressBar != null) progressBar.value = val * 100f;
            if (statusLabel != null) statusLabel.text = status;
        }

        private void CenterAndScaleModel(GameObject go, float targetSize)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            // Calculate current world bounds
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            Vector3 center = b.center;
            float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float factor = (maxDim > 0) ? (targetSize / maxDim) : 1.0f;

            // Calculate the offset required to center the model at the root's position
            Vector3 localCenter = go.transform.InverseTransformPoint(center);

            // Apply normalization (centering and scaling) to children
            // This leaves the root transform at scale (1,1,1) so the UI starts at 1.0
            foreach (Transform child in go.transform)
            {
                child.localPosition = (child.localPosition - localCenter) * factor;
                child.localScale *= factor;
            }
        }

        private void SetupArtifactRoot(GameObject go)
        {
            var baker = go.AddComponent<UVMapBaker>();
            baker.bakeShader = bakeShader != null ? bakeShader : Shader.Find("Hidden/UVSpaceBaker");
            
            if (simulator != null) simulator.baker = baker;
        }

        private void SetupArtifactPart(GameObject go, ChronoProject project)
        {
            var col = go.AddComponent<MeshCollider>();
            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null) col.sharedMesh = meshFilter.sharedMesh;

            ApplySavedMaterial(go, project);
        }

        private void SetupEnvironmentPart(GameObject go, ChronoProject project)
        {
            var col = go.AddComponent<MeshCollider>();
            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null) col.sharedMesh = meshFilter.sharedMesh;

            if (!ApplySavedMaterial(go, project))
            {
                // Default to first preset if no saved material
                if (materialLibrary != null && materialLibrary.presets.Count > 0)
                {
                    var renderer = go.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        var preset = materialLibrary.presets[0];
                        renderer.sharedMaterial = preset.visualMaterial;
                        var sim = go.AddComponent<SimulationMaterial>();
                        sim.reflectance = preset.reflectance;
                        sim.transmittance = preset.transmittance;
                    }
                }
            }
        }

        private bool ApplySavedMaterial(GameObject go, ChronoProject project)
        {
            if (project == null || materialLibrary == null) return false;

            // Use hierarchy path as key to avoid collisions
            string uniqueKey = GetObjectKey(go);
            int idx = project.objectNames.IndexOf(uniqueKey);
            
            if (idx >= 0 && idx < project.materialPresetNames.Count)
            {
                string presetName = project.materialPresetNames[idx];
                var preset = materialLibrary.presets.Find(p => p.materialName == presetName);
                if (preset != null)
                {
                    var renderer = go.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = preset.visualMaterial;
                        var sim = go.GetComponent<SimulationMaterial>() ?? go.AddComponent<SimulationMaterial>();
                        sim.reflectance = preset.reflectance;
                        sim.transmittance = preset.transmittance;
                        return true;
                    }
                }
            }
            return false;
        }

        private string GetObjectKey(GameObject go)
        {
            // Key format: RootName/MeshName
            if (go.transform.parent != null && go.transform.parent != modelRoot)
                return $"{go.transform.parent.name}/{go.name}";
            return go.name;
        }

        public void ClearExistingModels()
        {
            if (_artifactInstance != null) { 
                _artifactInstance.transform.SetParent(null);
                Destroy(Application.isPlaying ? (Object)_artifactInstance : _artifactInstance); 
                _artifactInstance = null; 
            }
            foreach (var env in _environmentInstances) {
                if (env != null) {
                    env.transform.SetParent(null);
                    Destroy(Application.isPlaying ? (Object)env : env);
                }
            }
            _environmentInstances.Clear();
            if (simulator != null) simulator.baker = null;
        }
    }
}
