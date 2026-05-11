using UnityEngine;
using UnityEngine.InputSystem;
using ChronoLux.Library;
using System;

namespace ChronoLux.Interaction
{
    /// <summary>
    /// Optimized Object Picker: Fixed frame drops by removing redundant per-frame material updates.
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

        private void OnDestroy()
        {
            if (_highlightMat != null)
            {
                if (Application.isPlaying) Destroy(_highlightMat);
                else DestroyImmediate(_highlightMat);
            }
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
            Ray ray = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                GameObject hitObj = hit.collider.gameObject;

                // Artifact Guard
                if (hitObj.GetComponent<UVMapBaker>() != null || hitObj.GetComponentInParent<UVMapBaker>() != null)
                {
                    if (hoveredObject != null) { ClearHighlight(hoveredObject); hoveredObject = null; }
                    return;
                }

                if (hitObj != hoveredObject)
                {
                    // Only clear if it's NOT the selected object
                    if (hoveredObject != null && hoveredObject != selectedObject) 
                        ClearHighlight(hoveredObject);

                    hoveredObject = hitObj;

                    // Only apply if it's NOT already the selected object (which has its own color)
                    if (hoveredObject != selectedObject)
                        ApplyHighlight(hoveredObject, hoverColor);
                }
            }
            else
            {
                if (hoveredObject != null)
                {
                    if (hoveredObject != selectedObject) ClearHighlight(hoveredObject);
                    hoveredObject = null;
                }
            }
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

            // Set the color via PropertyBlock
            r.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(_ID_GlowColor, color);
            r.SetPropertyBlock(_propBlock);

            // Ensure the material pass exists
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

            // Preserve the highlight while changing the base material
            r.sharedMaterial = preset.visualMaterial;
            
            var sim = selectedObject.GetComponent<SimulationMaterial>() ?? selectedObject.AddComponent<SimulationMaterial>();
            sim.reflectance = preset.reflectance;
            sim.transmittance = preset.transmittance;

            // Re-apply the selection color to the PropertyBlock (which might have been reset)
            r.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(_ID_GlowColor, selectColor);
            r.SetPropertyBlock(_propBlock);
        }
    }
}
