using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

[AddComponentMenu("ChronoLux/Irradiance Baker")]
public class IrradianceBaker : MonoBehaviour
{
    public RayTracingShader rayTracingShader;
    [Header("Fallback Simulation Material")]
    [Range(0f, 1f)] public float defaultReflectance = 0.8f;
    [Range(0f, 1f)] public float defaultTransmittance = 0.0f;

    private RenderTexture _doseMap;
    private RayTracingAccelerationStructure _rtas;
    private ComputeBuffer _simulationMaterialBuffer;
    private int _materialCount;
    private bool _isInitialized = false;

    public RenderTexture DoseMap => _doseMap;

    private void OnValidate()
    {
        ClampEnergy(ref defaultReflectance, ref defaultTransmittance);
    }

    private static void ClampEnergy(ref float reflectance, ref float transmittance)
    {
        reflectance = Mathf.Clamp01(reflectance);
        transmittance = Mathf.Clamp01(transmittance);

        float sum = reflectance + transmittance;
        if (sum <= 1.0f) return;

        float inv = 1.0f / sum;
        reflectance *= inv;
        transmittance *= inv;
    }

    private struct RendererSortEntry
    {
        public Renderer renderer;
        public string sortKey;
    }

    private static string BuildTransformSortKey(Transform t)
    {
        if (t == null) return string.Empty;

        var segments = new List<string>(8);
        while (t != null)
        {
            // Sibling index disambiguates same-name siblings for deterministic ordering.
            segments.Add($"{t.GetSiblingIndex():D6}:{t.name}");
            t = t.parent;
        }
        segments.Reverse();
        return string.Join("/", segments);
    }

    private static int GetRendererComponentSlot(Renderer renderer)
    {
        if (renderer == null) return 0;

        Renderer[] siblings = renderer.GetComponents<Renderer>();
        for (int i = 0; i < siblings.Length; i++)
        {
            if (ReferenceEquals(siblings[i], renderer)) return i;
        }

        return 0;
    }

    private static string BuildRendererSortKey(Renderer renderer)
    {
        if (renderer == null) return string.Empty;

        string scenePath = renderer.gameObject.scene.path ?? string.Empty;
        string hierarchyKey = BuildTransformSortKey(renderer.transform);
        int rendererSlot = GetRendererComponentSlot(renderer);
        string rendererType = renderer.GetType().FullName;
        return $"{scenePath}|{hierarchyKey}|{rendererType}|{rendererSlot:D4}";
    }

    private static int CompareRendererEntries(RendererSortEntry a, RendererSortEntry b)
    {
        if (ReferenceEquals(a.renderer, b.renderer)) return 0;
        if (a.renderer == null) return -1;
        if (b.renderer == null) return 1;

        int keyCompare = string.CompareOrdinal(a.sortKey, b.sortKey);
        if (keyCompare != 0) return keyCompare;

        // Final deterministic tie-breaker for pathological duplicates.
        return string.CompareOrdinal(a.renderer.name, b.renderer.name);
    }

    private static void ReadMaterialScalars(Material material, ref float reflectance, ref float transmittance)
    {
        if (material == null) return;

        if (material.HasProperty("_Reflectance")) reflectance = material.GetFloat("_Reflectance");
        if (material.HasProperty("_Transmittance")) transmittance = material.GetFloat("_Transmittance");
    }

    private void ResolveFromSharedMaterials(Renderer renderer, ref float reflectance, ref float transmittance)
    {
        Material[] sharedMaterials = renderer.sharedMaterials;
        if (sharedMaterials == null || sharedMaterials.Length == 0) return;

        if (sharedMaterials.Length > 1)
        {
            Debug.LogWarning($"[IrradianceBaker] Renderer '{renderer.name}' has {sharedMaterials.Length} shared materials. " +
                             "Using conservative max of _Reflectance/_Transmittance across all submesh materials.");
        }

        float maxReflectance = reflectance;
        float maxTransmittance = transmittance;

        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            float r = reflectance;
            float t = transmittance;
            ReadMaterialScalars(sharedMaterials[i], ref r, ref t);
            ClampEnergy(ref r, ref t);

            maxReflectance = Mathf.Max(maxReflectance, r);
            maxTransmittance = Mathf.Max(maxTransmittance, t);
        }

