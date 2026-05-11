using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

#if UNITY_EDITOR
using UnityEditor;
#endif

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

    public bool IsSimulating => _isSimulating;

    private bool _isSimulating = false;
    private IEnumerator _simulationEnumerator;
    private MaterialPropertyBlock _propBlock;
    private RenderTexture _downsampleRT;
    private Texture2D _readbackTex;

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
        if (baker.PositionMap == null || baker.PositionMap.width != bakedResolution) baker.Bake(bakedResolution);
        irradianceBaker.Initialize(bakedResolution, bakedResolution);
        completedSteps = 0;
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
        if (baker.PositionMap == null || baker.PositionMap.width != bakedResolution) baker.Bake(bakedResolution);
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
        for (int day = startDay; day <= endDay; day++) {
            DateTime date = new DateTime(year, 1, 1).AddDays(day - 1);
            currentDayIdx++;
            DateTime sunrise = SunCalculator.FindSunrise(date, latitude, longitude, utcOffset);
            DateTime sunset = SunCalculator.FindSunset(date, latitude, longitude, utcOffset);
            if (sunrise == date && sunset == date) continue;
            for (DateTime localTime = sunrise; localTime < sunset; localTime = localTime.AddSeconds(stepSeconds)) {
                if (!_isSimulating) yield break;
                ApplySunPosition(localTime);
                irradianceBaker.DispatchRays(CurrentSunDirection, CurrentBeamLux, CurrentDiffuseLux, deltaHours, samplesPerPixel, baker.PositionMap, baker.NormalMap);
                completedSteps++;
                currentProgress = ((float)currentDayIdx / totalDays) * 100f;
                if (completedSteps % 10 == 0) { 
                    FindMaxDose(false); 
                    ApplyDosePreview(); 
#if UNITY_EDITOR
                    SceneView.RepaintAll(); 
#endif
                }
                yield return null;
            }
        }
        AutoScale();
    }

    public void ApplyDosePreview() {
        if (irradianceBaker == null || irradianceBaker.DoseMap == null) return;
        Renderer r = baker != null ? baker.GetComponentInChildren<Renderer>(true) : null;
        if (r == null) return;
        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        r.GetPropertyBlock(_propBlock);
        _propBlock.SetTexture(previewTextureProperty, irradianceBaker.DoseMap);
        _propBlock.SetFloat("_MinDose", 0f);
        _propBlock.SetFloat("_MaxDose", maxDoseInScene > 1f ? maxDoseInScene : 100000f);
        r.SetPropertyBlock(_propBlock);
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
        int hitCount = 0;
        var data = _readbackTex.GetRawTextureData<float>();
        for (int i = 0; i < data.Length; i++) {
            if (data[i] > maxVal) maxVal = data[i];
            if (data[i] > 1e-4f) hitCount++;
        }
        maxDoseInScene = maxVal;

        // SCIENTIFIC NORMALIZATION:
        // Divide hit pixels by the ACTUAL number of pixels the object occupies (ignoring UV empty space)
        // We must scale the SurfacePixelCount to match our current downsampled 'res'
        float downsampleRatio = (float)res / bakedResolution;
        float expectedSurfaceArea = baker.SurfacePixelCount * (downsampleRatio * downsampleRatio);
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
