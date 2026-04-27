using UnityEngine;
using System.Collections.Generic;

[AddComponentMenu("ChronoLux/Virtual Lux Sensor")]
public class VirtualLuxSensor : MonoBehaviour
{
    // Static registration to avoid FindObjectsByType allocations
    public static readonly List<VirtualLuxSensor> AllSensors = new List<VirtualLuxSensor>();

    [Header("Validation Data")]
    [ReadOnly, SerializeField] private float simulatedLux;
    [ReadOnly, SerializeField] private float theoreticalLux;
    [ReadOnly, SerializeField, Range(-100, 100)] private float errorPercent;

    [Header("Visuals")]
    public bool showGizmo = true;
    public Color gizmoColor = Color.cyan;

    private void OnEnable() => AllSensors.Add(this);
    private void OnDisable() => AllSensors.Remove(this);

    public void UpdateReadings(float lux, Vector3 sunDir, float beamLux, float diffuseLux)
    {
        simulatedLux = lux;
        float nDotL = Mathf.Max(0, Vector3.Dot(transform.up, sunDir));
        float direct = beamLux * nDotL;
        theoreticalLux = direct + (diffuseLux * 0.5f); 

        if (theoreticalLux > 1e-3f)
            errorPercent = ((simulatedLux - theoreticalLux) / theoreticalLux) * 100f;
        else
            errorPercent = 0f;
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
