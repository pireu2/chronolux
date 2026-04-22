using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("ChronoLux/Simulation Material")]
public class SimulationMaterial : MonoBehaviour
{
    [Range(0f, 1f)] public float reflectance = 0.8f;
    [Range(0f, 1f)] public float transmittance = 0.0f;

    public void GetClampedScalars(out float reflectanceOut, out float transmittanceOut)
    {
        reflectanceOut = Mathf.Clamp01(reflectance);
        transmittanceOut = Mathf.Clamp01(transmittance);

        float sum = reflectanceOut + transmittanceOut;
        if (sum <= 1.0f) return;

        float inv = 1.0f / sum;
        reflectanceOut *= inv;
        transmittanceOut *= inv;
    }

    private void OnValidate()
    {
        GetClampedScalars(out reflectance, out transmittance);
    }
}
