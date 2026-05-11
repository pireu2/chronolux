using UnityEngine;
using UnityEngine.InputSystem;
using ChronoLux.Library;
using System;

namespace ChronoLux.Interaction
{
    /// <summary>
    /// Reverted to the confirmed working version. 
    /// Handles selection via the center of the screen (Crosshair).
    /// </summary>
    public class ObjectPicker : MonoBehaviour
    {
        [Header("Assets")]
        public MaterialLibrary library;
        public Shader selectionShader;

        [Header("Colors")]
        public Color hoverColor = new Color(0.1f, 0.4f, 1.0f, 1.0f);
        public Color selectColor = new Color(0.6f, 0.1f, 1.0f, 1.0f);

        public event Action<GameObject> OnObjectSelected;
        public event Action OnSelectionCleared;

        [Header("Status (Read Only)")]
        public GameObject hoveredObject;
        public GameObject selectedObject;
        
        private Camera _cam;
        private MaterialPropertyBlock _propBlock;
        private Material _highlightMat;
        private static readonly int _ID_GlowColor = Shader.PropertyToID("_GlowColor");

        private void Start()
        {
            _cam = GetComponent<Camera>();
            _propBlock = new MaterialPropertyBlock();
            if (selectionShader != null) _highlightMat = new Material(selectionShader);
        }

        private void Update()
        {
            HandleHover();

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                HandleSelection();
            }
        }

        private void HandleHover()
        {
            // Raycast from the center of the screen (Crosshair)
            Ray ray = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                GameObject hitObj = hit.collider.gameObject;
                if (hitObj != hoveredObject)
                {
                    ClearHighlight(hoveredObject);
                    hoveredObject = hitObj;
                    ApplyHighlight(hoveredObject, hoverColor);
                }
            }
            else
            {
                if (hoveredObject != null)
                {
                    ClearHighlight(hoveredObject);
                    hoveredObject = null;
                }
            }
            
            if (selectedObject != null) ApplyHighlight(selectedObject, selectColor);
        }

        private void HandleSelection()
        {
            if (hoveredObject != null)
            {
                if (selectedObject != null) ClearHighlight(selectedObject);
                selectedObject = hoveredObject;
                ApplyHighlight(selectedObject, selectColor);
                OnObjectSelected?.Invoke(selectedObject);
            }
            else
            {
                if (selectedObject != null) ClearHighlight(selectedObject);
                selectedObject = null;
                OnSelectionCleared?.Invoke();
            }
        }

        private void ApplyHighlight(GameObject obj, Color color)
        {
            if (obj == null || _highlightMat == null) return;
            Renderer r = obj.GetComponentInChildren<Renderer>();
            if (r == null) return;

            r.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(_ID_GlowColor, color);
            r.SetPropertyBlock(_propBlock);

            Material[] mats = r.sharedMaterials;
            bool exists = false;
            foreach (var m in mats) if (m != null && m.shader == selectionShader) exists = true;

            if (!exists)
            {
                Material[] newMats = new Material[mats.Length + 1];
                for (int i = 0; i < mats.Length; i++) newMats[i] = mats[i];
                newMats[newMats.Length - 1] = _highlightMat;
                r.sharedMaterials = newMats;
            }
        }

        private void ClearHighlight(GameObject obj)
        {
            if (obj == null) return;
            Renderer r = obj.GetComponentInChildren<Renderer>();
            if (r == null) return;

            Material[] mats = r.sharedMaterials;
            int count = 0;
            foreach (var m in mats) if (m == null || m.shader != selectionShader) count++;

            if (count < mats.Length)
            {
                Material[] newMats = new Material[count];
                int j = 0;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].shader != selectionShader)
                        newMats[j++] = mats[i];
                }
                r.sharedMaterials = newMats;
            }
        }

        public void AssignMaterialToSelected(MaterialPreset preset)
        {
            if (selectedObject == null || preset == null) return;
            Renderer r = selectedObject.GetComponentInChildren<Renderer>();
            if (r == null) return;

            ClearHighlight(selectedObject);
            r.sharedMaterial = preset.visualMaterial;
            var sim = selectedObject.GetComponent<SimulationMaterial>() ?? selectedObject.AddComponent<SimulationMaterial>();
            sim.reflectance = preset.reflectance;
            sim.transmittance = preset.transmittance;
            ApplyHighlight(selectedObject, selectColor);
        }
    }
}
