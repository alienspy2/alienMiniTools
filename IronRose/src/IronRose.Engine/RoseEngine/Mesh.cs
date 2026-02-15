using System.Runtime.InteropServices;
using Veldrid;

namespace RoseEngine
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 UV;

        public Vertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            Position = position;
            Normal = normal;
            UV = uv;
        }

        public static uint SizeInBytes => (uint)Marshal.SizeOf<Vertex>();
    }

    public class Mesh
    {
        public Vertex[] vertices = [];
        public uint[] indices = [];

        internal DeviceBuffer? VertexBuffer;
        internal DeviceBuffer? IndexBuffer;
        internal bool isDirty = true;

        public void UploadToGPU(GraphicsDevice device)
        {
            if (!isDirty || vertices.Length == 0 || indices.Length == 0)
                return;

            var factory = device.ResourceFactory;

            // Dispose old buffers
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();

            // Create vertex buffer
            VertexBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)(vertices.Length * Marshal.SizeOf<Vertex>()),
                BufferUsage.VertexBuffer));
            device.UpdateBuffer(VertexBuffer, 0, vertices);

            // Create index buffer
            IndexBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)(indices.Length * sizeof(uint)),
                BufferUsage.IndexBuffer));
            device.UpdateBuffer(IndexBuffer, 0, indices);

            isDirty = false;
        }

        public void Dispose()
        {
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
            VertexBuffer = null;
            IndexBuffer = null;
        }
    }
}
