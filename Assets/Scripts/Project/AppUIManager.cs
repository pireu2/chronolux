using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using ChronoLux.Library;
using ChronoLux.Interaction;
using System;

namespace ChronoLux.Project
{
    public class AppUIManager : MonoBehaviour
    {
        [Header("References")]
        public UIDocument uiDocument;
        public LightDoseSimulator simulator;
        public MaterialLibrary materialLibrary;
        public ObjectPicker picker;
        public FreeLookCamera freeLookCamera; 

        [Header("State")]
        private ChronoProject _currentProject;
        private VisualElement _launcherScreen;
        private VisualElement _dashboardScreen;
        private VisualElement _selectionPanel;
        private ScrollView _projectList;
        private ScrollView _modelList;
        private string _selectedModelFile;
        private GameObject _activeSelection;

        private void OnEnable()
        {
            if (uiDocument == null) return;
            var root = uiDocument.rootVisualElement;

            _launcherScreen = root.Q<VisualElement>("LauncherScreen");
            _dashboardScreen = root.Q<VisualElement>("DashboardScreen");
            _selectionPanel = root.Q<VisualElement>("SelectionPanel");
            _projectList = root.Q<ScrollView>("ProjectList");
            _modelList = root.Q<ScrollView>("ModelList");

            // Launcher
            var btnLaunch = root.Q<Button>("BtnLaunch");
            if (btnLaunch != null) btnLaunch.clicked += OnLaunchClicked;

            // Dashboard
            var btnExit = root.Q<Button>("BtnExit");
            if (btnExit != null) btnExit.clicked += ShowLauncher;

            var btnStart = root.Q<Button>("BtnStart");
            if (btnStart != null) btnStart.clicked += () => { SyncParamsToSimulator(); simulator.StartSimulation(); };

            var btnStop = root.Q<Button>("BtnStop");
            if (btnStop != null) btnStop.clicked += () => simulator.StopSimulation();

            var btnClear = root.Q<Button>("BtnClear");
            if (btnClear != null) btnClear.clicked += () => simulator.AutoScale();

            var btnNav = root.Q<Button>("BtnNav");
            if (btnNav != null) btnNav.clicked += () => {
                UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                UnityEngine.Cursor.visible = false;
            };

            // SPP Slider
            var sldSpp = root.Q<SliderInt>("SldSamples");
            var lblSpp = root.Q<Label>("LblSamplesVal");
            if (sldSpp != null && lblSpp != null)
                sldSpp.RegisterValueChangedCallback(evt => lblSpp.text = evt.newValue.ToString());

            // Reflectance Slider
            var sldRefl = root.Q<Slider>("SldRefl");
            var lblRefl = root.Q<Label>("LblReflVal");
            if (sldRefl != null && lblRefl != null)
            {
                sldRefl.RegisterValueChangedCallback(evt => {
                    lblRefl.text = evt.newValue.ToString("F2");
                    UpdateSelectedMaterial();
                });
            }

            // Transmittance Slider
            var sldTrans = root.Q<Slider>("SldTrans");
            var lblTrans = root.Q<Label>("LblTransVal");
            if (sldTrans != null && lblTrans != null)
            {
                sldTrans.RegisterValueChangedCallback(evt => {
                    lblTrans.text = evt.newValue.ToString("F2");
                    UpdateSelectedMaterial();
                });
            }

            if (picker != null) {
                picker.OnObjectSelected += OnObjectSelected;
                picker.OnSelectionCleared += OnSelectionCleared;
            }

            ShowLauncher();
        }

        public void ShowLauncher()
        {
            if (_launcherScreen == null) return;
            _launcherScreen.style.display = DisplayStyle.Flex;
            if (_dashboardScreen != null) _dashboardScreen.style.display = DisplayStyle.None;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            if (freeLookCamera != null) freeLookCamera.enabled = false;
            RefreshProjectList();
            RefreshModelList();
        }

        public void ShowDashboard()
        {
            if (_launcherScreen != null) _launcherScreen.style.display = DisplayStyle.None;
            if (_dashboardScreen != null) _dashboardScreen.style.display = DisplayStyle.Flex;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            if (freeLookCamera != null) freeLookCamera.enabled = true;
            PopulateMaterialCatalog();
            SyncSimulatorToUI();
        }

        private void OnObjectSelected(GameObject obj)
        {
            _activeSelection = obj;
            if (_selectionPanel != null) _selectionPanel.style.display = DisplayStyle.Flex;
            var lblName = uiDocument.rootVisualElement.Q<Label>("TxtSelectedName");
            if (lblName != null) lblName.text = obj.name;

            var sim = obj.GetComponent<SimulationMaterial>();
            if (sim != null) {
                var sldRefl = uiDocument.rootVisualElement.Q<Slider>("SldRefl");
                var sldTrans = uiDocument.rootVisualElement.Q<Slider>("SldTrans");
                var lblRefl = uiDocument.rootVisualElement.Q<Label>("LblReflVal");
                var lblTrans = uiDocument.rootVisualElement.Q<Label>("LblTransVal");

                if (sldRefl != null) sldRefl.SetValueWithoutNotify(sim.reflectance);
                if (sldTrans != null) sldTrans.SetValueWithoutNotify(sim.transmittance);
                if (lblRefl != null) lblRefl.text = sim.reflectance.ToString("F2");
                if (lblTrans != null) lblTrans.text = sim.transmittance.ToString("F2");
            }
        }

        private void OnSelectionCleared() { _activeSelection = null; if (_selectionPanel != null) _selectionPanel.style.display = DisplayStyle.None; }

