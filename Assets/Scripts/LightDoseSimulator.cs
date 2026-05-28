using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;

    public struct SensorDataSnapshot {
        public float lux;
        public float dose;
        public float errorPct;
    }
    public struct HourlySnapshot {
        public int day;
        public string timeStr;
        public float altitude;
        public float azimuth;
        public float beamLux;
        public float diffuseLux;
        public float expectedSurfaceArea;
        public List<SensorDataSnapshot> sensors;
        public float[] doseMapData;
    }

[ExecuteAlways]
[AddComponentMenu("ChronoLux/Light Dose Simulator")]
public class LightDoseSimulator : MonoBehaviour
{
    [Header("Location")]
    public double latitude = 44.4268;
    public double longitude = 26.1025;
    public double utcOffset = 3.0;

    [Header("Simulation Time Range")]
    public int year = 2025;
    public int startDay = 1;
    public int endDay = 365;
    public float stepSeconds = 3600f;

    [Header("Simulation Quality")]
    [Range(1, 64)] public int samplesPerPixel = 8;
    public int bakedResolution = 1024;
    
    [Header("Data Export Settings")]
    [Tooltip("Resolution of the background snapshot (Higher = more accurate data, but uses more RAM)")]
    public int exportResolution = 256;
    [Tooltip("Lux-Hours required for a pixel to be considered 'covered' in the coverage metric")]
    public float coverageThreshold = 5.0f;

    [Header("Scene References")]
    public Light sunLight;
    public UVMapBaker baker;
    public IrradianceBaker irradianceBaker;
    public string previewTextureProperty = "_DoseMap";

    [Header("Auto-Visualization (Read Only)")]
    [ReadOnly] public string simulatedTime = "–";
    [ReadOnly] public float maxDoseInScene = 0f;
    [ReadOnly] public float surfaceCoverage = 0f;
    [ReadOnly] public float currentProgress = 0f;
    [ReadOnly] public int completedSteps = 0;
    [ReadOnly] public float currentAltitude = 0f;
    [ReadOnly] public float currentAzimuth = 0f;
    [ReadOnly] public float minDoseInScene = 0f;
    [ReadOnly] public float averageDoseInScene = 0f;
    [ReadOnly] public float doseVarianceInScene = 0f;
    [ReadOnly] public float deltaMaxDose = 0f;
    [ReadOnly] public float deltaAvgDose = 0f;

    public List<RenderTexture> historyMaps = new List<RenderTexture>();
    public int previewHistoryIndex = -1;

    public bool IsSimulating => _isSimulating;

    private bool _isSimulating = false;
    private IEnumerator _simulationEnumerator;
    private MaterialPropertyBlock _propBlock;
    private RenderTexture _downsampleRT;
    private Texture2D _readbackTex;
    private List<HourlySnapshot> _snapshotList = new List<HourlySnapshot>();

    public Vector3 CurrentSunDirection { get; private set; }
    public float CurrentBeamLux { get; private set; }
    public float CurrentDiffuseLux { get; private set; }

    private void OnValidate() { if (!Application.isPlaying) ApplyDosePreview(); }
    private void OnEnable() {
#if UNITY_EDITOR
        EditorApplication.update += EditorUpdate;
#endif
    }
    private void OnDisable() {
#if UNITY_EDITOR
        EditorApplication.update -= EditorUpdate;
#endif
        CleanDownsampleResources();
    }

    private void CleanDownsampleResources() {
        if (_downsampleRT != null) { _downsampleRT.Release(); _downsampleRT = null; }
        if (_readbackTex != null) { 
            if (Application.isPlaying) Destroy(_readbackTex);
            else DestroyImmediate(_readbackTex);
            _readbackTex = null; 
        }
    }

