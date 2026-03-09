using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Drives the light-dose simulation loop.

// For each time step the script:
//   1. Computes the sun's world-space direction + beam irradiance via SunCalculator.
//   2. Orients the scene Directional Light to match that sun position.
//   3. (TODO) Dispatches IrradianceBake.compute to accumulate dose into the DoseMap.
[AddComponentMenu("ChronoLux/Light Dose Simulator")]
public class LightDoseSimulator : MonoBehaviour
{
    [Header("Location")]
    [Tooltip("Geographic latitude in decimal degrees (positive = North).")]
    public double latitude = 44.4268;  // Bucharest, Romania

    [Tooltip("Geographic longitude in decimal degrees (positive = East).")]
    public double longitude = 26.1025;

    [Tooltip("UTC offset in hours. Standard time for Romania = +2 (EET), summer = +3 (EEST).")]
    public double utcOffset = 3.0;     // EEST (Romanian summer)

    [Header("Simulation Time Range")]
    [Tooltip("Year to simulate.")]
    public int year = 2025;

    [Tooltip("First day to simulate (day-of-year, 1 = Jan 1).")]
    public int startDay = 1;

    [Tooltip("Last day to simulate (day-of-year, inclusive).")]
    public int endDay = 365;

    [Tooltip("Duration of each time step in seconds (3600 = 1 hour).")]
    public float stepSeconds = 3600f;

    [Header("Scene References")]
    [Tooltip("The scene's main Directional Light (the Sun).")]
    public Light sunLight;

    [Tooltip("UVMapBaker that has already baked the artifact's PositionMap and NormalMap.")]
    public UVMapBaker baker;


    [Header("Progress (read-only at runtime)")]
    [SerializeField] private string simulatedTime = "–";
    [SerializeField] private float beamLux = 0f;
    [SerializeField] private float azimuthDeg = 0f;
    [SerializeField] private float altitudeDeg = 0f;


    // Current step's sun direction in world space (pointing TOWARD the sun).
    public Vector3 CurrentSunDirection { get; private set; }

    // Beam irradiance in Lux for the current step (perpendicular to sun rays).
    public float CurrentBeamLux { get; private set; }

    // Duration of the current time step in hours (for dose = lux × Δt).
    public float CurrentDeltaHours { get; private set; }

    // Start the simulation from the Inspector context menu or via script.
    [ContextMenu("Run Simulation")]
    public void StartSimulation()
    {
        StopAllCoroutines();
        StartCoroutine(RunSimulation());
    }

    // Preview the sun position at a specific date/time without running the full loop.
    [ContextMenu("Preview Sun At Noon")]
    public void PreviewNoon()
    {
        DateTime noon = new(year, 6, 21, 12, 0, 0); // summer solstice noon
        ApplySunPosition(noon);
        Debug.Log($"[SunCalc] {simulatedTime}  az={azimuthDeg:F1}°  alt={altitudeDeg:F1}°  {beamLux:F0} Lux");
    }

    private IEnumerator RunSimulation()
    {
        if (!ValidateReferences()) yield break;

        float deltaHours = stepSeconds / 3600f;
        CurrentDeltaHours = deltaHours;

        int totalDays = endDay - startDay + 1;
        Debug.Log($"[LightDoseSimulator] Starting. {totalDays} days to simulate (sunrise→sunset each day).");

        int completedSteps = 0;

        for (int day = startDay; day <= endDay; day++)
        {
            DateTime date = new DateTime(year, 1, 1).AddDays(day - 1);

            // Compute sunrise and sunset for this specific day and location.
            // Steps outside this window contribute zero irradiance and are skipped entirely.
            DateTime sunrise = SunCalculator.FindSunrise(date, latitude, longitude, utcOffset);
            DateTime sunset = SunCalculator.FindSunset(date, latitude, longitude, utcOffset);

            if (sunrise == date && sunset == date)
            {
                // Polar night — sun never rises, skip the whole day
                yield return null;
                continue;
            }

            // Step from sunrise to sunset in increments of stepSeconds
            for (DateTime localTime = sunrise; localTime < sunset; localTime = localTime.AddSeconds(stepSeconds))
            {
                // ── 1. Sun position ──────────────────────────────────────────
                ApplySunPosition(localTime);

                // ── 2. Dispatch irradiance accumulator (placeholder) ─────────
                // TODO: When IrradianceBake.compute is ready, call:
                //   DispatchIrradianceBake(CurrentSunDirection, CurrentBeamLux, CurrentDeltaHours);

                // ── 3. Yield so the Editor doesn't freeze ────────────────────
                completedSteps++;
                yield return null;
            }
        }

        Debug.Log("[LightDoseSimulator] Simulation complete.");
    }

    // Compute sun position for <paramref name="localTime"/> and apply it to the scene light.
    // Also updates the public properties and Inspector read-outs.
    private void ApplySunPosition(DateTime localTime)
    {
        SunCalculator.SunPosition sun = SunCalculator.Calculate(latitude, longitude, utcOffset, localTime);
        Vector3 sunDir = SunCalculator.ToWorldDirection(sun);

        // Cache for the compute shader
        CurrentSunDirection = sunDir;
        CurrentBeamLux = sun.BeamLux;

        // Inspector read-outs
        simulatedTime = localTime.ToString("yyyy-MM-dd HH:mm");
        beamLux = sun.BeamLux;
        azimuthDeg = sun.AzimuthDeg;
        altitudeDeg = sun.AltitudeDeg;

        // Drive the Directional Light
        if (sunLight == null) return;

        if (!sun.IsAboveHorizon)
        {
            sunLight.enabled = false;
            return;
        }

        // Light travels FROM the sun TOWARD the scene, so forward = -sunDir
        sunLight.enabled = true;
        sunLight.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.up);

        // Set physical intensity via HDRP light data (unit = Lux)
        // BeamLux is irradiance perpendicular to the sun; HDRP's Lambert BRDF handles dot(N,L) internally.

        sunLight.lightUnit = LightUnit.Lux;
        sunLight.intensity = sun.BeamLux;
    }

    // Logs warnings for any missing references and returns false if critical ones are absent.
    private bool ValidateReferences()
    {
        if (sunLight == null)
            Debug.LogWarning("[LightDoseSimulator] sunLight is not assigned — light will not be driven.");

        if (baker == null)
            Debug.LogWarning("[LightDoseSimulator] baker is not assigned — dose accumulation will be skipped.");

        if (stepSeconds <= 0f)
        {
            Debug.LogError("[LightDoseSimulator] stepSeconds must be > 0.");
            return false;
        }

        return true;
    }
}
