using System.Numerics;
using GLBManipulator.Native;

namespace GLBManipulator.Core;

public static class MeshSimplifier
{
    public static MeshData Simplify(MeshData input, int targetTriangles, bool lockBorder = true)
    {
        if (input.TriangleCount <= targetTriangles)
        {
            Console.WriteLine($"이미 목표 폴리곤 수({targetTriangles}) 이하입니다. 원본 반환.");
            return input.Clone();
        }

        int vertexCount = input.Positions.Count;
        bool hasNormals = input.Normals.Count == vertexCount;
        bool hasUVs = input.TexCoords.Count == vertexCount;

        // Position 배열 생성
        float[] positions = new float[vertexCount * 3];
        for (int i = 0; i < vertexCount; i++)
        {
            positions[i * 3 + 0] = input.Positions[i].X;
            positions[i * 3 + 1] = input.Positions[i].Y;
            positions[i * 3 + 2] = input.Positions[i].Z;
        }

        uint[] indices = input.Indices.ToArray();
        uint[] resultIndices = new uint[indices.Length];
        nuint targetIndexCount = (nuint)(targetTriangles * 3);

        float resultError = 0;
        nuint resultIndexCount;

        uint options = lockBorder ? MeshOptimizer.meshopt_SimplifyLockBorder : 0;

        unsafe
        {
            fixed (uint* destPtr = resultIndices)
            fixed (uint* idxPtr = indices)
            fixed (float* posPtr = positions)
            {
                if (hasUVs)
                {
                    // UV를 attribute로 전달
                    float[] attributes = new float[vertexCount * 2];
                    for (int i = 0; i < vertexCount; i++)
                    {
                        attributes[i * 2 + 0] = input.TexCoords[i].X;
                        attributes[i * 2 + 1] = input.TexCoords[i].Y;
                    }

                    float[] weights = { 1.0f, 1.0f };

                    fixed (float* attrPtr = attributes)
                    fixed (float* weightsPtr = weights)
                    {
                        resultIndexCount = MeshOptimizer.meshopt_simplifyWithAttributes(
                            destPtr,
                            idxPtr,
                            (nuint)indices.Length,
                            posPtr,
                            (nuint)vertexCount,
                            (nuint)(sizeof(float) * 3),
                            attrPtr,
                            (nuint)(sizeof(float) * 2),
                            weightsPtr,
                            2,
                            null,
                            targetIndexCount,
                            0.01f,
                            options,
                            &resultError
                        );
                    }
                }
                else
                {
                    resultIndexCount = MeshOptimizer.meshopt_simplify(
                        destPtr,
                        idxPtr,
                        (nuint)indices.Length,
                        posPtr,
                        (nuint)vertexCount,
                        (nuint)(sizeof(float) * 3),
                        targetIndexCount,
                        0.01f,
                        options,
                        &resultError
                    );
                }
            }
        }

        // 결과 메시 생성
        var result = new MeshData();
        var usedVertices = new HashSet<uint>();

        for (int i = 0; i < (int)resultIndexCount; i++)
        {
            usedVertices.Add(resultIndices[i]);
        }

        // 버텍스 재매핑
        var vertexRemap = new Dictionary<uint, uint>();
        uint newIndex = 0;

        foreach (var oldIndex in usedVertices.OrderBy(x => x))
        {
            vertexRemap[oldIndex] = newIndex++;
            result.Positions.Add(input.Positions[(int)oldIndex]);

            if (hasNormals)
                result.Normals.Add(input.Normals[(int)oldIndex]);

            if (hasUVs)
                result.TexCoords.Add(input.TexCoords[(int)oldIndex]);
        }

        // 인덱스 재매핑
        for (int i = 0; i < (int)resultIndexCount; i++)
        {
            result.Indices.Add(vertexRemap[resultIndices[i]]);
        }

        result.MaterialName = input.MaterialName;

        Console.WriteLine($"간소화 완료: {input.TriangleCount} -> {result.TriangleCount} 삼각형 (오차: {resultError:F4})");

        return result;
    }
}
