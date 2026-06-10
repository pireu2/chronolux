#ifndef CHRONOLUX_PATHTRACER_INCLUDED
#define CHRONOLUX_PATHTRACER_INCLUDED

#include "Common.hlsl"
#include "Random.hlsl"
#include "SkyModel.hlsl"

// ── SHARED INPUTS ────────────────────────────────────────────────────────────
// These must be declared in the top-level .raytrace file before including this.
/*
RaytracingAccelerationStructure _SceneRTAS;
float3 _SunDirection;
float _BeamLux;
float _DiffuseLux;
*/

// ── PATH TRACING ────────────────────────────────────────────────────────────
float TracePath(float3 origin, float3 direction, float3 surfaceNormal, inout uint seed, bool isDirectSun, RaytracingAccelerationStructure rtas, float3 sunDir, float beamLux, float diffuseLux)
{
    float throughput = 1.0;
    float3 currentOrigin = origin + surfaceNormal * RAY_BIAS;
    float3 currentDir = direction;

    [loop]
    for (uint bounce = 0; bounce < MAX_BOUNCES; bounce++)
    {
        RayDesc ray;
        ray.Origin = currentOrigin;
        ray.Direction = currentDir;
        ray.TMin = 0.001;
        ray.TMax = 10000.0;

        Payload payload;
        payload.isOccluded = 1; // Assume hit
        payload.reflectance = 0.0;
        payload.transmittance = 0.0;
        payload.hitDistance = 0.0;
        payload.worldNormal = float3(0, 1, 0);

        uint rayFlags = RAY_FLAG_FORCE_OPAQUE;
        if (isDirectSun) rayFlags |= RAY_FLAG_CULL_BACK_FACING_TRIANGLES;
        
        TraceRay(rtas, rayFlags, 0xFF, 0, 1, 0, ray, payload);

        if (payload.isOccluded == 0)
        {
            return EvaluateSky(currentDir, sunDir, beamLux, diffuseLux, isDirectSun) * throughput;
        }

        if (isDirectSun)
        {
            float trans = saturate(payload.transmittance);
            if (trans <= 0.0) return 0.0; // Blocked by opaque geometry
            
            throughput *= trans;
            // Use a larger bias to guarantee escaping the pane geometry, especially at grazing angles
            currentOrigin += currentDir * (payload.hitDistance + RAY_BIAS * 5.0);
            continue;
        }

        // Probabilistic event selection (Russian Roulette)
        float rv = Random(seed);
        float refl = saturate(payload.reflectance);
        float trans = saturate(payload.transmittance);
        
        if (rv < refl)
        {
            float3 hitPoint = currentOrigin + currentDir * payload.hitDistance;
            currentDir = GetCosineWeightedDirection(payload.worldNormal, seed, PI);
            currentOrigin = hitPoint + payload.worldNormal * RAY_BIAS;
        }
        else if (rv < refl + trans)
        {
            currentOrigin += currentDir * (payload.hitDistance + RAY_BIAS);
        }
        else
        {
            return 0.0;
        }

        if (throughput <= MIN_VISIBILITY) break;
    }
    return 0.0;
}

#endif
