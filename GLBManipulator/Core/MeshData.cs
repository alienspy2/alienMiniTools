using System.Numerics;

namespace GLBManipulator.Core;

public class MeshData
{
    public List<Vector3> Positions { get; set; } = new();
    public List<Vector3> Normals { get; set; } = new();
    public List<Vector2> TexCoords { get; set; } = new();
    public List<uint> Indices { get; set; } = new();
    public string? MaterialName { get; set; }

    public int TriangleCount => Indices.Count / 3;
    public int VertexCount => Positions.Count;

    public MeshData Clone()
    {
        return new MeshData
        {
            Positions = new List<Vector3>(Positions),
            Normals = new List<Vector3>(Normals),
            TexCoords = new List<Vector2>(TexCoords),
            Indices = new List<uint>(Indices),
            MaterialName = MaterialName
        };
    }
}

public class TextureData
{
    public string Name { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string MimeType { get; set; } = "image/png";
}

public class GlbData
{
    public List<MeshData> Meshes { get; set; } = new();
    public List<TextureData> Textures { get; set; } = new();
}
