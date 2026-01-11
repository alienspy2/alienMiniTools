using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace GLBManipulator.Core;

using VERTEX = VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>;

public static class GlbWriter
{
    public static void Save(GlbData data, string filePath)
    {
        var scene = new SceneBuilder();
        var material = new MaterialBuilder("default")
            .WithDoubleSide(true)
            .WithMetallicRoughnessShader();

        foreach (var meshData in data.Meshes)
        {
            var mesh = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>("mesh");
            var prim = mesh.UsePrimitive(material);

            bool hasNormals = meshData.Normals.Count == meshData.Positions.Count;
            bool hasUVs = meshData.TexCoords.Count == meshData.Positions.Count;

            for (int i = 0; i < meshData.Indices.Count; i += 3)
            {
                var v0 = CreateVertex(meshData, (int)meshData.Indices[i], hasNormals, hasUVs);
                var v1 = CreateVertex(meshData, (int)meshData.Indices[i + 1], hasNormals, hasUVs);
                var v2 = CreateVertex(meshData, (int)meshData.Indices[i + 2], hasNormals, hasUVs);
                prim.AddTriangle(v0, v1, v2);
            }

            scene.AddRigidMesh(mesh, Matrix4x4.Identity);
        }

        var model = scene.ToGltf2();
        model.SaveGLB(filePath);
    }

    private static VERTEX CreateVertex(MeshData meshData, int index, bool hasNormals, bool hasUVs)
    {
        var pos = meshData.Positions[index];
        var normal = hasNormals ? meshData.Normals[index] : Vector3.UnitY;
        var uv = hasUVs ? meshData.TexCoords[index] : Vector2.Zero;

        return new VERTEX(
            new VertexPositionNormal(pos, normal),
            new VertexTexture1(uv)
        );
    }
}
