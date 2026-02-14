using Assimp;
using System.IO;
using UnityEngine;

namespace IronRose.AssetPipeline
{
    public class MeshImporter
    {
        public UnityEngine.Mesh Import(string meshPath, float scale = 1.0f,
            bool generateNormals = true, bool flipUVs = true, bool triangulate = true)
        {
            if (!File.Exists(meshPath))
            {
                Debug.LogError($"Mesh file not found: {meshPath}");
                return null!;
            }

            var postProcess = PostProcessSteps.None;
            if (triangulate) postProcess |= PostProcessSteps.Triangulate;
            if (generateNormals) postProcess |= PostProcessSteps.GenerateNormals;
            if (flipUVs) postProcess |= PostProcessSteps.FlipUVs;

            var context = new AssimpContext();
            var scene = context.ImportFile(meshPath, postProcess);

            if (scene == null || scene.MeshCount == 0)
            {
                Debug.LogError($"Failed to load mesh: {meshPath}");
                return null!;
            }

            // 모든 메시를 하나로 병합
            var allVertices = new List<Vertex>();
            var allIndices = new List<uint>();

            foreach (var assimpMesh in scene.Meshes)
            {
                uint baseVertex = (uint)allVertices.Count;

                bool hasUVs = assimpMesh.TextureCoordinateChannelCount > 0
                    && assimpMesh.TextureCoordinateChannels[0].Count > 0;

                for (int i = 0; i < assimpMesh.VertexCount; i++)
                {
                    var pos = assimpMesh.Vertices[i];
                    var normal = assimpMesh.HasNormals
                        ? assimpMesh.Normals[i]
                        : new Vector3D(0, 1, 0);
                    var uv = hasUVs
                        ? assimpMesh.TextureCoordinateChannels[0][i]
                        : new Vector3D(0, 0, 0);

                    allVertices.Add(new Vertex
                    {
                        Position = new Vector3(pos.X * scale, pos.Y * scale, pos.Z * scale),
                        Normal = new Vector3(normal.X, normal.Y, normal.Z),
                        UV = new Vector2(uv.X, uv.Y)
                    });
                }

                foreach (var face in assimpMesh.Faces)
                {
                    if (face.IndexCount == 3)
                    {
                        allIndices.Add(baseVertex + (uint)face.Indices[0]);
                        allIndices.Add(baseVertex + (uint)face.Indices[1]);
                        allIndices.Add(baseVertex + (uint)face.Indices[2]);
                    }
                }
            }

            var mesh = new UnityEngine.Mesh
            {
                vertices = allVertices.ToArray(),
                indices = allIndices.ToArray()
            };

            Debug.Log($"Imported mesh: {meshPath} ({allVertices.Count} vertices, {allIndices.Count / 3} triangles)");

            return mesh;
        }
    }
}
