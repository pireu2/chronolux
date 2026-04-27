#ifndef CHRONOLUX_SKYMODEL_INCLUDED
#define CHRONOLUX_SKYMODEL_INCLUDED

// REFERENCE — Perez All-Weather Sky Model:
//   Perez, R., Seals, R., Michalsky, J. (1993). "All-weather model for sky luminance distribution." 
//   Solar Energy, 50(3), pp. 235-245.

// Perez coefficients for clear sky (Turbidity T = 2.0)
static const float perez_a = -0.1058; // Horizon brightening/darkening
static const float perez_b = -0.0738; // Luminance gradient near horizon
static const float perez_c = 1.8200;  // Circumsolar intensity
static const float perez_d = -2.9744; // Circumsolar width
static const float perez_e = 0.1778;  // Backscattered light

float PerezFunction(float theta, float gamma)
{
    float cosTheta = max(0.01, cos(theta));
    float cosGamma = cos(gamma);
    
    float f = (1.0 + perez_a * exp(perez_b / cosTheta));
    float g = (1.0 + perez_c * exp(perez_d * gamma) + perez_e * cosGamma * cosGamma);
    
    return f * g;
}

float EvaluateSky(float3 direction, float3 sunDir, float beamLux, float diffuseLux, bool includeSun)
{
    // Sun disk check (approx 0.5 degrees)
    if (includeSun && dot(direction, sunDir) > 0.9998) 
    {
        return beamLux;
    }
    
    // Perez Luminance Distribution
    float theta = acos(saturate(dot(direction, float3(0, 1, 0))));
    float gamma = acos(saturate(dot(direction, sunDir)));
    
    float relLuminance = PerezFunction(theta, gamma);
    
    // Normalization factor: Lz ~ Ed * 0.2 for T=2.0
    return relLuminance * (diffuseLux * 0.2); 
}

#endif
