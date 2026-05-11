using UnityEngine;
using UnityEngine.Rendering;

public class UVMapBaker : MonoBehaviour
{
    public Shader bakeShader;
    public int resolution = 2048;
    public RenderTexture PositionMap { get; private set; }
    public RenderTexture NormalMap { get; private set; }

    public int SurfacePixelCount { get; private set; }

    [ContextMenu("Bake UV Maps")]
    public void Bake() => Bake(resolution);

    public void Bake(int targetResolution)
    {
        if (bakeShader == null) return;
        var allMF = GetComponentsInChildren<MeshFilter>(true);
        if (allMF.Length == 0) return;

        if (PositionMap != null) PositionMap.Release();
        if (NormalMap != null) NormalMap.Release();
        PositionMap = MakeRT(targetResolution);
        NormalMap = MakeRT(targetResolution);

        var depthRT = new RenderTexture(targetResolution, targetResolution, 16, RenderTextureFormat.Depth);
        depthRT.Create();

        var mat = new Material(bakeShader);
        var cmd = new CommandBuffer { name = "UVBake" };

        cmd.SetRenderTarget(new RenderTargetIdentifier[] { PositionMap, NormalMap }, depthRT);
        cmd.ClearRenderTarget(true, true, Color.clear);

        foreach (var mf in allMF)
        {
            if (mf.sharedMesh == null) continue;
            var mpb = new MaterialPropertyBlock();
            mpb.SetMatrix("_O2W", mf.transform.localToWorldMatrix);
            mpb.SetMatrix("_O2WIT", mf.transform.localToWorldMatrix.inverse.transpose);
            for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                cmd.DrawMesh(mf.sharedMesh, mf.transform.localToWorldMatrix, mat, sub, 0, mpb);
        }

        Graphics.ExecuteCommandBuffer(cmd);
        
        // CALCULATE TOTAL SURFACE PIXELS
        CalculateSurfacePixels(targetResolution);

        cmd.Release();
        DestroyImmediate(mat);
        depthRT.Release();
        DestroyImmediate(depthRT);
    }

    private void CalculateSurfacePixels(int res)
    {
        var readback = new Texture2D(res, res, TextureFormat.RGBAFloat, false);
        RenderTexture activeRT = RenderTexture.active;
        RenderTexture.active = PositionMap;
        readback.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        readback.Apply();
        RenderTexture.active = activeRT;

        var data = readback.GetRawTextureData<Vector4>();
        int count = 0;
        for (int i = 0; i < data.Length; i++) if (data[i].w > 0.5f) count++; // Alpha channel used for masking
        SurfacePixelCount = count > 0 ? count : 1; // Prevent div by zero
        
        if (Application.isPlaying) Destroy(readback); else DestroyImmediate(readback);
    }

    RenderTexture MakeRT(int res)
    {
        var rt = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true, filterMode = FilterMode.Bilinear };
        rt.Create();
        return rt;
    }

    public void SaveToEXR()
    {
        SaveRT(PositionMap, "PositionMap");
        SaveRT(NormalMap, "NormalMap");
    }

    private void SaveRT(RenderTexture rt, string label)
    {
        if (rt == null) return;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.dataPath, $"../{gameObject.name}_{label}.exr"), tex.EncodeToEXR());
        DestroyImmediate(tex);
    }
}