        reflectance = maxReflectance;
        transmittance = maxTransmittance;
        ClampEnergy(ref reflectance, ref transmittance);
    }

    private void ResolveSimulationMaterial(Renderer renderer, out float reflectance, out float transmittance)
    {
        reflectance = defaultReflectance;
        transmittance = defaultTransmittance;

        var simMaterial = renderer.GetComponent<SimulationMaterial>();
        if (simMaterial != null)
        {
            simMaterial.GetClampedScalars(out reflectance, out transmittance);
            return;
        }

        ResolveFromSharedMaterials(renderer, ref reflectance, ref transmittance);

        ClampEnergy(ref reflectance, ref transmittance);
    }

    public void Initialize(int width, int height)
    {
        if (!SystemInfo.supportsRayTracing || rayTracingShader == null)
        {
            Debug.LogError("[IrradianceBaker] Ray tracing not supported or shader missing.");
            return;
        }

        if (_rtas != null) _rtas.Release();
        _rtas = new RayTracingAccelerationStructure();

        if (_simulationMaterialBuffer != null)
        {
            _simulationMaterialBuffer.Release();
            _simulationMaterialBuffer = null;
        }
        _materialCount = 0;

        Renderer[] allRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        var sortedRenderers = new List<RendererSortEntry>(allRenderers.Length);
        foreach (Renderer renderer in allRenderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            sortedRenderers.Add(new RendererSortEntry
            {
                renderer = renderer,
                sortKey = BuildRendererSortKey(renderer)
            });
        }
        sortedRenderers.Sort(CompareRendererEntries);

        var materialRows = new List<Vector4>(sortedRenderers.Count);
        int addedInstances = 0;
        foreach (RendererSortEntry entry in sortedRenderers)
        {
            Renderer r = entry.renderer;

            // Determine submesh count to avoid null/default ambiguity
            int subMeshCount = 1;
            if (r is MeshRenderer mr)
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) subMeshCount = mf.sharedMesh.subMeshCount;
            }
            else if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                subMeshCount = smr.sharedMesh.subMeshCount;
            }

            var subMeshFlags = new RayTracingSubMeshFlags[subMeshCount];
            for (int i = 0; i < subMeshCount; i++) subMeshFlags[i] = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;

            ResolveSimulationMaterial(r, out float reflectance, out float transmittance);
            materialRows.Add(new Vector4(reflectance, transmittance, 0f, 0f));
            uint materialIndex = (uint)(materialRows.Count - 1);

            _rtas.AddInstance(r, subMeshFlags, false, false, 0xFF, materialIndex);
            addedInstances++;
        }

        _materialCount = materialRows.Count;
        if (_materialCount > 0)
        {
            _simulationMaterialBuffer = new ComputeBuffer(_materialCount, sizeof(float) * 4);
            _simulationMaterialBuffer.SetData(materialRows);
        }

        _rtas.Build();

        if (_doseMap != null) { _doseMap.Release(); Destroy(_doseMap); }
        _doseMap = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            name = "DoseMap"
        };
        _doseMap.Create();

        var cmd = new CommandBuffer { name = "Clear DoseMap" };
        cmd.SetRenderTarget(_doseMap);
        cmd.ClearRenderTarget(true, true, Color.clear);
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

        _isInitialized = true;
        Debug.Log($"[IrradianceBaker] Initialized with {addedInstances} instances.");
    }

    public void DispatchRays(Vector3 sunDirection, float beamLux, float deltaHours, RenderTexture positionMap, RenderTexture normalMap)
    {
        if (!_isInitialized || positionMap == null || normalMap == null) return;
        if (sunDirection.sqrMagnitude <= 1e-8f) return;

        sunDirection.Normalize();
        var cmd = new CommandBuffer { name = "Dispatch IrradianceBake" };

        cmd.SetRayTracingTextureParam(rayTracingShader, "_PositionMap", positionMap);
        cmd.SetRayTracingTextureParam(rayTracingShader, "_NormalMap", normalMap);
        cmd.SetRayTracingTextureParam(rayTracingShader, "_DoseMap", _doseMap);
        cmd.SetRayTracingAccelerationStructure(rayTracingShader, "_SceneRTAS", _rtas);
        if (_simulationMaterialBuffer != null)
        {
            cmd.SetRayTracingBufferParam(rayTracingShader, "_SimulationMaterials", _simulationMaterialBuffer);
        }

        cmd.SetRayTracingVectorParam(rayTracingShader, "_SunDirection", sunDirection);
        cmd.SetRayTracingFloatParam(rayTracingShader, "_BeamLux", beamLux);
        cmd.SetRayTracingFloatParam(rayTracingShader, "_DeltaHours", deltaHours);
        cmd.SetRayTracingIntParam(rayTracingShader, "_MaterialCount", _materialCount);

        Camera cam = Camera.main ?? GetComponent<Camera>();
        if (cam == null) { Debug.LogError("[IrradianceBaker] No Camera found."); cmd.Release(); return; }

        cmd.DispatchRays(rayTracingShader, "IrradianceBakeRayGen", (uint)_doseMap.width, (uint)_doseMap.height, 1, cam);
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();
    }

    private void OnDestroy()
    {
        if (_rtas != null) _rtas.Release();
        if (_simulationMaterialBuffer != null) _simulationMaterialBuffer.Release();
        if (_doseMap != null) { _doseMap.Release(); Destroy(_doseMap); }
    }
}
