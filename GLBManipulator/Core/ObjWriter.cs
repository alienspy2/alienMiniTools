using System.Globalization;
using System.Text;

namespace GLBManipulator.Core;

public static class ObjWriter
{
    public static void Save(GlbData data, string filePath)
    {
        var sb = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

        sb.AppendLine("# GLBManipulator OBJ Export");
        sb.AppendLine($"# Meshes: {data.Meshes.Count}");
        sb.AppendLine();

        int vertexOffset = 1;
        int texCoordOffset = 1;
        int normalOffset = 1;

        for (int meshIndex = 0; meshIndex < data.Meshes.Count; meshIndex++)
        {
            var mesh = data.Meshes[meshIndex];
            sb.AppendLine($"o mesh_{meshIndex}");

            // 정점
            foreach (var pos in mesh.Positions)
            {
                sb.AppendLine(string.Format(culture, "v {0:F6} {1:F6} {2:F6}", pos.X, pos.Y, pos.Z));
            }

            // 텍스처 좌표
            foreach (var uv in mesh.TexCoords)
            {
                sb.AppendLine(string.Format(culture, "vt {0:F6} {1:F6}", uv.X, uv.Y));
            }

            // 노말
            foreach (var normal in mesh.Normals)
            {
                sb.AppendLine(string.Format(culture, "vn {0:F6} {1:F6} {2:F6}", normal.X, normal.Y, normal.Z));
            }

            bool hasUVs = mesh.TexCoords.Count == mesh.Positions.Count;
            bool hasNormals = mesh.Normals.Count == mesh.Positions.Count;

            // 면
            for (int i = 0; i < mesh.Indices.Count; i += 3)
            {
                var i0 = (int)mesh.Indices[i] + vertexOffset;
                var i1 = (int)mesh.Indices[i + 1] + vertexOffset;
                var i2 = (int)mesh.Indices[i + 2] + vertexOffset;

                if (hasUVs && hasNormals)
                {
                    var t0 = (int)mesh.Indices[i] + texCoordOffset;
                    var t1 = (int)mesh.Indices[i + 1] + texCoordOffset;
                    var t2 = (int)mesh.Indices[i + 2] + texCoordOffset;
                    var n0 = (int)mesh.Indices[i] + normalOffset;
                    var n1 = (int)mesh.Indices[i + 1] + normalOffset;
                    var n2 = (int)mesh.Indices[i + 2] + normalOffset;
                    sb.AppendLine($"f {i0}/{t0}/{n0} {i1}/{t1}/{n1} {i2}/{t2}/{n2}");
                }
                else if (hasUVs)
                {
                    var t0 = (int)mesh.Indices[i] + texCoordOffset;
                    var t1 = (int)mesh.Indices[i + 1] + texCoordOffset;
                    var t2 = (int)mesh.Indices[i + 2] + texCoordOffset;
                    sb.AppendLine($"f {i0}/{t0} {i1}/{t1} {i2}/{t2}");
                }
                else if (hasNormals)
                {
                    var n0 = (int)mesh.Indices[i] + normalOffset;
                    var n1 = (int)mesh.Indices[i + 1] + normalOffset;
                    var n2 = (int)mesh.Indices[i + 2] + normalOffset;
                    sb.AppendLine($"f {i0}//{n0} {i1}//{n1} {i2}//{n2}");
                }
                else
                {
                    sb.AppendLine($"f {i0} {i1} {i2}");
                }
            }

            vertexOffset += mesh.Positions.Count;
            texCoordOffset += mesh.TexCoords.Count;
            normalOffset += mesh.Normals.Count;
            sb.AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString());
    }
}
