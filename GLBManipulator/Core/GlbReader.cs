using System.Numerics;
using SharpGLTF.Schema2;

namespace GLBManipulator.Core;

public static class GlbReader
{
    public static GlbData Load(string filePath)
    {
        var model = ModelRoot.Load(filePath);
        var result = new GlbData();

        // 메시 추출
        foreach (var mesh in model.LogicalMeshes)
        {
            foreach (var primitive in mesh.Primitives)
            {
                var meshData = ExtractMeshData(primitive);
                meshData.MaterialName = primitive.Material?.Name;
                result.Meshes.Add(meshData);
            }
        }

        // 텍스처 추출
        foreach (var image in model.LogicalImages)
        {
            var textureData = new TextureData
            {
                Name = image.Name ?? $"texture_{result.Textures.Count}",
                Data = image.Content.Content.ToArray(),
                MimeType = image.Content.MimeType
            };
            result.Textures.Add(textureData);
        }

        return result;
    }

    private static MeshData ExtractMeshData(MeshPrimitive primitive)
    {
        var meshData = new MeshData();

        // 정점 위치
        var posAccessor = primitive.GetVertexAccessor("POSITION");
        if (posAccessor != null)
        {
            foreach (var pos in posAccessor.AsVector3Array())
            {
                meshData.Positions.Add(pos);
            }
        }

        // 노말
        var normalAccessor = primitive.GetVertexAccessor("NORMAL");
        if (normalAccessor != null)
        {
            foreach (var normal in normalAccessor.AsVector3Array())
            {
                meshData.Normals.Add(normal);
            }
        }

        // 텍스처 좌표
        var texCoordAccessor = primitive.GetVertexAccessor("TEXCOORD_0");
        if (texCoordAccessor != null)
        {
            foreach (var uv in texCoordAccessor.AsVector2Array())
            {
                meshData.TexCoords.Add(uv);
            }
        }

        // 인덱스
        var indexAccessor = primitive.GetIndexAccessor();
        if (indexAccessor != null)
        {
            foreach (var index in indexAccessor.AsIndicesArray())
            {
                meshData.Indices.Add(index);
            }
        }

        return meshData;
    }
}
