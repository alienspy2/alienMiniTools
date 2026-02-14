using System;
using System.Collections.Generic;
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
        public Vector4 Emission;
        public float HasTexture;
        public float _pad1;
        public float _pad2;
        public float _pad3;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LightInfoGPU
    {
        public Vector4 PositionOrDirection; // xyz = pos/dir, w = type (0=dir, 1=point)
        public Vector4 ColorIntensity;      // rgb = color, a = intensity
        public Vector4 Params;              // x = range
        public Vector4 _padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LightUniforms
    {
        public Vector4 CameraPos;   // xyz = camera pos, w = unused
        public int LightCount;
        public int _pad1;
        public int _pad2;
        public int _pad3;
        public LightInfoGPU Light0;
        public LightInfoGPU Light1;
        public LightInfoGPU Light2;
        public LightInfoGPU Light3;
        public LightInfoGPU Light4;
        public LightInfoGPU Light5;
        public LightInfoGPU Light6;
        public LightInfoGPU Light7;
    }

    public class RenderSystem : IDisposable
    {
        private GraphicsDevice? _device;
        private Pipeline? _pipeline;
        private Pipeline? _wireframePipeline;
        private DeviceBuffer? _transformBuffer;
        private DeviceBuffer? _materialBuffer;
        private DeviceBuffer? _lightBuffer;
        private ResourceLayout? _perObjectLayout;
        private ResourceLayout? _perFrameLayout;
        private ResourceSet? _perFrameResourceSet;
        private Shader[]? _shaders;

        // Texture resources
        private Sampler? _defaultSampler;
        private Texture2D? _whiteTexture;

        // Per-material ResourceSet cache (keyed by TextureView or null)
        private readonly Dictionary<TextureView, ResourceSet> _resourceSetCache = new();
        private ResourceSet? _defaultResourceSet; // for materials without texture

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

            _lightBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<LightUniforms>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            // Default sampler
            _defaultSampler = factory.CreateSampler(new SamplerDescription(
                SamplerAddressMode.Wrap, SamplerAddressMode.Wrap, SamplerAddressMode.Wrap,
                SamplerFilter.MinLinear_MagLinear_MipLinear,
                null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));

            // White fallback texture (1x1)
            _whiteTexture = Texture2D.CreateWhitePixel();
            _whiteTexture.UploadToGPU(device);

            // Per-object layout (set 0): transforms + material + texture + sampler
            _perObjectLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Transforms", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("MaterialData", ResourceKind.UniformBuffer, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            // Per-frame layout (set 1): lights
            _perFrameLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("LightData", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            // Per-frame resource set
            _perFrameResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _perFrameLayout, _lightBuffer));

            // Default per-object resource set (white texture)
            _defaultResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _perObjectLayout, _transformBuffer, _materialBuffer, _whiteTexture.TextureView!, _defaultSampler));

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
                ResourceLayouts = new[] { _perObjectLayout, _perFrameLayout },
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
            Console.WriteLine("[RenderSystem] Pipeline created successfully (texture + lighting)");
        }

        public void Render(CommandList cl, Camera? camera, float aspectRatio)
        {
            if (_device == null || _pipeline == null || camera == null)
                return;

            var viewMatrix = camera.GetViewMatrix().ToNumerics();
            var projMatrix = camera.GetProjectionMatrix(aspectRatio).ToNumerics();
            var viewProj = viewMatrix * projMatrix;

            // Upload light data once per frame
            UploadLightData(cl, camera);

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

        private void UploadLightData(CommandList cl, Camera camera)
        {
            var camPos = camera.transform.position;
            var lightData = new LightUniforms
            {
                CameraPos = new Vector4(camPos.x, camPos.y, camPos.z, 0),
                LightCount = 0,
            };

            int count = 0;
            foreach (var light in Light._allLights)
            {
                if (count >= 8) break;
                if (!light.enabled) continue;
                if (!light.gameObject.activeInHierarchy) continue;

                var info = new LightInfoGPU();

                if (light.type == LightType.Directional)
                {
                    var forward = light.transform.forward;
                    info.PositionOrDirection = new Vector4(forward.x, forward.y, forward.z, 0f);
                }
                else
                {
                    var pos = light.transform.position;
                    info.PositionOrDirection = new Vector4(pos.x, pos.y, pos.z, 1f);
                }

                info.ColorIntensity = new Vector4(light.color.r, light.color.g, light.color.b, light.intensity);
                info.Params = new Vector4(light.range, 0, 0, 0);

                SetLightInfo(ref lightData, count, info);
                count++;
            }

            lightData.LightCount = count;
            cl.UpdateBuffer(_lightBuffer, 0, lightData);
        }

        private static void SetLightInfo(ref LightUniforms data, int index, LightInfoGPU info)
        {
            switch (index)
            {
                case 0: data.Light0 = info; break;
                case 1: data.Light1 = info; break;
                case 2: data.Light2 = info; break;
                case 3: data.Light3 = info; break;
                case 4: data.Light4 = info; break;
                case 5: data.Light5 = info; break;
                case 6: data.Light6 = info; break;
                case 7: data.Light7 = info; break;
            }
        }

        private ResourceSet GetOrCreateResourceSet(TextureView? textureView)
        {
            if (textureView == null)
                return _defaultResourceSet!;

            if (_resourceSetCache.TryGetValue(textureView, out var cached))
                return cached;

            var resourceSet = _device!.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                _perObjectLayout!, _transformBuffer!, _materialBuffer!, textureView, _defaultSampler!));
            _resourceSetCache[textureView] = resourceSet;
            return resourceSet;
        }

        private void DrawAllRenderers(CommandList cl, System.Numerics.Matrix4x4 viewProj, bool useWireframeColor)
        {
            foreach (var renderer in MeshRenderer._allRenderers)
            {
                if (!renderer.enabled) continue;
                if (!renderer.gameObject.activeInHierarchy) continue;

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
                var material = renderer.material;
                var color = useWireframeColor
                    ? Debug.wireframeColor
                    : (material?.color ?? Color.white);
                var emission = useWireframeColor
                    ? Color.black
                    : (material?.emission ?? Color.black);

                // Determine texture
                TextureView? texView = null;
                float hasTexture = 0f;
                if (!useWireframeColor && material?.mainTexture != null)
                {
                    material.mainTexture.UploadToGPU(_device!);
                    if (material.mainTexture.TextureView != null)
                    {
                        texView = material.mainTexture.TextureView;
                        hasTexture = 1f;
                    }
                }

                var materialData = new MaterialUniforms
                {
                    Color = new Vector4(color.r, color.g, color.b, color.a),
                    Emission = new Vector4(emission.r, emission.g, emission.b, emission.a),
                    HasTexture = hasTexture,
                };
                cl.UpdateBuffer(_materialBuffer, 0, materialData);

                // Bind resource sets
                var perObjectSet = GetOrCreateResourceSet(texView);
                cl.SetGraphicsResourceSet(0, perObjectSet);
                cl.SetGraphicsResourceSet(1, _perFrameResourceSet);

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
            _lightBuffer?.Dispose();
            _defaultSampler?.Dispose();
            _whiteTexture?.Dispose();
            _defaultResourceSet?.Dispose();
            _perFrameResourceSet?.Dispose();
            _perObjectLayout?.Dispose();
            _perFrameLayout?.Dispose();

            foreach (var rs in _resourceSetCache.Values)
                rs.Dispose();
            _resourceSetCache.Clear();

            if (_shaders != null)
            {
                foreach (var shader in _shaders)
                    shader.Dispose();
            }

            Console.WriteLine("[RenderSystem] Disposed");
        }
    }
}
