using UnityEngine;

namespace ChronoLux.Library
{
    [CreateAssetMenu(fileName = "New Material Preset", menuName = "ChronoLux/Material Preset")]
    public class MaterialPreset : ScriptableObject
    {
        public string materialName = "New Material";
        
        [Header("Physical Properties")]
        [Range(0f, 1f)] public float reflectance = 0.2f;
        [Range(0f, 1f)] public float transmittance = 0f;

        [Header("Visuals")]
        public Material visualMaterial;

        private void OnValidate()
        {
            // Simple energy conservation check
            float sum = reflectance + transmittance;
            if (sum > 1.0f)
            {
                float inv = 1.0f / sum;
                reflectance *= inv;
                transmittance *= inv;
            }
        }
    }
}
