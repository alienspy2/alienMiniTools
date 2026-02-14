using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using UnityEngine;

namespace IronRose.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct TransformUniforms
    {
        public System.Numerics.Matrix4x4 World;
        public System.Numerics.Matrix4x4 ViewProjection;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MaterialUniforms
    {
        public Vector4 Color;
    }

    public class RenderSystem : IDisposable
    {
        private GraphicsDevice? _device;
        private Pipeline? _pipeline;
        private Pipeline? _wireframePipeline;
        private DeviceBuffer? _transformBuffer;
        private DeviceBuffer? _materialBuffer;
        private ResourceSet? _resourceSet;
        private ResourceLayout? _resourceLayout;
        private Shader[]? _shaders;

        public void Initialize(GraphicsDevice device)
        {
            _device = device;
            var factory = device.ResourceFactory;

            // Find shader files
            string shaderDir = FindShaderDirectory();
            string vertexPath = Path.Combine(shaderDir, "vertex.glsl");
            string fragmentPath = Path.Combine(shaderDir, "fragment.glsl");

            // Compile shaders
            _shaders = ShaderCompiler.CompileGLSL(device, vertexPath, fragmentPath);

            // Create uniform buffers
            _transformBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<TransformUniforms>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            _materialBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<MaterialUniforms>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            // Resource layout (set 0: transforms + material)
            _resourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Transforms", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("MaterialData", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            // Resource set
            _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _resourceLayout, _transformBuffer, _materialBuffer));

            // Vertex layout matching our Vertex struct
            var vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

            // Pipeline
            var pipelineDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true,
                    depthWriteEnabled: true,
                    comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.Back,
                    fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _resourceLayout },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: new[] { vertexLayout },
                    shaders: _shaders),
                Outputs = device.SwapchainFramebuffer.OutputDescription,
            };

            _pipeline = factory.CreateGraphicsPipeline(pipelineDesc);

            // Wireframe overlay pipeline (no cull, depth bias to avoid z-fighting)
            var wireframeDesc = pipelineDesc;
            wireframeDesc.RasterizerState = new RasterizerStateDescription(
                cullMode: FaceCullMode.None,
                fillMode: PolygonFillMode.Wireframe,
                frontFace: FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false);
            wireframeDesc.DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: true,
                depthWriteEnabled: false,
                comparisonKind: ComparisonKind.LessEqual);

            _wireframePipeline = factory.CreateGraphicsPipeline(wireframeDesc);
            Console.WriteLine("[RenderSystem] Pipeline created successfully");
        }

        public void Render(CommandList cl, Camera? camera, float aspectRatio)
        {
            if (_device == null || _pipeline == null || camera == null)
                return;

            var viewMatrix = camera.GetViewMatrix().ToNumerics();
            var projMatrix = camera.GetProjectionMatrix(aspectRatio).ToNumerics();
            var viewProj = viewMatrix * projMatrix;

            // --- Solid pass ---
            cl.SetPipeline(_pipeline);
            DrawAllRenderers(cl, viewProj, useWireframeColor: false);

            // --- Wireframe overlay pass ---
            if (Debug.wireframe && _wireframePipeline != null)
            {
                cl.SetPipeline(_wireframePipeline);
                DrawAllRenderers(cl, viewProj, useWireframeColor: true);
            }
        }

        private void DrawAllRenderers(CommandList cl, System.Numerics.Matrix4x4 viewProj, bool useWireframeColor)
        {
            foreach (var renderer in MeshRenderer._allRenderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter?.mesh == null) continue;

                var mesh = filter.mesh;

                // Upload mesh to GPU if needed
                mesh.UploadToGPU(_device!);
                if (mesh.VertexBuffer == null || mesh.IndexBuffer == null) continue;

                // Compute world matrix from transform
                var t = renderer.transform;
                var worldMatrix = UnityEngine.Matrix4x4.TRS(t.position, t.rotation, t.localScale).ToNumerics();

                // Update transform uniform
                var transforms = new TransformUniforms
                {
                    World = worldMatrix,
                    ViewProjection = viewProj,
                };
                cl.UpdateBuffer(_transformBuffer, 0, transforms);

                // Update material uniform
                var color = useWireframeColor
                    ? Debug.wireframeColor
                    : (renderer.material?.color ?? Color.white);
                var materialData = new MaterialUniforms
                {
                    Color = new Vector4(color.r, color.g, color.b, color.a),
                };
                cl.UpdateBuffer(_materialBuffer, 0, materialData);

                // Bind and draw
                cl.SetGraphicsResourceSet(0, _resourceSet);
                cl.SetVertexBuffer(0, mesh.VertexBuffer);
                cl.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
                cl.DrawIndexed((uint)mesh.indices.Length);
            }
        }

        private static string FindShaderDirectory()
        {
            // Try relative paths from working directory
            string[] candidates = { "Shaders", "../Shaders", "../../Shaders" };
            foreach (var candidate in candidates)
            {
                string fullPath = Path.GetFullPath(candidate);
                if (Directory.Exists(fullPath) &&
                    File.Exists(Path.Combine(fullPath, "vertex.glsl")))
                {
                    Console.WriteLine($"[RenderSystem] Shader directory: {fullPath}");
                    return fullPath;
                }
            }

            throw new FileNotFoundException("Could not find Shaders directory with vertex.glsl and fragment.glsl");
        }

        public void Dispose()
        {
            _pipeline?.Dispose();
            _wireframePipeline?.Dispose();
            _transformBuffer?.Dispose();
            _materialBuffer?.Dispose();
            _resourceSet?.Dispose();
            _resourceLayout?.Dispose();

            if (_shaders != null)
            {
                foreach (var shader in _shaders)
                    shader.Dispose();
            }

            Console.WriteLine("[RenderSystem] Disposed");
        }
    }
}