        private void UpdateSelectedMaterial()
        {
            if (_activeSelection == null) return;
            var sim = _activeSelection.GetComponent<SimulationMaterial>() ?? _activeSelection.AddComponent<SimulationMaterial>();
            
            var sldRefl = uiDocument.rootVisualElement.Q<Slider>("SldRefl");
            var sldTrans = uiDocument.rootVisualElement.Q<Slider>("SldTrans");
            
            if (sldRefl != null && sldTrans != null)
            {
                float r = sldRefl.value;
                float t = sldTrans.value;
                
                // Energy conservation (probabilistic selection logic)
                float sum = r + t;
                if (sum > 1.0f)
                {
                    r /= sum;
                    t /= sldTrans.value > 0 ? sum : 1.0f; // Simplified re-normalization
                    
                    // Update UI to reflect the forced energy conservation if needed
                    sldRefl.SetValueWithoutNotify(r);
                    sldTrans.SetValueWithoutNotify(t);
                    uiDocument.rootVisualElement.Q<Label>("LblReflVal").text = r.ToString("F2");
                    uiDocument.rootVisualElement.Q<Label>("LblTransVal").text = t.ToString("F2");
                }

                sim.reflectance = r;
                sim.transmittance = t;
            }
        }

        private void SyncParamsToSimulator()
        {
            if (simulator == null) return;
            var root = uiDocument.rootVisualElement;
            int.TryParse(root.Q<TextField>("InYear").value, out simulator.year);
            int.TryParse(root.Q<TextField>("InStartDay").value, out simulator.startDay);
            int.TryParse(root.Q<TextField>("InEndDay").value, out simulator.endDay);
            double.TryParse(root.Q<TextField>("InLat").value, out simulator.latitude);
            double.TryParse(root.Q<TextField>("InLon").value, out simulator.longitude);
            var sld = root.Q<SliderInt>("SldSamples");
            if (sld != null) simulator.samplesPerPixel = sld.value;
        }

        private void SyncSimulatorToUI()
        {
            if (simulator == null) return;
            var root = uiDocument.rootVisualElement;
            var inYear = root.Q<TextField>("InYear"); if (inYear != null) inYear.value = simulator.year.ToString();
            var inStart = root.Q<TextField>("InStartDay"); if (inStart != null) inStart.value = simulator.startDay.ToString();
            var inEnd = root.Q<TextField>("InEndDay"); if (inEnd != null) inEnd.value = simulator.endDay.ToString();
            var inLat = root.Q<TextField>("InLat"); if (inLat != null) inLat.value = simulator.latitude.ToString("F3");
            var inLon = root.Q<TextField>("InLon"); if (inLon != null) inLon.value = simulator.longitude.ToString("F3");
            var sld = root.Q<SliderInt>("SldSamples"); if (sld != null) sld.value = simulator.samplesPerPixel;
            var lbl = root.Q<Label>("LblSamplesVal"); if (lbl != null) lbl.text = simulator.samplesPerPixel.ToString();
        }

        private void RefreshProjectList()
        {
            if (_projectList == null) return;
            _projectList.Clear();
            foreach (var name in ProjectManager.GetAvailableProjects()) {
                var btn = new Button { text = name };
                btn.AddToClassList("list-item");
                btn.clicked += () => LoadExistingProject(name);
                _projectList.Add(btn);
            }
        }

        private void RefreshModelList()
        {
            if (_modelList == null) return;
            _modelList.Clear();
            foreach (var file in ProjectManager.GetAvailableModels()) {
                var btn = new Button { text = file };
                btn.AddToClassList("list-item");
                btn.clicked += () => _selectedModelFile = file;
                _modelList.Add(btn);
            }
        }

        private void OnLaunchClicked()
        {
            var root = uiDocument.rootVisualElement;
            string name = root.Q<TextField>("InputProjectName").value;
            if (string.IsNullOrEmpty(name)) return;
            _currentProject = new ChronoProject { projectName = name, modelFileName = _selectedModelFile };
            ProjectManager.SaveProject(_currentProject);
            ShowDashboard();
        }

        private void LoadExistingProject(string name) { _currentProject = ProjectManager.LoadProject(name); ShowDashboard(); }

        private void PopulateMaterialCatalog()
        {
            var catalog = uiDocument.rootVisualElement.Q<ScrollView>("MaterialCatalog");
            if (catalog == null) return;
            catalog.Clear();
            if (materialLibrary == null) return;
            foreach (var preset in materialLibrary.presets) {
                if (preset == null) continue;
                var btn = new Button { text = preset.materialName };
                btn.AddToClassList("button");
                btn.style.width = 130; btn.style.height = 50;
                btn.clicked += () => {
                    if (picker != null) { picker.AssignMaterialToSelected(preset); OnObjectSelected(_activeSelection); }
                };
                catalog.Add(btn);
            }
        }

        private void Update()
        {
            if (simulator == null) return;
            if (_dashboardScreen != null && _dashboardScreen.style.display == DisplayStyle.Flex)
            {
                var root = uiDocument.rootVisualElement;
                var txtTime = root.Q<Label>("TxtTime"); if (txtTime != null) txtTime.text = simulator.simulatedTime;
                var txtCoords = root.Q<Label>("TxtSunCoords"); if (txtCoords != null) txtCoords.text = $"ALT: {simulator.currentAltitude:F1}° | AZI: {simulator.currentAzimuth:F1}°";
                var txtMax = root.Q<Label>("TxtMaxDose"); if (txtMax != null) txtMax.text = $"{simulator.maxDoseInScene:F0} Lux·h";
                var prog = root.Q<ProgressBar>("ProgSim"); if (prog != null) prog.value = simulator.currentProgress;
            }
        }
    }
}
