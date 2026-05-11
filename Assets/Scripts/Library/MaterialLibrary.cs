using UnityEngine;
using System.Collections.Generic;

namespace ChronoLux.Library
{
    [CreateAssetMenu(fileName = "Global Material Library", menuName = "ChronoLux/Material Library")]
    public class MaterialLibrary : ScriptableObject
    {
        public List<MaterialPreset> presets = new List<MaterialPreset>();
    }
}
