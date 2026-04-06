using UnityEngine;
using UnityEngine.Rendering;

[AddComponentMenu("ChronoLux/Irradiance Baker")]
public class IrradianceBaker : MonoBehaviour
{
    public RayTracingShader rayTracingShader;
    private RenderTexture _doseMap;
    private RayTracingAccelerationStructure _rtas;
    private bool _isInitialized = false;

    public RenderTexture DoseMap => _doseMap;

    public void Initialize(int width, int height)
    {
        if (!SystemInfo.supportsRayTracing || rayTracingShader == null)
        {
            Debug.LogError("[IrradianceBaker] Ray tracing not supported or shader missing.");
            return;
        }

        if (_rtas != null) _rtas.Release();
        _rtas = new RayTracingAccelerationStructure();

        Renderer[] allRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        int addedInstances = 0;
        foreach (var r in allRenderers)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;

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

            _rtas.AddInstance(r, subMeshFlags, false, false, 0xFF, 0);
            addedInstances++;
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

        cmd.SetRayTracingVectorParam(rayTracingShader, "_SunDirection", sunDirection);
        cmd.SetRayTracingFloatParam(rayTracingShader, "_BeamLux", beamLux);
        cmd.SetRayTracingFloatParam(rayTracingShader, "_DeltaHours", deltaHours);

        Camera cam = Camera.main ?? GetComponent<Camera>();
        if (cam == null) { Debug.LogError("[IrradianceBaker] No Camera found."); cmd.Release(); return; }

        cmd.DispatchRays(rayTracingShader, "IrradianceBakeRayGen", (uint)_doseMap.width, (uint)_doseMap.height, 1, cam);
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();
    }

    private void OnDestroy()
    {
        if (_rtas != null) _rtas.Release();
        if (_doseMap != null) { _doseMap.Release(); Destroy(_doseMap); }
    }
}
