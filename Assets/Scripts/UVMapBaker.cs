using UnityEngine;
using UnityEngine.Rendering;

public class UVMapBaker : MonoBehaviour
{
    public Shader bakeShader;
    public int resolution = 2048;
    public RenderTexture PositionMap { get; private set; }
    public RenderTexture NormalMap { get; private set; }

    [ContextMenu("Bake UV Maps")]
    public void Bake()
    {
        if (bakeShader == null) return;
        var allMF = GetComponentsInChildren<MeshFilter>(true);
        if (allMF.Length == 0) return;

        if (PositionMap != null) PositionMap.Release();
        if (NormalMap != null) NormalMap.Release();
        PositionMap = MakeRT();
        NormalMap = MakeRT();

        var depthRT = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.Depth);
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
        cmd.Release();
        DestroyImmediate(mat);
        depthRT.Release();
        DestroyImmediate(depthRT);
    }

    RenderTexture MakeRT()
    {
        var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true, filterMode = FilterMode.Bilinear };
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
