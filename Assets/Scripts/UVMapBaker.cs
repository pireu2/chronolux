using UnityEngine;
using UnityEngine.Rendering;

// Renders the mesh in UV space so the GPU rasteriser writes world-space
// position and normal into two MRT textures (PositionMap, NormalMap).
public class UVMapBaker : MonoBehaviour
{
    public Shader bakeShader;  // assign Hidden/UVSpaceBaker
    public int resolution = 2048;

    public RenderTexture PositionMap { get; private set; }
    public RenderTexture NormalMap { get; private set; }

    [ContextMenu("Bake UV Maps")]
    public void Bake()
    {
        if (bakeShader == null)
        {
            Debug.LogError("[UVMapBaker] bakeShader is null. Assign Hidden/UVSpaceBaker in the Inspector.", this);
            return;
        }

        var allMF = GetComponentsInChildren<MeshFilter>(true);
        if (allMF.Length == 0) { Debug.LogError("[UVMapBaker] No MeshFilter found.", this); return; }

        foreach (var mf in allMF)
        {
            if (mf.sharedMesh == null) continue;
            if (!mf.sharedMesh.isReadable)
            {
                Debug.LogError($"[UVMapBaker] '{mf.sharedMesh.name}' — enable Read/Write in Import Settings.", this);
                return;
            }
            if (mf.sharedMesh.uv == null || mf.sharedMesh.uv.Length == 0)
            {
                Debug.LogError($"[UVMapBaker] '{mf.sharedMesh.name}' has no UV channel 0. Unwrap it first.", this);
                return;
            }
        }

        if (PositionMap != null) PositionMap.Release();
        if (NormalMap != null) NormalMap.Release();
        PositionMap = MakeRT();
        NormalMap = MakeRT();

        // MRT needs a depth surface even though ZWrite is off.
        var depthRT = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.Depth)
        { hideFlags = HideFlags.HideAndDontSave };
        depthRT.Create();

        var mat = new Material(bakeShader) { hideFlags = HideFlags.HideAndDontSave };
        var cmd = new CommandBuffer { name = "UVBake" };

        var targets = new RenderTargetIdentifier[]
        {
            new RenderTargetIdentifier(PositionMap),
            new RenderTargetIdentifier(NormalMap),
        };
        cmd.SetRenderTarget(targets, depthRT);
        cmd.ClearRenderTarget(true, true, Color.clear);

        int totalTris = 0;
        foreach (var mf in allMF)
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) continue;

            Matrix4x4 o2w = mf.transform.localToWorldMatrix;
            Matrix4x4 o2wIT = o2w.inverse.transpose; // correct normal transform under non-uniform scale

            var mpb = new MaterialPropertyBlock();
            mpb.SetMatrix("_O2W", o2w);
            mpb.SetMatrix("_O2WIT", o2wIT);

            // Identity matrix: world transform is applied in the shader via _O2W.
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                cmd.DrawMesh(mesh, Matrix4x4.identity, mat, sub, 0, mpb);

            totalTris += (int)(mesh.triangles.Length / 3);
        }

        Graphics.ExecuteCommandBuffer(cmd);

        cmd.Release();
        DestroyImmediate(mat);
        depthRT.Release();
        DestroyImmediate(depthRT);

        Debug.Log($"[UVMapBaker] Done — {totalTris} triangles baked at {resolution}x{resolution}.");
    }

    RenderTexture MakeRT()
    {
        var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
        };
        rt.Create();
        return rt;
    }

    [ContextMenu("Save to EXR")]
    public void SaveToEXR()
    {
        SaveRT(PositionMap, "PositionMap");
        SaveRT(NormalMap, "NormalMap");
    }

    void SaveRT(RenderTexture rt, string label)
    {
        if (rt == null) { Debug.LogWarning($"[UVMapBaker] {label} is null — bake first."); return; }

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        byte[] bytes = tex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
        DestroyImmediate(tex);

        string path = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Application.dataPath),
            $"{gameObject.name}_{label}.exr");
        System.IO.File.WriteAllBytes(path, bytes);
        Debug.Log($"[UVMapBaker] Saved {label} → {path}");
    }
}