    [ContextMenu("Run Simulation")]
    public void StartSimulation() {
        if (!ValidateReferences()) return;
        baker.Bake(bakedResolution);
        irradianceBaker.Initialize(bakedResolution, bakedResolution);
        completedSteps = 0;
        _snapshotList.Clear();
        
        foreach(var rt in historyMaps) if (rt != null) rt.Release();
        historyMaps.Clear();
        previewHistoryIndex = -1;

        foreach (var sensor in VirtualLuxSensor.AllSensors) {
            sensor.ResetDose();
        }

        _isSimulating = true;
        _simulationEnumerator = RunSimulationInternal();
        ApplyDosePreview();
    }

    [ContextMenu("Stop Simulation")]
    public void StopSimulation() { _isSimulating = false; _simulationEnumerator = null; }

    [ContextMenu("Clear Dose Map")]
    public void ClearDoseMap()
    {
        if (irradianceBaker != null) irradianceBaker.ClearDoseMap();
        maxDoseInScene = 0;
        minDoseInScene = 0;
        averageDoseInScene = 0;
        foreach(var rt in historyMaps) if (rt != null) rt.Release();
        historyMaps.Clear();
        previewHistoryIndex = -1;
        ApplyDosePreview();
    }

    [ContextMenu("Auto-Scale Visuals")]
    public void AutoScale() { FindMaxDose(true); ApplyDosePreview(); }

    private void EditorUpdate() {
        if (_isSimulating && _simulationEnumerator != null) if (!_simulationEnumerator.MoveNext()) StopSimulation();
    }

    [ContextMenu("Test Static Bake (1 Hour)")]
    public void TestStaticBake() {
        if (!ValidateReferences()) return;
        baker.Bake(bakedResolution);
        irradianceBaker.Initialize(bakedResolution, bakedResolution);
        ApplySunPosition(new DateTime(year, 6, 21, 12, 0, 0));
        irradianceBaker.DispatchRays(CurrentSunDirection, CurrentBeamLux, CurrentDiffuseLux, 1.0f, samplesPerPixel, baker.PositionMap, baker.NormalMap);
        AutoScale();
    }

