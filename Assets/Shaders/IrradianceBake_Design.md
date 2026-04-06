# IrradianceBake — Ray Tracing Design Notes

## For thesis advisor review — March 2026

---

## 1. Goal

Compute cumulative light dose (Lux·Hours) at every surface point of a museum artifact,
accounting for:

- Direct sunlight
- Light blocked by occluders (walls, columns, other objects)
- Light reflected from specular/mirror surfaces
- Light transmitted through transparent surfaces (glass, windows)

**Worst-case assumption:** The simulation uses a clear-sky model for every hour of the year.
No clouds, haze, or atmospheric scattering beyond the standard clear-sky transmittance model.
This is a deliberate conservative choice for conservation science — it computes the maximum
possible annual dose an artifact could receive, giving museum staff the upper bound they need
to evaluate display conditions against conservation thresholds (e.g. 150,000 Lux·Hours/year
for sensitive pigments per CIE 157:2004).

Output: a 2D float texture in UV space (\_DoseMap, same dimensions as PositionMap/NormalMap)
where each texel holds the total dose accumulated over the simulated year.

---

## 2. Inputs (per simulation step, injected from C# each hour)

| Variable       | Type                            | Source                         |
| -------------- | ------------------------------- | ------------------------------ |
| \_PositionMap  | Texture2D                       | UVMapBaker (world XYZ)         |
| \_NormalMap    | Texture2D                       | UVMapBaker (world normals)     |
| \_SunDirection | float3                          | SunCalculator.ToWorldDirection |
| \_BeamLux      | float                           | SunCalculator.BeamLux          |
| \_DeltaHours   | float                           | stepSeconds / 3600 (= 1.0)     |
| \_DoseMap      | RWTexture2D (float)             | Accumulates across ALL steps   |
| Scene RTAS     | RayTracingAccelerationStructure | Built once at start            |

---

## 3. Unity DXR API Requirements

- Shader type: .raytrace (not .compute, not .hlsl surface shader)
- Pragma kernels needed:
  #pragma max_recursion_depth 1 // DXR does NOT support recursion
  // Bounces must be implemented as a manual loop
  // inside RayGen using TraceRay() repeatedly
  #pragma raytracing IrradianceBakeRayGen

- C# side: RayTracingShader asset, dispatched via:
  shader.Dispatch("IrradianceBakeRayGen", texWidth, texHeight, 1, camera)
  NOTE: camera parameter is required by Unity API but can be a dummy camera

- Acceleration structure: RayTracingAccelerationStructure
  Built once in C#, includes all MeshRenderers in scene
  Must be rebuilt if scene geometry changes at runtime

---

## 4. Ray Path — Pseudocode

```
RAYGEN shader (one thread per texel):

    uv     = thread_id / texture_dimensions
    origin = _PositionMap[uv].xyz
    normal = _NormalMap[uv].xyz

    // Discard texels with no geometry (alpha == 0 in PositionMap)
    if _PositionMap[uv].w == 0: return

    // Offset origin along normal to avoid self-intersection (shadow acne)
    origin = origin + normal * RAY_BIAS    // RAY_BIAS ≈ 0.001

    total_irradiance = 0.0

    for i = 0 to N_SAMPLES:

        // Sample 0 is always the sun direction — this is called "next event estimation"
        // and ensures direct sunlight is captured with zero variance (one deterministic
        // sample) while the remaining N-1 random hemisphere rays capture all indirect
        // contributions (reflections, transmissions, skylight from other angles).
        //
        // All samples go through the exact same TracePath function — there is no
        // separate direct/indirect pass. If the sun sample is occluded by a wall,
        // TracePath follows bounces and EvaluateSky returns 0 (direction ≠ sun disk).
        // If a random hemisphere ray happens to bounce off a mirror toward the sun,
        // EvaluateSky returns BeamLux naturally.
        //
        if i == 0:
            direction = _SunDirection
            NdotL = dot(normal, direction)
            if NdotL <= 0: continue   // surface faces away from sun — skip this sample
        else:
            direction = CosineSampleHemisphere(normal)   // random, NdotL always > 0
            NdotL     = dot(normal, direction)

        // pdf of cosine-weighted sampling = NdotL / PI
        // so the Monte Carlo estimator  f * NdotL / pdf  simplifies to  f * PI
        pdf = NdotL / PI

        irradiance = TracePath(origin, direction, throughput=1.0)

        total_irradiance += irradiance * NdotL / pdf   // = irradiance * PI

    // Average over all samples and accumulate dose (Lux·Hours)
    _DoseMap[uv] += (total_irradiance / N_SAMPLES) * _DeltaHours


// ── TracePath — follows a ray through bounces until it escapes or is absorbed ──
FUNCTION TracePath(origin, direction, throughput):
    irradiance = 0.0
    for bounce = 0 to MAX_BOUNCES:
        hit = TraceRay(RTAS, origin, direction, T_MIN=0.001, T_MAX=1e6)

        if hit == MISS:
            // Ray escaped the scene — evaluate sun disk + sky environment
            irradiance += throughput * EvaluateSky(direction, _SunDirection, _BeamLux)
            break

        material = hit.material

        if material._Transmittance > 0.5:       // Glass / window — pass through
            origin     = hit.position + direction * RAY_BIAS
            throughput *= material._Transmittance

        else if material._Reflectance > 0.5:    // Mirror / specular — reflect
            direction  = reflect(direction, hit.normal)
            origin     = hit.position + hit.normal * RAY_BIAS
            throughput *= material._Reflectance

        else:                                   // Diffuse — absorbed, stop
            break

    return irradiance
```

