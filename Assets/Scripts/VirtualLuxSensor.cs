using UnityEngine;

[AddComponentMenu("ChronoLux/Virtual Lux Sensor")]
public class VirtualLuxSensor : MonoBehaviour
{
    [Header("Validation Data")]
    [ReadOnly, SerializeField] private float simulatedLux;
    [ReadOnly, SerializeField] private float theoreticalLux;
    [ReadOnly, SerializeField, Range(-100, 100)] private float errorPercent;

    [Header("Visuals")]
    public bool showGizmo = true;
    public Color gizmoColor = Color.cyan;

    /// <summary>
    /// Updates the simulated Lux value received from the GPU.
    /// Also computes the theoretical value for comparison.
    /// </summary>
    public void UpdateReadings(float lux, Vector3 sunDir, float beamLux, float diffuseLux)
    {
        simulatedLux = lux;

        // --- THEORETICAL VALIDATION ---
        // Formula: E_total = Beam * cos(theta) + Diffuse_Factor
        // We assume a simple hemisphere factor for clear-sky diffuse (0.5 * diffuseLux) 
        // for vertical/random orientations, but the exact comparison depends on the Perez integral.
        float nDotL = Mathf.Max(0, Vector3.Dot(transform.up, sunDir));
        float direct = beamLux * nDotL;
        
        // Simple theoretical approximation for validation
        theoreticalLux = direct + (diffuseLux * 0.5f); 

        if (theoreticalLux > 1e-3f)
        {
            errorPercent = ((simulatedLux - theoreticalLux) / theoreticalLux) * 100f;
        }
        else
        {
            errorPercent = 0f;
        }
    }

    public float SimulatedLux => simulatedLux;

    private void OnDrawGizmos()
    {
        if (!showGizmo) return;
        
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(transform.position, 0.05f);
        Gizmos.DrawRay(transform.position, transform.up * 0.2f);
        
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.1f, $"{simulatedLux:F0} Lux");
#endif
    }
}

// Simple attribute to make fields read-only in inspector
public class ReadOnlyAttribute : PropertyAttribute { }

#if UNITY_EDITOR
[UnityEditor.CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
public class ReadOnlyDrawer : UnityEditor.PropertyDrawer
{
    public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
    {
        GUI.enabled = false;
        UnityEditor.EditorGUI.PropertyField(position, property, label);
        GUI.enabled = true;
    }
}
#endif
