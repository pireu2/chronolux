using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Globalization;

namespace ChronoLux.Project
{
    public static class SimpleObjLoader
    {
        public class MeshData
        {
            public List<Vector3> vertices = new List<Vector3>();
            public List<Vector2> uvs = new List<Vector2>();
            public List<Vector3> normals = new List<Vector3>();
            public List<int> triangles = new List<int>();
            public string name;
        }

        public static List<MeshData> ParseObj(string objText, System.Action<float, string> onProgress = null)
        {
            List<Vector3> sourceVertices = new List<Vector3>();
            List<Vector2> sourceUVs = new List<Vector2>();
            List<Vector3> sourceNormals = new List<Vector3>();
            
            List<MeshData> results = new List<MeshData>();
            MeshData currentMesh = new MeshData { name = "Default" };
            Dictionary<ObjVertex, int> vertexCache = new Dictionary<ObjVertex, int>();

            string[] lines = objText.Split('\n');
            int totalLines = lines.Length;

            for (int i = 0; i < totalLines; i++)
            {
                string line = lines[i].Trim();
                if (line.Length < 2 || line.StartsWith("#")) continue;

                if (i % 5000 == 0 && onProgress != null) 
                    onProgress((float)i / totalLines, "Parsing geometry data...");

                string[] parts = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                switch (parts[0])
                {
                    case "o":
                    case "g":
                        if (currentMesh.vertices.Count > 0) {
                            results.Add(currentMesh);
                            currentMesh = new MeshData();
                            vertexCache.Clear();
                        }
                        currentMesh.name = parts.Length > 1 ? parts[1] : "Object_" + results.Count;
                        break;
                    case "v":
                        if (parts.Length >= 4)
                            sourceVertices.Add(new Vector3(-float.Parse(parts[1], CultureInfo.InvariantCulture), 
                                                           float.Parse(parts[2], CultureInfo.InvariantCulture), 
                                                           float.Parse(parts[3], CultureInfo.InvariantCulture)));
                        break;
                    case "vt":
                        if (parts.Length >= 3)
                            sourceUVs.Add(new Vector2(float.Parse(parts[1], CultureInfo.InvariantCulture), 
                                                      float.Parse(parts[2], CultureInfo.InvariantCulture)));
                        break;
                    case "vn":
                        if (parts.Length >= 4)
                            sourceNormals.Add(new Vector3(-float.Parse(parts[1], CultureInfo.InvariantCulture), 
                                                          float.Parse(parts[2], CultureInfo.InvariantCulture), 
                                                          float.Parse(parts[3], CultureInfo.InvariantCulture)));
                        break;
                    case "f":
                        List<int> faceIndices = new List<int>();
                        for (int k = 1; k < parts.Length; k++)
                        {
                            string[] subParts = parts[k].Split('/');
                            ObjVertex vert = new ObjVertex();
                            
                            if (subParts.Length > 0 && !string.IsNullOrEmpty(subParts[0]))
                                vert.v = int.Parse(subParts[0]) - 1;
                            
                            if (subParts.Length > 1 && !string.IsNullOrEmpty(subParts[1])) 
                                vert.uv = int.Parse(subParts[1]) - 1;
                            else vert.uv = -1;
                            
                            if (subParts.Length > 2 && !string.IsNullOrEmpty(subParts[2])) 
                                vert.n = int.Parse(subParts[2]) - 1;
                            else vert.n = -1;

                            if (!vertexCache.TryGetValue(vert, out int index))
                            {
                                index = currentMesh.vertices.Count;
                                currentMesh.vertices.Add(sourceVertices[vert.v]);
                                if (vert.uv >= 0 && vert.uv < sourceUVs.Count) currentMesh.uvs.Add(sourceUVs[vert.uv]);
                                if (vert.n >= 0 && vert.n < sourceNormals.Count) currentMesh.normals.Add(sourceNormals[vert.n]);
                                vertexCache.Add(vert, index);
                            }
                            faceIndices.Add(index);
                        }

                        for (int k = 1; k < faceIndices.Count - 1; k++)
                        {
                            currentMesh.triangles.Add(faceIndices[0]);
                            currentMesh.triangles.Add(faceIndices[k + 1]);
                            currentMesh.triangles.Add(faceIndices[k]);
                        }
                        break;
                }
            }

            if (currentMesh.vertices.Count > 0) results.Add(currentMesh);
            return results;
        }

        public static Mesh CreateMesh(MeshData data)
        {
            if (data == null || data.vertices.Count == 0) return null;

            Mesh mesh = new Mesh();
            mesh.name = string.IsNullOrEmpty(data.name) ? "ImportedMesh" : data.name;
            mesh.indexFormat = data.vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(data.vertices);
            if (data.uvs.Count == data.vertices.Count) mesh.SetUVs(0, data.uvs);
            if (data.normals.Count == data.vertices.Count) mesh.SetNormals(data.normals);
            else mesh.RecalculateNormals();
            
            mesh.SetTriangles(data.triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private struct ObjVertex
        {
            public int v, uv, n;
            public override bool Equals(object obj) => obj is ObjVertex other && v == other.v && uv == other.uv && n == other.n;
            public override int GetHashCode() => (v, uv, n).GetHashCode();
        }
    }
}
