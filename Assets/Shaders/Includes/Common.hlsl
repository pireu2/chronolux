#ifndef CHRONOLUX_COMMON_INCLUDED
#define CHRONOLUX_COMMON_INCLUDED

// ── SHARED STRUCTURES ────────────────────────────────────────────────────────
struct Payload
{
    uint isOccluded; // 1 for hit, 0 for miss (safer than bool for DXR payloads)
    float reflectance;
    float transmittance;
    float hitDistance;
    float3 worldNormal;
};

struct MeshMetadata
{
    uint vertexOffset;
    uint indexOffset;
    uint hasGeometry;
    uint padding;
};

// ── GLOBAL CONSTANTS ─────────────────────────────────────────────────────────
static const float PI = 3.14159265359;
static const float RAY_BIAS = 0.002;
static const float MIN_VISIBILITY = 1e-4;
static const uint MAX_SUBMESHES = 8;

#endif