---

## 5. Miss Shader — Sky Evaluation

```
FUNCTION EvaluateSky(rayDir, sunDir, beamLux):

    // Check if ray is aimed directly at the sun disk
    // Angular radius of sun = 0.265 degrees = 0.00463 radians
    SUN_COS_THRESHOLD = cos(0.00463)
    if dot(rayDir, sunDir) > SUN_COS_THRESHOLD:
        return beamLux    // direct sun contribution

    // Clear-sky diffuse gradient (approximate)
    // At horizon (rayDir.y=0): ~5,000 Lux
    // At zenith  (rayDir.y=1): ~15,000 Lux
    t = saturate(rayDir.y)
    return lerp(5000.0, 15000.0, t)
```

NOTE: The clear-sky model is a deliberate worst-case choice — see Section 1.
The Perez All-Weather Sky Model is cited here as the reference for the sky luminance
distribution formula this gradient approximates.
Reference: Perez, R. et al. (1993). "All-weather model for sky luminance distribution."
Solar Energy, 50(3), pp. 235-245. https://doi.org/10.1016/0038-092X(93)90017-I

---

## 6. Material Properties Required on Scene Objects

Each MeshRenderer in the scene needs two float properties on its material:

| Property        | Range | Typical values                                              |
| --------------- | ----- | ----------------------------------------------------------- |
| \_Reflectance   | 0–1   | Mirror: 0.90 / Metal: 0.70 / White wall: 0.80 / Stone: 0.30 |
| \_Transmittance | 0–1   | Clear glass: 0.90 / Tinted glass: 0.60 / Opaque: 0.00       |

Decision rule used in pseudocode:

- transmittance > 0.5 → treat as glass (pass-through)
- reflectance > 0.5 → treat as mirror (reflect)
- otherwise → treat as diffuse (absorb, stop tracing)

NOTE: A single material can have both values set (e.g. tinted reflective glass).
The current pseudocode checks transmittance first. This priority order could be
changed or blended depending on desired physical accuracy.

---

## 7. DXR Shader Structure (files to create)

```
IrradianceBake.raytrace
├── #pragma raytracing IrradianceBakeRayGen
├── [shader("raygeneration")]  IrradianceBakeRayGen()   — main loop above
├── [shader("closesthit")]     ClosestHitMain()         — reads material, returns properties
├── [shader("miss")]           MissMain()               — returns sky irradiance
└── [shader("anyhit")]         AnyHitTransparent()      — optional: alpha cutout skip
```

All four entry points live in the same .raytrace file in Unity.

---

## 8. C# Driver (IrradianceBaker.cs) — Responsibilities

1. Build RayTracingAccelerationStructure at scene start
   - AddInstances() for every MeshRenderer in scene
   - Build()

2. Expose DispatchRays(Vector3 sunDir, float beamLux, float deltaHours):
   - Set shader properties (\_SunDirection, \_BeamLux, \_DeltaHours, \_PositionMap, \_NormalMap, \_DoseMap)
   - shader.Dispatch("IrradianceBakeRayGen", texWidth, texHeight, 1, dummyCamera)

3. Expose GetDoseMap() → RenderTexture for heatmap visualization

LightDoseSimulator.cs calls DispatchRays() each simulation step
(replacing the current TODO comment).

---

## 9. Key Design Decisions to Discuss with Advisor

A) **No GPU recursion** — DXR does not allow recursive TraceRay() calls.
Bounces are a manual for-loop inside RayGen. Max depth = 5 adds 5x GPU cost.
Question: Is 5 bounces scientifically justifiable, or would 2-3 be sufficient
for typical museum scenarios (one glass window + one reflective surface)?

