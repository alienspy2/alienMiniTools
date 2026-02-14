using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public enum PrimitiveType
    {
        Cube,
        Sphere,
        Capsule,
        Plane,
        Quad,
    }

    public static class PrimitiveGenerator
    {
        public static Mesh CreateCube()
        {
            var mesh = new Mesh();

            // 24 vertices (6 faces x 4 vertices each, separate normals per face)
            mesh.vertices = new Vertex[]
            {
                // Front face (Z+)
                new(new Vector3(-0.5f, -0.5f,  0.5f), Vector3.forward, new Vector2(0, 1)),
                new(new Vector3( 0.5f, -0.5f,  0.5f), Vector3.forward, new Vector2(1, 1)),
                new(new Vector3( 0.5f,  0.5f,  0.5f), Vector3.forward, new Vector2(1, 0)),
                new(new Vector3(-0.5f,  0.5f,  0.5f), Vector3.forward, new Vector2(0, 0)),

                // Back face (Z-)
                new(new Vector3( 0.5f, -0.5f, -0.5f), Vector3.back, new Vector2(0, 1)),
                new(new Vector3(-0.5f, -0.5f, -0.5f), Vector3.back, new Vector2(1, 1)),
                new(new Vector3(-0.5f,  0.5f, -0.5f), Vector3.back, new Vector2(1, 0)),
                new(new Vector3( 0.5f,  0.5f, -0.5f), Vector3.back, new Vector2(0, 0)),

                // Top face (Y+)
                new(new Vector3(-0.5f,  0.5f,  0.5f), Vector3.up, new Vector2(0, 1)),
                new(new Vector3( 0.5f,  0.5f,  0.5f), Vector3.up, new Vector2(1, 1)),
                new(new Vector3( 0.5f,  0.5f, -0.5f), Vector3.up, new Vector2(1, 0)),
                new(new Vector3(-0.5f,  0.5f, -0.5f), Vector3.up, new Vector2(0, 0)),

                // Bottom face (Y-)
                new(new Vector3(-0.5f, -0.5f, -0.5f), Vector3.down, new Vector2(0, 1)),
                new(new Vector3( 0.5f, -0.5f, -0.5f), Vector3.down, new Vector2(1, 1)),
                new(new Vector3( 0.5f, -0.5f,  0.5f), Vector3.down, new Vector2(1, 0)),
                new(new Vector3(-0.5f, -0.5f,  0.5f), Vector3.down, new Vector2(0, 0)),

                // Right face (X+)
                new(new Vector3( 0.5f, -0.5f,  0.5f), Vector3.right, new Vector2(0, 1)),
                new(new Vector3( 0.5f, -0.5f, -0.5f), Vector3.right, new Vector2(1, 1)),
                new(new Vector3( 0.5f,  0.5f, -0.5f), Vector3.right, new Vector2(1, 0)),
                new(new Vector3( 0.5f,  0.5f,  0.5f), Vector3.right, new Vector2(0, 0)),

                // Left face (X-)
                new(new Vector3(-0.5f, -0.5f, -0.5f), Vector3.left, new Vector2(0, 1)),
                new(new Vector3(-0.5f, -0.5f,  0.5f), Vector3.left, new Vector2(1, 1)),
                new(new Vector3(-0.5f,  0.5f,  0.5f), Vector3.left, new Vector2(1, 0)),
                new(new Vector3(-0.5f,  0.5f, -0.5f), Vector3.left, new Vector2(0, 0)),
            };

            // 36 indices (6 faces x 2 triangles x 3 vertices)
            mesh.indices = new uint[]
            {
                 0,  1,  2,   0,  2,  3,  // Front
                 4,  5,  6,   4,  6,  7,  // Back
                 8,  9, 10,   8, 10, 11,  // Top
                12, 13, 14,  12, 14, 15,  // Bottom
                16, 17, 18,  16, 18, 19,  // Right
                20, 21, 22,  20, 22, 23,  // Left
            };

            return mesh;
        }

        /// <summary>UV sphere, radius=0.5, 24 longitude x 16 latitude (Unity-compatible).</summary>
        public static Mesh CreateSphere(int lonSegments = 24, int latSegments = 16)
        {
            var mesh = new Mesh();
            float radius = 0.5f;

            var verts = new List<Vertex>();
            var indices = new List<uint>();

            // Generate vertices ring by ring from top pole to bottom pole
            for (int lat = 0; lat <= latSegments; lat++)
            {
                float theta = MathF.PI * lat / latSegments;       // 0 (top) → π (bottom)
                float sinTheta = MathF.Sin(theta);
                float cosTheta = MathF.Cos(theta);

                for (int lon = 0; lon <= lonSegments; lon++)
                {
                    float phi = 2f * MathF.PI * lon / lonSegments; // 0 → 2π
                    float sinPhi = MathF.Sin(phi);
                    float cosPhi = MathF.Cos(phi);

                    var normal = new Vector3(sinTheta * cosPhi, cosTheta, sinTheta * sinPhi);
                    var pos = normal * radius;
                    var uv = new Vector2((float)lon / lonSegments, (float)lat / latSegments);

                    verts.Add(new Vertex(pos, normal, uv));
                }
            }

            // Generate indices
            int ringSize = lonSegments + 1;
            for (int lat = 0; lat < latSegments; lat++)
            {
                for (int lon = 0; lon < lonSegments; lon++)
                {
                    uint current = (uint)(lat * ringSize + lon);
                    uint next = current + (uint)ringSize;

                    indices.Add(current);
                    indices.Add(next);
                    indices.Add(current + 1);

                    indices.Add(current + 1);
                    indices.Add(next);
                    indices.Add(next + 1);
                }
            }

            mesh.vertices = verts.ToArray();
            mesh.indices = indices.ToArray();
            return mesh;
        }

        /// <summary>Capsule: height=2, radius=0.5, Y-axis aligned (Unity-compatible).</summary>
        public static Mesh CreateCapsule(int lonSegments = 24, int capRings = 8, int bodyRings = 1)
        {
            var mesh = new Mesh();
            float radius = 0.5f;
            float halfHeight = 0.5f; // half of cylinder body height (total height = 2 = body 1 + caps 1)

            var verts = new List<Vertex>();
            var indices = new List<uint>();
            int ringSize = lonSegments + 1;

            // --- Top hemisphere (Y = halfHeight .. halfHeight+radius) ---
            for (int lat = 0; lat <= capRings; lat++)
            {
                float theta = MathF.PI * 0.5f * lat / capRings; // 0 (pole) → π/2 (equator)
                float sinTheta = MathF.Sin(theta);
                float cosTheta = MathF.Cos(theta);

                for (int lon = 0; lon <= lonSegments; lon++)
                {
                    float phi = 2f * MathF.PI * lon / lonSegments;
                    var normal = new Vector3(sinTheta * MathF.Cos(phi), cosTheta, sinTheta * MathF.Sin(phi));
                    var pos = new Vector3(normal.x * radius, halfHeight + normal.y * radius, normal.z * radius);
                    float v = 0.5f * (1f - (float)lat / capRings) * 0.5f; // 0 → 0.25
                    verts.Add(new Vertex(pos, normal, new Vector2((float)lon / lonSegments, v)));
                }
            }

            // --- Cylinder body ---
            for (int ring = 0; ring <= bodyRings; ring++)
            {
                float t = (float)ring / bodyRings;
                float y = halfHeight - t * (2f * halfHeight); // halfHeight → -halfHeight

                for (int lon = 0; lon <= lonSegments; lon++)
                {
                    float phi = 2f * MathF.PI * lon / lonSegments;
                    var normal = new Vector3(MathF.Cos(phi), 0f, MathF.Sin(phi));
                    var pos = new Vector3(normal.x * radius, y, normal.z * radius);
                    float v = 0.25f + t * 0.5f; // 0.25 → 0.75
                    verts.Add(new Vertex(pos, normal, new Vector2((float)lon / lonSegments, v)));
                }
            }

            // --- Bottom hemisphere (Y = -halfHeight .. -(halfHeight+radius)) ---
            for (int lat = 0; lat <= capRings; lat++)
            {
                float theta = MathF.PI * 0.5f + MathF.PI * 0.5f * lat / capRings; // π/2 → π
                float sinTheta = MathF.Sin(theta);
                float cosTheta = MathF.Cos(theta);

                for (int lon = 0; lon <= lonSegments; lon++)
                {
                    float phi = 2f * MathF.PI * lon / lonSegments;
                    var normal = new Vector3(sinTheta * MathF.Cos(phi), cosTheta, sinTheta * MathF.Sin(phi));
                    var pos = new Vector3(normal.x * radius, -halfHeight + normal.y * radius, normal.z * radius);
                    float v = 0.75f + 0.25f * (float)lat / capRings; // 0.75 → 1.0
                    verts.Add(new Vertex(pos, normal, new Vector2((float)lon / lonSegments, v)));
                }
            }

            // Generate indices for all rings
            int totalRings = capRings + bodyRings + capRings + 1; // +1 for connecting ring
            int totalVertexRows = capRings + 1 + bodyRings + 1 + capRings + 1;
            for (int row = 0; row < totalVertexRows - 1; row++)
            {
                for (int lon = 0; lon < lonSegments; lon++)
                {
                    uint current = (uint)(row * ringSize + lon);
                    uint next = current + (uint)ringSize;

                    indices.Add(current);
                    indices.Add(next);
                    indices.Add(current + 1);

                    indices.Add(current + 1);
                    indices.Add(next);
                    indices.Add(next + 1);
                }
            }

            mesh.vertices = verts.ToArray();
            mesh.indices = indices.ToArray();
            return mesh;
        }

        /// <summary>Plane: 10x10 units, 10x10 subdivisions, Y-up (Unity-compatible).</summary>
        public static Mesh CreatePlane(int resolution = 10)
        {
            var mesh = new Mesh();
            float size = 10f;
            float half = size * 0.5f;

            var verts = new Vertex[(resolution + 1) * (resolution + 1)];
            var indices = new List<uint>();

            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    float px = -half + size * x / resolution;
                    float pz = -half + size * z / resolution;
                    float u = (float)x / resolution;
                    float v = (float)z / resolution;

                    verts[z * (resolution + 1) + x] = new Vertex(
                        new Vector3(px, 0f, pz),
                        Vector3.up,
                        new Vector2(u, v));
                }
            }

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    uint i = (uint)(z * (resolution + 1) + x);
                    uint row = (uint)(resolution + 1);

                    indices.Add(i);
                    indices.Add(i + row);
                    indices.Add(i + 1);

                    indices.Add(i + 1);
                    indices.Add(i + row);
                    indices.Add(i + row + 1);
                }
            }

            mesh.vertices = verts;
            mesh.indices = indices.ToArray();
            return mesh;
        }

        /// <summary>Quad: 1x1, centered, facing Z+ (Unity-compatible).</summary>
        public static Mesh CreateQuad()
        {
            var mesh = new Mesh();

            mesh.vertices = new Vertex[]
            {
                new(new Vector3(-0.5f, -0.5f, 0f), Vector3.forward, new Vector2(0, 1)),
                new(new Vector3( 0.5f, -0.5f, 0f), Vector3.forward, new Vector2(1, 1)),
                new(new Vector3( 0.5f,  0.5f, 0f), Vector3.forward, new Vector2(1, 0)),
                new(new Vector3(-0.5f,  0.5f, 0f), Vector3.forward, new Vector2(0, 0)),
            };

            mesh.indices = new uint[]
            {
                0, 1, 2,  0, 2, 3,
            };

            return mesh;
        }
    }
}
