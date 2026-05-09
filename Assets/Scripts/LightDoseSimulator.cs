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

    [Header("Simulation Quality")]
    [Range(1, 64)] public int samplesPerPixel = 8;

    [Header("Simulation Time Range")]
    public int year = 2025;
    public int startDay = 1;
    public int endDay = 365;
    public float stepSeconds = 3600f;

    [Header("Scene References")]
    public Light sunLight;
    public UVMapBaker baker;
    public IrradianceBaker irradianceBaker;
    public string previewTextureProperty = "_DoseMap";

    [Header("Auto-Visualization (Read Only)")]
    [ReadOnly] public float maxDoseInScene = 0f;
    [ReadOnly] public float currentProgress = 0f;

    private bool _isSimulating = false;
    private IEnumerator _simulationEnumerator;
    private MaterialPropertyBlock _propBlock;
    
    // Downsampling resources for accurate auto-scaling
    private RenderTexture _downsampleRT;
    private Texture2D _readbackTex;

    public Vector3 CurrentSunDirection { get; private set; }
    public float CurrentBeamLux { get; private set; }
    public float CurrentDiffuseLux { get; private set; }

    private void OnEnable()
    {
#if UNITY_EDITOR
        EditorApplication.update += EditorUpdate;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorUpdate;
#endif
        CleanDownsampleResources();
    }

    private void CleanDownsampleResources()
    {
        if (_downsampleRT != null) { _downsampleRT.Release(); _downsampleRT = null; }
        if (_readbackTex != null) { DestroyImmediate(_readbackTex); _readbackTex = null; }
    }

    [ContextMenu("Run Simulation")]
    public void StartSimulation()
    {
        if (irradianceBaker == null || baker == null) return;
        if (baker.PositionMap == null) baker.Bake();

        irradianceBaker.Initialize(baker.PositionMap.width, baker.PositionMap.height);
        irradianceBaker.samplesPerPixel = samplesPerPixel;
        
        _isSimulating = true;
        _simulationEnumerator = RunSimulationInternal();
        ApplyDosePreview();
    }

    [ContextMenu("Stop Simulation")]
    public void StopSimulation() { _isSimulating = false; _simulationEnumerator = null; }

    [ContextMenu("Auto-Scale Visuals")]
    public void AutoScale()
    {
        FindMaxDose(forceFullScan: true);
        ApplyDosePreview();
    }

    private void EditorUpdate()
    {
        if (_isSimulating && _simulationEnumerator != null)
        {
            if (!_simulationEnumerator.MoveNext()) StopSimulation();
        }
    }

    [ContextMenu("Test Static Bake (1 Hour)")]
    public void TestStaticBake()
    {
        if (irradianceBaker == null || baker == null) return;
        if (baker.PositionMap == null) baker.Bake();
        irradianceBaker.Initialize(baker.PositionMap.width, baker.PositionMap.height);
        irradianceBaker.samplesPerPixel = samplesPerPixel;

        ApplySunPosition(new DateTime(year, 6, 21, 12, 0, 0));
        irradianceBaker.DispatchRays(CurrentSunDirection, CurrentBeamLux, CurrentDiffuseLux, 1.0f, baker.PositionMap, baker.NormalMap);
        
        AutoScale();
    }

    private IEnumerator RunSimulationInternal()
    {
        float deltaHours = stepSeconds / 3600f;
        int totalDays = endDay - startDay + 1;
        int currentDayIdx = 0;

        for (int day = startDay; day <= endDay; day++)
        {
            DateTime date = new DateTime(year, 1, 1).AddDays(day - 1);
            currentDayIdx++;
            DateTime sunrise = SunCalculator.FindSunrise(date, latitude, longitude, utcOffset);
            DateTime sunset = SunCalculator.FindSunset(date, latitude, longitude, utcOffset);
            if (sunrise == date && sunset == date) continue;

            for (DateTime localTime = sunrise; localTime < sunset; localTime = localTime.AddSeconds(stepSeconds))
            {
                if (!_isSimulating) yield break;
                ApplySunPosition(localTime);
                irradianceBaker.DispatchRays(CurrentSunDirection, CurrentBeamLux, CurrentDiffuseLux, deltaHours, baker.PositionMap, baker.NormalMap);
                currentProgress = ((float)currentDayIdx / totalDays) * 100f;
                
                if (completedSteps() % 20 == 0) {
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

    private int completedSteps() { return (int)((currentProgress/100f) * (endDay-startDay+1) * (12/ (stepSeconds/3600f))); } // Approximation for modulo

    public void ApplyDosePreview()
    {
        if (irradianceBaker == null || irradianceBaker.DoseMap == null) return;
        Renderer r = baker != null ? baker.GetComponentInChildren<Renderer>(true) : null;
        if (r == null) return;

        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        r.GetPropertyBlock(_propBlock);

        _propBlock.SetTexture(previewTextureProperty, irradianceBaker.DoseMap);
        _propBlock.SetFloat("_MinDose", 0f);
        // Use the scanned max, or fallback to a sensible default if zero
        _propBlock.SetFloat("_MaxDose", maxDoseInScene > 1f ? maxDoseInScene : 100000f);
        _propBlock.SetTexture("_BaseColorMap", irradianceBaker.DoseMap);

        r.SetPropertyBlock(_propBlock);
    }

    private void FindMaxDose(bool forceFullScan)
    {
        if (irradianceBaker == null || irradianceBaker.DoseMap == null) return;

        int sampleRes = forceFullScan ? 256 : 64; // Higher res for manual button
        
        if (_downsampleRT == null || _downsampleRT.width != sampleRes)
        {
            if (_downsampleRT != null) _downsampleRT.Release();
            _downsampleRT = new RenderTexture(sampleRes, sampleRes, 0, RenderTextureFormat.RFloat);
            _downsampleRT.Create();
        }

        if (_readbackTex == null || _readbackTex.width != sampleRes)
        {
            if (_readbackTex != null) DestroyImmediate(_readbackTex);
            _readbackTex = new Texture2D(sampleRes, sampleRes, TextureFormat.RFloat, false);
        }

        // Use Hardware Blit to accurately downsample the entire texture
        Graphics.Blit(irradianceBaker.DoseMap, _downsampleRT);

        RenderTexture activeRT = RenderTexture.active;
        RenderTexture.active = _downsampleRT;
        _readbackTex.ReadPixels(new Rect(0, 0, sampleRes, sampleRes), 0, 0);
        _readbackTex.Apply();
        RenderTexture.active = activeRT;

        float maxVal = 0f;
        var data = _readbackTex.GetRawTextureData<float>();
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] > maxVal) maxVal = data[i];
        }

        maxDoseInScene = maxVal;
    }

    private void ApplySunPosition(DateTime localTime)
    {
        SunCalculator.SunPosition sun = SunCalculator.Calculate(latitude, longitude, utcOffset, localTime);
        Vector3 sunDir = SunCalculator.ToWorldDirection(sun);
        CurrentSunDirection = sunDir;
        CurrentBeamLux = sun.BeamLux;
        CurrentDiffuseLux = sun.DiffuseLux;

        if (sunLight == null) return;
        if (!sun.IsAboveHorizon) { sunLight.enabled = false; return; }
        sunLight.enabled = true;
        sunLight.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.up);
        sunLight.lightUnit = LightUnit.Lux;
        sunLight.intensity = sun.BeamLux;
    }
}
