using System.Numerics;
using Assimp;

namespace GLBManipulator.Core;

public static class ObjReader
{
    public static GlbData Load(string filePath)
    {
        var context = new AssimpContext();
        var scene = context.ImportFile(filePath,
            PostProcessSteps.Triangulate |
            PostProcessSteps.GenerateNormals |
            PostProcessSteps.FlipUVs);

        var glbData = new GlbData();

        foreach (var mesh in scene.Meshes)
        {
            var meshData = new MeshData();

            // Positions
            foreach (var vertex in mesh.Vertices)
            {
                meshData.Positions.Add(new Vector3(vertex.X, vertex.Y, vertex.Z));
            }

            // Normals
            if (mesh.HasNormals)
            {
                foreach (var normal in mesh.Normals)
                {
                    meshData.Normals.Add(new Vector3(normal.X, normal.Y, normal.Z));
                }
            }

            // UVs (first channel)
            if (mesh.HasTextureCoords(0))
            {
                foreach (var uv in mesh.TextureCoordinateChannels[0])
                {
                    meshData.TexCoords.Add(new Vector2(uv.X, uv.Y));
                }
            }

            // Indices
            foreach (var face in mesh.Faces)
            {
                if (face.IndexCount == 3)
                {
                    meshData.Indices.Add((uint)face.Indices[0]);
                    meshData.Indices.Add((uint)face.Indices[1]);
                    meshData.Indices.Add((uint)face.Indices[2]);
                }
            }

            // Material name
            if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < scene.MaterialCount)
            {
                meshData.MaterialName = scene.Materials[mesh.MaterialIndex].Name;
            }

            glbData.Meshes.Add(meshData);
        }

        // Load textures from materials
        foreach (var material in scene.Materials)
        {
            if (material.HasTextureDiffuse)
            {
                var texturePath = material.TextureDiffuse.FilePath;
                if (!string.IsNullOrEmpty(texturePath))
                {
                    // Try to load texture relative to OBJ file
                    string? dir = Path.GetDirectoryName(filePath);
                    string fullPath = dir != null ? Path.Combine(dir, texturePath) : texturePath;

                    if (File.Exists(fullPath))
                    {
                        var textureData = new TextureData
                        {
                            Name = Path.GetFileName(texturePath),
                            Data = File.ReadAllBytes(fullPath),
                            MimeType = GetMimeType(fullPath)
                        };
                        glbData.Textures.Add(textureData);
                    }
                }
            }
        }

        return glbData;
    }

    private static string GetMimeType(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }
}
