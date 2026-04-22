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

    private static string GetTransformPath(Transform t)
    {
        if (t == null) return string.Empty;

        var names = new List<string>(8);
        while (t != null)
        {
            names.Add(t.name);
            t = t.parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }

    private static int CompareRenderersDeterministically(Renderer a, Renderer b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        int sceneCompare = string.CompareOrdinal(a.gameObject.scene.path, b.gameObject.scene.path);
        if (sceneCompare != 0) return sceneCompare;

        return string.CompareOrdinal(GetTransformPath(a.transform), GetTransformPath(b.transform));
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

        Material sharedMaterial = renderer.sharedMaterial;
        if (sharedMaterial != null)
        {
            if (sharedMaterial.HasProperty("_Reflectance")) reflectance = sharedMaterial.GetFloat("_Reflectance");
            if (sharedMaterial.HasProperty("_Transmittance")) transmittance = sharedMaterial.GetFloat("_Transmittance");
        }

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
        var sortedRenderers = new List<Renderer>(allRenderers.Length);
        foreach (Renderer renderer in allRenderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            sortedRenderers.Add(renderer);
        }
        sortedRenderers.Sort(CompareRenderersDeterministically);

        var materialRows = new List<Vector4>(sortedRenderers.Count);
        int addedInstances = 0;
        foreach (var r in sortedRenderers)
        {
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
        cmd.SetRayTracingFloatParam(rayTracingShader, "_MaterialCount", _materialCount);

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
