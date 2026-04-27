using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[AddComponentMenu("ChronoLux/Irradiance Baker")]
public class IrradianceBaker : MonoBehaviour
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MeshMetadata
    {
        public uint vertexOffset;
        public uint indexOffset;
        public uint hasGeometry; 
        public uint padding;     
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SensorInput
    {
        public Vector4 position; 
        public Vector4 normal;   
    }

    private const int MAX_SUBMESHES = 8;

    [Header("Assets")]
    public RayTracingShader rayTracingShader;
    public RayTracingShader sensorShader;

    [Header("Fallback Simulation Material")]
    [Range(0f, 1f)] public float defaultReflectance = 0.8f;
    [Range(0f, 1f)] public float defaultTransmittance = 0.0f;

    private RenderTexture _doseMap;
    private RayTracingAccelerationStructure _rtas;
    private ComputeBuffer _simulationMaterialBuffer;
    private ComputeBuffer _vertexBuffer;
    private ComputeBuffer _indexBuffer;
    private ComputeBuffer _metadataBuffer;

    private ComputeBuffer _sensorInputBuffer;
    private ComputeBuffer _sensorOutputBuffer;
    private SensorInput[] _cachedSensorInputs;
    private float[] _cachedZeros;

    private int _materialCount;
    private bool _isInitialized = false;

    private static readonly int _ID_PositionMap = Shader.PropertyToID("_PositionMap");
    private static readonly int _ID_NormalMap = Shader.PropertyToID("_NormalMap");
    private static readonly int _ID_DoseMap = Shader.PropertyToID("_DoseMap");
    private static readonly int _ID_SceneRTAS = Shader.PropertyToID("_SceneRTAS");
    private static readonly int _ID_SunDirection = Shader.PropertyToID("_SunDirection");
    private static readonly int _ID_BeamLux = Shader.PropertyToID("_BeamLux");
    private static readonly int _ID_DiffuseLux = Shader.PropertyToID("_DiffuseLux");
    private static readonly int _ID_DeltaHours = Shader.PropertyToID("_DeltaHours");
    private static readonly int _ID_MaterialCount = Shader.PropertyToID("_MaterialCount");
    private static readonly int _ID_FrameIndex = Shader.PropertyToID("_FrameIndex");
    private static readonly int _ID_SimulationMaterials = Shader.PropertyToID("_SimulationMaterials");
    private static readonly int _ID_MeshMetadata = Shader.PropertyToID("_MeshMetadata");
    private static readonly int _ID_GlobalVertices = Shader.PropertyToID("_GlobalVertices");
    private static readonly int _ID_GlobalIndices = Shader.PropertyToID("_GlobalIndices");
    private static readonly int _ID_SensorInputs = Shader.PropertyToID("_SensorInputs");
    private static readonly int _ID_SensorOutputs = Shader.PropertyToID("_SensorOutputs");

    public RenderTexture DoseMap => _doseMap;

    private void OnValidate() => ClampEnergy(ref defaultReflectance, ref defaultTransmittance);
    private static void ClampEnergy(ref float r, ref float t) { r = Mathf.Clamp01(r); t = Mathf.Clamp01(t); float s = r + t; if (s > 1f) { r /= s; t /= s; } }

    private struct RendererSortEntry { public Renderer renderer; public string sortKey; }
    private static string BuildTransformSortKey(Transform t) {
        if (t == null) return string.Empty;
        var s = new List<string>(8);
        while (t != null) { s.Add($"{t.GetSiblingIndex():D6}:{t.name}"); t = t.parent; }
        s.Reverse(); return string.Join("/", s);
    }
    private static string BuildRendererSortKey(Renderer r) {
        if (r == null) return string.Empty;
        string p = r.gameObject.scene.path ?? string.Empty;
        string h = BuildTransformSortKey(r.transform);
        int s = 0; Renderer[] sib = r.GetComponents<Renderer>();
        for (int i = 0; i < sib.Length; i++) if (ReferenceEquals(sib[i], r)) { s = i; break; }
        return $"{p}|{h}|{r.GetType().FullName}|{s:D4}";
    }

    private void ResolveSimulationMaterial(Renderer renderer, out float reflectance, out float transmittance) {
        reflectance = defaultReflectance; transmittance = defaultTransmittance;
        var sim = renderer.GetComponent<SimulationMaterial>();
        if (sim != null) { sim.GetClampedScalars(out reflectance, out transmittance); return; }
        Material m = renderer.sharedMaterial;
        if (m != null) {
            if (m.HasProperty("_Reflectance")) reflectance = m.GetFloat("_Reflectance");
            if (m.HasProperty("_Transmittance")) transmittance = m.GetFloat("_Transmittance");
        }
        ClampEnergy(ref reflectance, ref transmittance);
    }

    public void Initialize(int width, int height)
    {
        if (!SystemInfo.supportsRayTracing || rayTracingShader == null) return;
        CleanUp();
        _rtas = new RayTracingAccelerationStructure();

        Renderer[] all = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        var sorted = new List<RendererSortEntry>(all.Length);
        foreach (var r in all) if (r != null && r.enabled && r.gameObject.activeInHierarchy) sorted.Add(new RendererSortEntry { renderer = r, sortKey = BuildRendererSortKey(r) });
        sorted.Sort((a, b) => string.CompareOrdinal(a.sortKey, b.sortKey));

        var mats = new List<Vector4>(sorted.Count);
        var verts = new List<Vector4>(); 
        var idxs = new List<uint>();
        var metas = new MeshMetadata[Mathf.Max(1, sorted.Count) * MAX_SUBMESHES];

        for (int i = 0; i < sorted.Count; i++) {
            Renderer r = sorted[i].renderer; Mesh mesh = null; int subCount = 1;
            if (r is MeshRenderer mr) { var mf = mr.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
            else if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            if (mesh != null) subCount = mesh.subMeshCount;
            if (mesh != null && mesh.isReadable) {
                uint vOff = (uint)verts.Count;
                foreach(var v in mesh.vertices) verts.Add(new Vector4(v.x, v.y, v.z, 1.0f));
                for (int s = 0; s < Mathf.Min(subCount, MAX_SUBMESHES); s++) {
                    uint iOff = (uint)idxs.Count;
                    foreach (int idx in mesh.GetIndices(s)) idxs.Add((uint)idx);
                    metas[i * MAX_SUBMESHES + s] = new MeshMetadata { vertexOffset = vOff, indexOffset = iOff, hasGeometry = 1 };
                }
            }
            ResolveSimulationMaterial(r, out float refl, out float trans);
            mats.Add(new Vector4(refl, trans, 0f, 0f));
            var flags = new RayTracingSubMeshFlags[subCount];
            for (int s = 0; s < subCount; s++) flags[s] = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
            _rtas.AddInstance(r, flags, false, false, 0xFF, (uint)mats.Count - 1);
        }

        _materialCount = mats.Count;
        _simulationMaterialBuffer = new ComputeBuffer(Mathf.Max(1, _materialCount), 16);
        _simulationMaterialBuffer.SetData(mats.Count > 0 ? mats.ToArray() : new Vector4[] { Vector4.zero });
        _metadataBuffer = new ComputeBuffer(metas.Length, 16);
        _metadataBuffer.SetData(metas);
        _vertexBuffer = new ComputeBuffer(Mathf.Max(1, verts.Count), 16);
        _vertexBuffer.SetData(verts.Count > 0 ? verts.ToArray() : new Vector4[] { Vector4.zero });
        _indexBuffer = new ComputeBuffer(Mathf.Max(1, idxs.Count), 4);
        _indexBuffer.SetData(idxs.Count > 0 ? idxs.ToArray() : new uint[] { 0 });
        _rtas.Build();

        _doseMap = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat) { enableRandomWrite = true, filterMode = FilterMode.Bilinear, name = "DoseMap" };
        _doseMap.Create();

        var cmd = new CommandBuffer { name = "Clear DoseMap" };
        cmd.SetRenderTarget(_doseMap); cmd.ClearRenderTarget(true, true, Color.clear);
        Graphics.ExecuteCommandBuffer(cmd); cmd.Release();

        _isInitialized = true;
    }

    private uint _frameIndex = 0;

    public void DispatchRays(Vector3 sunDirection, float beamLux, float diffuseLux, float deltaHours, RenderTexture positionMap, RenderTexture normalMap)
    {
        if (!_isInitialized || positionMap == null || normalMap == null) return;
        if (sunDirection.sqrMagnitude <= 1e-8f) return;

        Camera cam = Camera.main ?? GetComponent<Camera>();
        if (cam == null) return; // Early exit before CommandBuffer allocation

        _frameIndex++;
        sunDirection.Normalize();

        // 1. DYNAMIC SENSOR UPDATE
        var sensors = VirtualLuxSensor.AllSensors;
        int sensorCount = sensors.Count;

        if (sensorCount > 0) {
            if (_sensorInputBuffer == null || _sensorInputBuffer.count != sensorCount) {
                if (_sensorInputBuffer != null) _sensorInputBuffer.Release();
                if (_sensorOutputBuffer != null) _sensorOutputBuffer.Release();
                _sensorInputBuffer = new ComputeBuffer(sensorCount, Marshal.SizeOf<SensorInput>());
                _sensorOutputBuffer = new ComputeBuffer(sensorCount, sizeof(float));
                _cachedSensorInputs = new SensorInput[sensorCount];
                _cachedZeros = new float[sensorCount];
            }
            
            for (int i = 0; i < sensorCount; i++) {
                _cachedSensorInputs[i] = new SensorInput { 
                    position = (Vector4)sensors[i].transform.position + new Vector4(0,0,0,1),
                    normal = (Vector4)sensors[i].transform.up
                };
            }
            _sensorInputBuffer.SetData(_cachedSensorInputs);
            _sensorOutputBuffer.SetData(_cachedZeros);
        }

        var cmd = new CommandBuffer { name = "Dispatch ChronoLux" };
        void SetCommon(RayTracingShader s) {
            cmd.SetRayTracingAccelerationStructure(s, _ID_SceneRTAS, _rtas);
            cmd.SetRayTracingBufferParam(s, _ID_SimulationMaterials, _simulationMaterialBuffer);
            cmd.SetRayTracingBufferParam(s, _ID_MeshMetadata, _metadataBuffer);
            cmd.SetRayTracingBufferParam(s, _ID_GlobalVertices, _vertexBuffer);
            cmd.SetRayTracingBufferParam(s, _ID_GlobalIndices, _indexBuffer);
            cmd.SetRayTracingVectorParam(s, _ID_SunDirection, sunDirection);
            cmd.SetRayTracingFloatParam(s, _ID_BeamLux, beamLux);
            cmd.SetRayTracingFloatParam(s, _ID_DiffuseLux, diffuseLux);
            cmd.SetRayTracingIntParam(s, _ID_MaterialCount, _materialCount);
            cmd.SetRayTracingIntParam(s, _ID_FrameIndex, (int)_frameIndex);
        }

        SetCommon(rayTracingShader);
        cmd.SetRayTracingTextureParam(rayTracingShader, _ID_PositionMap, positionMap);
        cmd.SetRayTracingTextureParam(rayTracingShader, _ID_NormalMap, normalMap);
        cmd.SetRayTracingTextureParam(rayTracingShader, _ID_DoseMap, _doseMap);
        cmd.SetRayTracingFloatParam(rayTracingShader, _ID_DeltaHours, deltaHours);
        cmd.DispatchRays(rayTracingShader, "IrradianceBakeRayGen", (uint)_doseMap.width, (uint)_doseMap.height, 1, cam);

        if (sensorShader != null && sensorCount > 0) {
            SetCommon(sensorShader);
            cmd.SetRayTracingBufferParam(sensorShader, _ID_SensorInputs, _sensorInputBuffer);
            cmd.SetRayTracingBufferParam(sensorShader, _ID_SensorOutputs, _sensorOutputBuffer);
            cmd.DispatchRays(sensorShader, "SensorBakeRayGen", (uint)sensorCount, 1, 1, cam);
        }

        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

        // 2. STABLE READBACK (Capture snapshot to avoid closure race conditions)
        if (sensorCount > 0) {
            var snapshot = sensors.ToArray();
            AsyncGPUReadback.Request(_sensorOutputBuffer, (req) => {
                if (req.hasError) return;
                var data = req.GetData<float>();
                for (int i = 0; i < snapshot.Length; i++) {
                    if (i < data.Length && snapshot[i] != null) 
                        snapshot[i].UpdateReadings(data[i], sunDirection, beamLux, diffuseLux);
                }
            });
        }
    }

    private void CleanUp() {
        if (_rtas != null) { _rtas.Release(); _rtas = null; }
        void Rel(ref ComputeBuffer b) { if (b != null) { b.Release(); b = null; } }
        Rel(ref _simulationMaterialBuffer); Rel(ref _vertexBuffer); Rel(ref _indexBuffer); Rel(ref _metadataBuffer); Rel(ref _sensorInputBuffer); Rel(ref _sensorOutputBuffer);
        if (_doseMap != null) { _doseMap.Release(); if (Application.isPlaying) Destroy(_doseMap); else DestroyImmediate(_doseMap); _doseMap = null; }
    }
    private void OnDestroy() => CleanUp();
}