B) **Throughput model is multiplicative** — each bounce multiplies by reflectance
or transmittance. This is physically correct for specular transport but does
NOT model diffuse inter-reflection (color bleeding). Acceptable for dosimetry
since color shifts do not affect total luminous energy.

C) **Lambert factor (NdotL) applied once at the texel, not at each bounce.**
Reflected rays are treated as carrying the full reduced irradiance — this is
correct for specular (mirror-like) reflection where the Lambertian distribution
does not apply to the intermediate surface.

D) **Clear-sky only — deliberate worst-case design choice.**
Modelling real cloud cover would require historical meteorological data for the museum's
location and would produce an average-case dose rather than an upper bound.
For conservation decision-making, the upper bound is more useful: if an artifact is safe
under the worst-case (every day perfectly clear), it is safe under any real-world condition.
CIE 157:2004 "Ocular lighting conditions for seeing details" uses this same convention.
Adding a cloudiness factor is possible but would change the tool's scientific framing.

E) **No spectral simulation.** We simulate total luminous flux (Lux).
For stricter conservation science the simulation could be split into
UV (< 400nm) and Visible (400-700nm) bands, since UV causes disproportionate
photochemical damage. This would require separate BeamIrradiance values per band.

F) **Single unified pass using next event estimation (NEE).**
Sample 0 is always the sun direction (deterministic, zero variance for direct light).
Samples 1..N-1 are random cosine-weighted hemisphere rays (capture all indirect light).
All samples go through the same `TracePath` function — no separate direct/indirect code path.
This is the standard NEE pattern used in production path tracers (e.g. PBRT, Mitsuba).
Reference: Veach, E. (1997). "Robust Monte Carlo Methods for Light Transport Simulation."
Stanford PhD thesis. Chapter 9. https://graphics.stanford.edu/papers/veach_thesis/

Back-facing texels (NdotL ≤ 0 for the sun direction) skip the sun sample but still run
the hemisphere samples and can accumulate indirect dose from mirrors, skylights, etc.

G) **Simplified material model — not full PBR. Deliberate scope decision.**

The current model uses two scalar properties per material: `_Reflectance` and
`_Transmittance`. Reflection is always perfect specular (`reflect()`, roughness = 0).
This is a simplified material model, not a GGX/metallic-roughness PBR model.

Why not full PBR:

- Roughness spreads reflected energy across directions but does not destroy it.
  For total dose accumulation this causes positional spread error, not total energy error.
  A rough mirror and a perfect mirror with the same reflectance deliver the same total
  Lux·Hours to the scene — just distributed differently.
- Implementing full PBR (GGX importance sampling + Fresnel) adds ~50 lines of HLSL
  and requires reading packed roughness/metallic textures from hit surfaces via DXR.
  That is engineering effort that does not change the scientific instrument's output
  significantly for a first-order dosimetry tool.
- Full PBR would require every scene surface to have calibrated metallic/roughness
  values, turning scene authoring into a materials calibration sub-project and making
  it harder to produce a clear, controlled demonstration of the simulation pipeline.

Scientific framing for the thesis:
"Roughness is treated as zero (perfect specular), which represents a conservative
upper bound on focused energy transfer from reflective surfaces. Full GGX BRDF
sampling is a defined future extension."

Future extension — if roughness support is added later, only one line changes:
// Current:
direction = reflect(direction, hit.normal)
// Future:
direction = SampleGGX(hit.normal, roughness, incoming_direction)
Everything else in the pipeline (TracePath, accumulation, C# driver) is unchanged.

For the demonstration scene, three clearly labelled material types are sufficient
to showcase all distinct light transport paths:

- Mirror panel (\_Reflectance = 0.9) — demonstrates specular indirect illumination
- Glass window (\_Transmittance = 0.9) — demonstrates solar transmission
- Stone / diffuse (\_Reflectance = 0.1) — demonstrates occlusion / shadow

--- (full year, 2048×2048 DoseMap)

| Parameter         | Value                                                          |
| ----------------- | -------------------------------------------------------------- |
| Texels            | 2048 × 2048 = 4,194,304                                        |
| Steps / year      | ~365 days × ~13 h avg = ~4,745                                 |
| Rays per step     | N_SAMPLES per texel (sample 0 = sun, rest = random hemisphere) |
| Total rays (N=4)  | ~25 billion                                                    |
| Total rays (N=16) | ~95 billion                                                    |

Hardware RT (DXR) on an RTX-class GPU can do ~5–10 billion rays/second.
Estimated wall time:

- N=1 (sun sample only, no hemisphere), no bounces: ~2 seconds/year
- N=4, 5 bounces: ~30–60 seconds/year
- N=16, 5 bounces: ~2–4 minutes/year
