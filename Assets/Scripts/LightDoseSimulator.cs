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

    [Header("Visualization Settings")]
    [Tooltip("Adjust THESE sliders to change the heatmap. Do not use the Material sliders.")]
    public float minExposureRange = 0f;
    public float maxExposureLimit = 5000000f;
    public float criticalLimit = 10000000f;
    public bool useLogScale = false;
    [Range(0f, 1f)] public float shadowVisibility = 0.2f;

    [Header("Scene References")]
    public Light sunLight;
    public UVMapBaker baker;
    public IrradianceBaker irradianceBaker;
    public string previewTextureProperty = "_DoseMap";

    [Header("Progress (read-only)")]
    public string simulatedTime = "–";
    public float progressPercent = 0f;
    public int completedSteps = 0;
    [ReadOnly, SerializeField] private float currentMaxDoseFound = 0f;

    private bool _isSimulating = false;
    private IEnumerator _simulationEnumerator;
    private MaterialPropertyBlock _propBlock;

    public Vector3 CurrentSunDirection { get; private set; }
    public float CurrentBeamLux { get; private set; }
    public float CurrentDiffuseLux { get; private set; }
    public float CurrentDeltaHours { get; private set; }

    private void OnValidate()
    {
        // This makes the sliders "Live" in the editor
        if (!Application.isPlaying) ApplyDosePreview();
    }

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
    }

    [ContextMenu("Run Simulation")]
    public void StartSimulation()
    {
        if (irradianceBaker == null || baker == null) return;
        if (baker.PositionMap == null) baker.Bake();
        irradianceBaker.Initialize(baker.PositionMap.width, baker.PositionMap.height);
        irradianceBaker.samplesPerPixel = samplesPerPixel;
        
        completedSteps = 0;
        _isSimulating = true;
        _simulationEnumerator = RunSimulationInternal();
        ApplyDosePreview();
    }

    [ContextMenu("Stop Simulation")]
    public void StopSimulation() { _isSimulating = false; _simulationEnumerator = null; }

    [ContextMenu("Auto-Scale Visuals")]
    public void AutoScale()
    {
        if (irradianceBaker == null || irradianceBaker.DoseMap == null) return;
        
        RenderTexture activeRT = RenderTexture.active;
        RenderTexture.active = irradianceBaker.DoseMap;
        Texture2D tempTex = new Texture2D(irradianceBaker.DoseMap.width, irradianceBaker.DoseMap.height, TextureFormat.RFloat, false);
        tempTex.ReadPixels(new Rect(0, 0, tempTex.width, tempTex.height), 0, 0);
        tempTex.Apply();
        RenderTexture.active = activeRT;

        float maxVal = 0f;
        Color[] pixels = tempTex.GetPixels();
        for (int i = 0; i < pixels.Length; i++) if (pixels[i].r > maxVal) maxVal = pixels[i].r;
        
        currentMaxDoseFound = maxVal;
        maxExposureLimit = maxVal * 1.1f; 
        criticalLimit = maxVal * 2.0f;
        
        if (Application.isPlaying) Destroy(tempTex);
        else DestroyImmediate(tempTex);
        
        ApplyDosePreview();
        Debug.Log($"[ChronoLux] Auto-scaled to Max Dose: {maxVal:F0} Lux*Hours");
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
        ApplyDosePreview();
        ApplySunPosition(new DateTime(year, 6, 21, 12, 0, 0));
        irradianceBaker.DispatchRays(CurrentSunDirection, CurrentBeamLux, CurrentDiffuseLux, 1.0f, baker.PositionMap, baker.NormalMap);
        AutoScale();
    }

    private IEnumerator RunSimulationInternal()
    {
        float deltaHours = stepSeconds / 3600f;
        CurrentDeltaHours = deltaHours;
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
                irradianceBaker.DispatchRays(CurrentSunDirection, CurrentBeamLux, CurrentDiffuseLux, CurrentDeltaHours, baker.PositionMap, baker.NormalMap);
                completedSteps++;
                progressPercent = ((float)currentDayIdx / totalDays) * 100f;
                if (completedSteps % 10 == 0) { ApplyDosePreview(); SceneView.RepaintAll(); }
                yield return null;
            }
        }
        ApplyDosePreview();
    }

    public void ApplyDosePreview()
    {
        if (irradianceBaker == null || irradianceBaker.DoseMap == null) return;
        Renderer r = baker != null ? baker.GetComponentInChildren<Renderer>(true) : null;
        if (r == null) return;

        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        r.GetPropertyBlock(_propBlock);

        _propBlock.SetTexture(previewTextureProperty, irradianceBaker.DoseMap);
        _propBlock.SetFloat("_MinDose", minExposureRange);
        _propBlock.SetFloat("_MaxDose", maxExposureLimit);
        _propBlock.SetFloat("_CriticalLimit", criticalLimit);
        _propBlock.SetFloat("_ShadowVisibility", shadowVisibility);
        _propBlock.SetFloat("_UseLogScale", useLogScale ? 1.0f : 0.0f);
        _propBlock.SetTexture("_BaseColorMap", irradianceBaker.DoseMap);

        r.SetPropertyBlock(_propBlock);
    }

    private void ApplySunPosition(DateTime localTime)
    {
        SunCalculator.SunPosition sun = SunCalculator.Calculate(latitude, longitude, utcOffset, localTime);
        Vector3 sunDir = SunCalculator.ToWorldDirection(sun);
        CurrentSunDirection = sunDir;
        CurrentBeamLux = sun.BeamLux;
        CurrentDiffuseLux = sun.DiffuseLux;
        simulatedTime = localTime.ToString("yyyy-MM-dd HH:mm");
        if (sunLight == null) return;
        if (!sun.IsAboveHorizon) { sunLight.enabled = false; return; }
        sunLight.enabled = true;
        sunLight.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.up);
        sunLight.lightUnit = LightUnit.Lux;
        sunLight.intensity = sun.BeamLux;
    }

    private bool ValidateReferences() => baker != null && irradianceBaker != null;
}
