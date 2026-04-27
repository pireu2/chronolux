#ifndef CHRONOLUX_RANDOM_INCLUDED
#define CHRONOLUX_RANDOM_INCLUDED

// ── RANDOM NUMBER GENERATION ────────────────────────────────────────────────
uint Hash(uint x)
{
    x = ((x >> 16) ^ x) * 0x45d9f3b;
    x = ((x >> 16) ^ x) * 0x45d9f3b;
    x = (x >> 16) ^ x;
    return x;
}

float Random(inout uint state)
{
    state = Hash(state);
    return (float)state / 4294967296.0;
}

// ── SAMPLING ────────────────────────────────────────────────────────────────
float3 GetCosineWeightedDirection(float3 normal, inout uint seed, float PI)
{
    float u1 = Random(seed);
    float u2 = Random(seed);

    float r = sqrt(u1);
    float theta = 2.0 * PI * u2;

    float x = r * cos(theta);
    float y = r * sin(theta);
    float z = sqrt(max(0.0, 1.0 - u1));

    // Create tangent space
    float3 up = abs(normal.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 tangent = normalize(cross(up, normal));
    float3 bitangent = cross(normal, tangent);

    return tangent * x + bitangent * y + normal * z;
}

#endif