    private IEnumerator RunSimulationInternal() {
        if (stepSeconds <= 0) { Debug.LogError("[ChronoLux] stepSeconds must be > 0"); yield break; }
        float deltaHours = stepSeconds / 3600f;
        int totalDays = endDay - startDay + 1;
        int currentDayIdx = 0;

        string exportDir = Path.Combine(Application.dataPath, "../Exports", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(exportDir);

        for (int day = startDay; day <= endDay; day++) {
            DateTime date = new DateTime(year, 1, 1).AddDays(day - 1);
            currentDayIdx++;
            DateTime sunrise = SunCalculator.FindSunrise(date, latitude, longitude, utcOffset);
            DateTime sunset = SunCalculator.FindSunset(date, latitude, longitude, utcOffset);
            if (sunrise == date && sunset == date) continue;
            for (DateTime localTime = sunrise; localTime < sunset; localTime = localTime.AddSeconds(stepSeconds)) {
                if (!_isSimulating) yield break;
                ApplySunPosition(localTime);
                
                // Skip exact sunrise/sunset steps where there is functionally no light
                if (CurrentBeamLux < 0.1f && CurrentDiffuseLux < 0.1f) {
                    continue;
                }

                irradianceBaker.DispatchRays(CurrentSunDirection, CurrentBeamLux, CurrentDiffuseLux, deltaHours, samplesPerPixel, baker.PositionMap, baker.NormalMap);
                completedSteps++;
                currentProgress = ((float)currentDayIdx / totalDays) * 100f;

                TakeAsyncSnapshot(day, localTime.ToString("yyyy-MM-dd HH:mm"), currentAltitude, currentAzimuth, CurrentBeamLux, CurrentDiffuseLux);

                if (completedSteps % 10 == 0) { 
                    ApplyDosePreview(); 
#if UNITY_EDITOR
                    SceneView.RepaintAll(); 
#endif
                }
                yield return null;
            }
            
            ExportDailyMap(exportDir, day);
        }
        AutoScale();
        ExportDailyMap(exportDir, -1);
    }

    private void ExportDailyMap(string exportDir, int day) {
        if (irradianceBaker == null || irradianceBaker.DoseMap == null) return;
        
        RenderTexture doseRt = irradianceBaker.DoseMap;
        RenderTexture historyRt = new RenderTexture(doseRt.width, doseRt.height, 0, RenderTextureFormat.RFloat);
        historyRt.enableRandomWrite = true;
        historyRt.Create();
        Graphics.Blit(doseRt, historyRt);
        historyMaps.Add(historyRt);
        previewHistoryIndex = historyMaps.Count - 1;

        RenderTexture activeRT = RenderTexture.active;
        RenderTexture.active = doseRt;
        Texture2D tex = new Texture2D(doseRt.width, doseRt.height, TextureFormat.RFloat, false);
        tex.ReadPixels(new Rect(0, 0, doseRt.width, doseRt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = activeRT;

        byte[] exrBytes = ImageConversion.EncodeToEXR(tex, Texture2D.EXRFlags.OutputAsFloat);
        string filename = day == -1 ? "FinalDoseMap.exr" : $"DoseMap_Day{day:D3}.exr";
        File.WriteAllBytes(Path.Combine(exportDir, filename), exrBytes);
        
        if (Application.isPlaying) Destroy(tex); else DestroyImmediate(tex);
    }

    public void ApplyDosePreview() {
        if (irradianceBaker == null || irradianceBaker.DoseMap == null) return;
        Renderer r = baker != null ? baker.GetComponentInChildren<Renderer>(true) : null;
        if (r == null) return;
        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        r.GetPropertyBlock(_propBlock);
        
        Texture previewTex = irradianceBaker.DoseMap;
        if (previewHistoryIndex >= 0 && previewHistoryIndex < historyMaps.Count) {
            previewTex = historyMaps[previewHistoryIndex];
        }
        
        _propBlock.SetTexture(previewTextureProperty, previewTex);
        _propBlock.SetFloat("_MinDose", 0f);
        _propBlock.SetFloat("_MaxDose", maxDoseInScene > 1f ? maxDoseInScene : 100000f);
        r.SetPropertyBlock(_propBlock);
    }

    public void SetPreviewHistoryIndex(int index) {
        previewHistoryIndex = Mathf.Clamp(index, -1, historyMaps.Count - 1);
        ApplyDosePreview();
    }

    private void TakeAsyncSnapshot(int day, string timeStr, float alt, float azi, float beam, float diff) {
        if (irradianceBaker == null || irradianceBaker.DoseMap == null) return;
        int res = exportResolution;
        if (_downsampleRT == null || _downsampleRT.width != res) {
            if (_downsampleRT != null) _downsampleRT.Release();
            _downsampleRT = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat);
            _downsampleRT.Create();
        }
        Graphics.Blit(irradianceBaker.DoseMap, _downsampleRT);
        
        float expectedArea = baker.SurfacePixelCount * ((float)res / bakedResolution) * ((float)res / bakedResolution);
        
        var sensorsSnap = new List<SensorDataSnapshot>();
        foreach (var s in VirtualLuxSensor.AllSensors) {
            sensorsSnap.Add(new SensorDataSnapshot { lux = s.currentLux, dose = s.currentDose, errorPct = s.errorPercentage });
        }

        AsyncGPUReadback.Request(_downsampleRT, 0, TextureFormat.RFloat, (req) => {
            if (req.hasError) return;
            var data = req.GetData<float>();
            float[] mapData = data.ToArray();
            
            _snapshotList.Add(new HourlySnapshot {
                day = day, timeStr = timeStr, altitude = alt, azimuth = azi,
                beamLux = beam, diffuseLux = diff, sensors = sensorsSnap,
                doseMapData = mapData, expectedSurfaceArea = expectedArea
            });
            
            // Update UI variables occasionally so dashboard is responsive
            float maxVal = 0f; float minVal = float.MaxValue; double sumVal = 0.0; int hitCount = 0; int coverageHits = 0;
            for (int i = 0; i < mapData.Length; i++) {
                float val = mapData[i];
                if (val > 1e-4f) {
                    if (val > maxVal) maxVal = val;
                    if (val < minVal) minVal = val;
                    sumVal += val; hitCount++;
                }
                if (val > coverageThreshold) coverageHits++;
            }
            if (hitCount == 0) minVal = 0;
            maxDoseInScene = maxVal;
            averageDoseInScene = hitCount > 0 ? (float)(sumVal / expectedArea) : 0f;
            surfaceCoverage = Mathf.Clamp(((float)coverageHits / expectedArea) * 100f, 0f, 100f);
        });
    }

    public void StartExportCSV(VisualElement loadingScreen, ProgressBar bar, Label status) {
        StartCoroutine(ExportSimulationDataCSV(loadingScreen, bar, status));
    }

    private IEnumerator ExportSimulationDataCSV(VisualElement loadingScreen, ProgressBar bar, Label status) {
        if (_snapshotList.Count == 0) {
            loadingScreen.style.display = DisplayStyle.None;
            yield break;
        }

        string exportDir = Path.Combine(Application.dataPath, "../Exports", DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_CSV");
        Directory.CreateDirectory(exportDir);
        string csvPath = Path.Combine(exportDir, "SimulationMetrics.csv");

        int total = _snapshotList.Count;
        float[] prevMapData = null;

        using (StreamWriter writer = new StreamWriter(csvPath)) {
            writer.WriteLine($"Day,Time,Altitude,Azimuth,BeamLux,DiffuseLux,DeltaMaxDose,DeltaAvgDose,MaxDose,AvgDose,MinDose,DoseVariance,Coverage,AvgSensorLux,AvgSensorDose,AvgSensorErrorPct");

            for (int i = 0; i < total; i++) {
                var snap = _snapshotList[i];
                if (i % 10 == 0) {
                    bar.value = ((float)i / total) * 100f;
                    status.text = $"Processing hour {i} of {total} ({snap.timeStr})...";
                    yield return null;
                }

                float maxCumulative = 0f; float minCumulative = float.MaxValue; double sumCumulative = 0.0; 
                float maxDelta = 0f; float minDelta = float.MaxValue; double sumDelta = 0.0; double sumSqrDelta = 0.0;
                int activePixels = 0; int coverageHits = 0;
                
                for (int p = 0; p < snap.doseMapData.Length; p++) {
                    float currentVal = snap.doseMapData[p];
                    if (currentVal > 1e-5f) activePixels++;
                    if (currentVal > coverageThreshold) coverageHits++;
                    
                    float prevVal = prevMapData != null ? prevMapData[p] : 0f;
                    float deltaVal = currentVal - prevVal;
                    if (deltaVal < 0f) deltaVal = 0f;

                    if (currentVal > maxCumulative) maxCumulative = currentVal;
                    if (currentVal > 1e-4f && currentVal < minCumulative) minCumulative = currentVal;
                    sumCumulative += currentVal;

                    if (deltaVal > maxDelta) maxDelta = deltaVal;
                    if (currentVal > 1e-4f && deltaVal < minDelta) minDelta = deltaVal;
                    
                    sumDelta += deltaVal;
                    sumSqrDelta += (double)deltaVal * deltaVal;
                }
                if (activePixels == 0) minCumulative = 0;
                if (minDelta == float.MaxValue) minDelta = 0;
                
                float expectedArea = snap.expectedSurfaceArea;
                if (expectedArea < 1f) expectedArea = 1f;

                float avgCumulative = (float)(sumCumulative / expectedArea);
                float avgDelta = (float)(sumDelta / expectedArea);
                
                float hourlyVariance = 0f;
                if (activePixels > 1) {
                    double mean = sumDelta / expectedArea;
                    hourlyVariance = (float)((sumSqrDelta / expectedArea) - (mean * mean));
                    if (hourlyVariance < 0f) hourlyVariance = 0f;
                }
                
                float coverage = Mathf.Clamp(((float)coverageHits / expectedArea) * 100f, 0f, 100f);

                prevMapData = snap.doseMapData;

                float avgLux = 0f, avgDose = 0f, avgError = 0f;
                if (snap.sensors.Count > 0) {
                    foreach (var s in snap.sensors) {
                        avgLux += s.lux;
                        avgDose += s.dose;
                        avgError += s.errorPct;
                    }
                    avgLux /= snap.sensors.Count;
                    avgDose /= snap.sensors.Count;
                    avgError /= snap.sensors.Count;
                }

                writer.WriteLine($"{snap.day},{snap.timeStr},{snap.altitude},{snap.azimuth},{snap.beamLux},{snap.diffuseLux},{maxDelta},{avgDelta},{maxCumulative},{avgCumulative},{minCumulative},{hourlyVariance},{coverage},{avgLux},{avgDose},{avgError}");
            }
        }

        loadingScreen.style.display = DisplayStyle.None;
    }

    private void FindMaxDose(bool forceFullScan) {
        if (irradianceBaker == null || irradianceBaker.DoseMap == null) return;
        int res = forceFullScan ? 256 : 64;
        if (_downsampleRT == null || _downsampleRT.width != res) {
            if (_downsampleRT != null) _downsampleRT.Release();
            _downsampleRT = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat);
            _downsampleRT.Create();
        }
        if (_readbackTex == null || _readbackTex.width != res) {
            if (_readbackTex != null) {
                if (Application.isPlaying) Destroy(_readbackTex);
                else DestroyImmediate(_readbackTex);
            }
            _readbackTex = new Texture2D(res, res, TextureFormat.RFloat, false);
        }
        Graphics.Blit(irradianceBaker.DoseMap, _downsampleRT);
        RenderTexture activeRT = RenderTexture.active;
        RenderTexture.active = _downsampleRT;
        _readbackTex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        _readbackTex.Apply();
        RenderTexture.active = activeRT;
        float maxVal = 0f;
        float minVal = float.MaxValue;
        double sumVal = 0.0;
        int hitCount = 0;
        var data = _readbackTex.GetRawTextureData<float>();
        for (int i = 0; i < data.Length; i++) {
            float val = data[i];
            if (val > 1e-4f) {
                if (val > maxVal) maxVal = val;
                if (val < minVal) minVal = val;
                sumVal += val;
                hitCount++;
            }
        }
        if (hitCount == 0) minVal = 0;
        maxDoseInScene = maxVal;
        minDoseInScene = minVal;

        float downsampleRatio = (float)res / bakedResolution;
        float expectedSurfaceArea = baker.SurfacePixelCount * (downsampleRatio * downsampleRatio);
        if (expectedSurfaceArea < 1f) expectedSurfaceArea = 1f;

        averageDoseInScene = hitCount > 0 ? (float)(sumVal / expectedSurfaceArea) : 0f;
        surfaceCoverage = Mathf.Clamp(((float)hitCount / expectedSurfaceArea) * 100f, 0f, 100f);
    }

    private void ApplySunPosition(DateTime localTime) {
        SunCalculator.SunPosition sun = SunCalculator.Calculate(latitude, longitude, utcOffset, localTime);
        Vector3 sunDir = SunCalculator.ToWorldDirection(sun);
        CurrentSunDirection = sunDir; CurrentBeamLux = sun.BeamLux; CurrentDiffuseLux = sun.DiffuseLux;
        simulatedTime = localTime.ToString("yyyy-MM-dd HH:mm");
        currentAltitude = sun.AltitudeDeg;
        currentAzimuth = sun.AzimuthDeg;
        if (sunLight == null) return;
        if (!sun.IsAboveHorizon) { sunLight.enabled = false; return; }
        sunLight.enabled = true;
        sunLight.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.up);
        sunLight.lightUnit = LightUnit.Lux; sunLight.intensity = sun.BeamLux;
    }

    private bool ValidateReferences() {
        if (baker == null || irradianceBaker == null) return false;
        if (stepSeconds <= 0) return false;
        return true;
    }
}
