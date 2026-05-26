using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChronoLux.Project
{
    [Serializable]
    public class ChronoProject
    {
        public string projectName;
        [Obsolete("Use artifactFileName instead. Kept for migration.")]
        public string modelFileName; 
        public string artifactFileName;
        public List<string> environmentFileNames = new List<string>();
        
        [Header("Location")]
        public double latitude = 44.4268;
        public double longitude = 26.1025;
        public double utcOffset = 3.0;

        [Header("Simulation")]
        public int year = 2025;
        public int startDay = 1;
        public int endDay = 365;
        public int samplesPerPixel = 8;
        
        // Artifact Transformation
        public Vector3 artifactPosition = Vector3.zero;
        public Vector3 artifactScale = Vector3.one;
        
        // Dictionary mapping mesh object names to material preset names
        // Note: For JSON serialization, we'll use two lists to represent the dictionary
        public List<string> objectNames = new List<string>();
        public List<string> materialPresetNames = new List<string>();

        public void SetMaterial(string objectName, string presetName)
        {
            int index = objectNames.IndexOf(objectName);
            if (index >= 0)
            {
                materialPresetNames[index] = presetName;
            }
            else
            {
                objectNames.Add(objectName);
                materialPresetNames.Add(presetName);
            }
        }
    }
}
