#ifndef CHRONOLUX_GEOMETRY_INCLUDED
#define CHRONOLUX_GEOMETRY_INCLUDED

#include "Common.hlsl"

// ── INPUT BUFFERS (MUST BE DECLARED IN MAIN SHADER) ──────────────────────────
// StructuredBuffer<float4> _GlobalVertices;
// StructuredBuffer<uint> _GlobalIndices;
// StructuredBuffer<MeshMetadata> _MeshMetadata;
// StructuredBuffer<float4> _SimulationMaterials;
// uint _MaterialCount;

// ── LOGIC ────────────────────────────────────────────────────────────────────
float3 ReconstructGeometricNormal(
    uint instanceId, 
    uint primId, 
    uint geomId,
    StructuredBuffer<float4> globalVertices,
    StructuredBuffer<uint> globalIndices,
    StructuredBuffer<MeshMetadata> meshMetadata)
{
    float3 geomNormal = float3(0, 1, 0);

    if (geomId < MAX_SUBMESHES)
    {
        uint metaIndex = instanceId * MAX_SUBMESHES + geomId;
        MeshMetadata meta = meshMetadata[metaIndex];

        if (meta.hasGeometry > 0)
        {
            uint i0 = globalIndices[meta.indexOffset + primId * 3 + 0];
            uint i1 = globalIndices[meta.indexOffset + primId * 3 + 1];
            uint i2 = globalIndices[meta.indexOffset + primId * 3 + 2];

            float3 v0 = globalVertices[meta.vertexOffset + i0].xyz;
            float3 v1 = globalVertices[meta.vertexOffset + i1].xyz;
            float3 v2 = globalVertices[meta.vertexOffset + i2].xyz;

            float3x4 o2w = ObjectToWorld3x4();
            float3 wv0 = mul(o2w, float4(v0, 1.0));
            float3 wv1 = mul(o2w, float4(v1, 1.0));
            float3 wv2 = mul(o2w, float4(v2, 1.0));

            float3 edge1 = wv1 - wv0;
            float3 edge2 = wv2 - wv0;
            float3 rawNormal = cross(edge1, edge2);
            
            if (length(rawNormal) > 1e-6)
            {
                geomNormal = normalize(rawNormal);
            }

            // Flip normal if facing away from incoming ray (double-sided)
            if (dot(geomNormal, WorldRayDirection()) > 0.0)
            {
                geomNormal = -geomNormal;
            }
        }
    }
    return geomNormal;
}

#endif
