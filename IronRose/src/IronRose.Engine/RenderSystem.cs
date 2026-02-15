using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using RoseEngine;

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
        public float Metallic;
        public float Roughness;
        public float Occlusion;
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct AmbientUniforms
    {
        public Vector4 CameraPos;       // 16 bytes
        public Vector4 SkyAmbientColor; // 16 bytes
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LightVolumeUniforms
    {
        public System.Numerics.Matrix4x4 WorldViewProjection;  // 64 bytes
        public Vector4 CameraPos;                               // 16 bytes
        public Vector4 ScreenParams;                            // 16 bytes (x=width, y=height)
        public LightInfoGPU Light;                              // 64 bytes
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SkyboxUniforms
    {
        public System.Numerics.Matrix4x4 InverseViewProjection;  // 64 bytes
        public Vector4 SunDirection;                              // 16 bytes (xyz=dir, w=unused)
        public Vector4 SkyParams;                                 // 16 bytes (x=zenithIntensity, y=horizonIntensity, z=sunAngularRadius, w=sunIntensity)
        public Vector4 ZenithColor;                               // 16 bytes
        public Vector4 HorizonColor;                              // 16 bytes
        public Vector4 TextureParams;                             // 16 bytes (x=hasTexture, y=exposure, z=rotation, w=unused)
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EnvMapUniforms
    {
        public Vector4 TextureParams;   // 16 bytes (x=hasTexture, y=exposure, z=rotation(rad), w=unused)
        public Vector4 SunDirection;    // 16 bytes (xyz=direction toward sun, w=unused)
        public Vector4 SkyParams;       // 16 bytes (x=zenithIntensity, y=horizonIntensity, z=sunAngularRadius, w=sunIntensity)
        public Vector4 ZenithColor;     // 16 bytes (rgb=zenith color)
        public Vector4 HorizonColor;    // 16 bytes (rgb=horizon color)
    }

    public class RenderSystem : IDisposable
    {
        private GraphicsDevice? _device;
        private string _shaderDir = "";

        // Debug: 첫 N 프레임 동안 상세 로그 출력 (0 = off)
        private int _debugFrameCount = 0;
        private const int DebugLogFrames = 0;

        // --- Shared resources ---
        private DeviceBuffer? _transformBuffer;
        private DeviceBuffer? _materialBuffer;
        private ResourceLayout? _perObjectLayout;
        private Sampler? _defaultSampler;
        private Texture2D? _whiteTexture;
        private Cubemap? _whiteCubemap;
        private readonly Dictionary<TextureView, ResourceSet> _resourceSetCache = new();
        private ResourceSet? _defaultResourceSet;

        // --- Forward rendering (sprites/text/wireframe) ---
        private Veldrid.Shader[]? _forwardShaders;
        private Pipeline? _forwardPipeline;
        private Pipeline? _wireframePipeline;
        private Pipeline? _spritePipeline;
        private DeviceBuffer? _lightBuffer;
        private ResourceLayout? _perFrameLayout;
        private ResourceSet? _perFrameResourceSet;

        // --- Deferred rendering ---
        private GBuffer? _gBuffer;
        private Veldrid.Shader[]? _geometryShaders;
        private Pipeline? _geometryPipeline;

        // --- Light volume rendering ---
        private Veldrid.Shader[]? _ambientShaders;
        private Veldrid.Shader[]? _directionalLightShaders;
        private Veldrid.Shader[]? _pointLightShaders;

        private Pipeline? _ambientPipeline;
        private Pipeline? _directionalLightPipeline;
        private Pipeline? _pointLightPipeline;

        private ResourceLayout? _gBufferLayout;
        private ResourceLayout? _ambientLayout;
        private ResourceLayout? _lightVolumeLayout;

        private ResourceSet? _gBufferResourceSet;
        private ResourceSet? _ambientResourceSet;
        private ResourceSet? _lightVolumeResourceSet;

        private DeviceBuffer? _ambientBuffer;
        private DeviceBuffer? _lightVolumeBuffer;
        private DeviceBuffer? _envMapBuffer;

        private Mesh? _lightSphereMesh;
        private TextureView? _currentAmbientEnvMapView;

        // --- Skybox ---
        private Veldrid.Shader[]? _skyboxShaders;
        private Pipeline? _skyboxPipeline;
        private DeviceBuffer? _skyboxUniformBuffer;
        private ResourceLayout? _skyboxLayout;
        private ResourceSet? _skyboxResourceSet;
        private TextureView? _currentSkyboxTextureView; // Track for resource set invalidation

        // --- HDR intermediate ---
        private Texture? _hdrTexture;
        private TextureView? _hdrView;
        private Framebuffer? _hdrFramebuffer;

        // --- Post-processing ---
        private PostProcessStack? _postProcessStack;
        public PostProcessStack? PostProcessing => _postProcessStack;


        public void Initialize(GraphicsDevice device)
        {
            _device = device;
            _shaderDir = FindShaderDirectory();
            var factory = device.ResourceFactory;
            uint width = device.SwapchainFramebuffer.Width;
            uint height = device.SwapchainFramebuffer.Height;

            // --- Compile shaders ---
            _forwardShaders = ShaderCompiler.CompileGLSL(device,
                Path.Combine(_shaderDir, "vertex.glsl"),
                Path.Combine(_shaderDir, "fragment.glsl"));

            _geometryShaders = ShaderCompiler.CompileGLSL(device,
                Path.Combine(_shaderDir, "deferred_geometry.vert"),
                Path.Combine(_shaderDir, "deferred_geometry.frag"));

            _ambientShaders = ShaderCompiler.CompileGLSL(device,
                Path.Combine(_shaderDir, "deferred_lighting.vert"),
                Path.Combine(_shaderDir, "deferred_ambient.frag"));

            _directionalLightShaders = ShaderCompiler.CompileGLSL(device,
                Path.Combine(_shaderDir, "deferred_lighting.vert"),
                Path.Combine(_shaderDir, "deferred_directlight.frag"));

            _pointLightShaders = ShaderCompiler.CompileGLSL(device,
                Path.Combine(_shaderDir, "deferred_pointlight.vert"),
                Path.Combine(_shaderDir, "deferred_pointlight.frag"));

            _skyboxShaders = ShaderCompiler.CompileGLSL(device,
                Path.Combine(_shaderDir, "skybox.vert"),
                Path.Combine(_shaderDir, "skybox.frag"));

            // --- Uniform buffers ---
            _transformBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<TransformUniforms>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            _materialBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<MaterialUniforms>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            _lightBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<LightUniforms>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            _ambientBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<AmbientUniforms>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            _lightVolumeBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<LightVolumeUniforms>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            _skyboxUniformBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<SkyboxUniforms>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            _envMapBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<EnvMapUniforms>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            // --- Sampler ---
            _defaultSampler = factory.CreateSampler(new SamplerDescription(
                SamplerAddressMode.Wrap, SamplerAddressMode.Wrap, SamplerAddressMode.Wrap,
                SamplerFilter.MinLinear_MagLinear_MipLinear,
                null, 0, 0, uint.MaxValue, 0, SamplerBorderColor.TransparentBlack));

            // --- White fallback texture ---
            _whiteTexture = Texture2D.CreateWhitePixel();
            _whiteTexture.UploadToGPU(device);

            // --- White fallback cubemap ---
            _whiteCubemap = Cubemap.CreateWhiteCubemap();
            _whiteCubemap.UploadToGPU(device);

            // --- Resource layouts ---
            // Per-object (set 0): transforms + material + texture + sampler
            _perObjectLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Transforms", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("MaterialData", ResourceKind.UniformBuffer, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            // Per-frame (set 1): lights (forward)
            _perFrameLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("LightData", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            // GBuffer layout (set 0, shared by all lighting passes)
            _gBufferLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("gAlbedo", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("gNormal", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("gMaterial", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("gWorldPos", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("gSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            // Ambient layout (set 1)
            _ambientLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("AmbientData", ResourceKind.UniformBuffer, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("EnvMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("EnvMapParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            // Light volume layout (set 1, shared by directional + point)
            _lightVolumeLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("LightVolumeData", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

            // Skybox (set 0): uniform buffer + texture + sampler
            _skyboxLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("SkyboxUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SkyTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SkySampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            // --- Forward resource sets ---
            _perFrameResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _perFrameLayout, _lightBuffer));

            _defaultResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _perObjectLayout, _transformBuffer, _materialBuffer, _whiteTexture.TextureView!, _defaultSampler));

            _skyboxResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _skyboxLayout, _skyboxUniformBuffer, _whiteCubemap!.TextureView!, _defaultSampler));
            _currentSkyboxTextureView = null;

            // --- Light volume resource set (buffer doesn't change, content does) ---
            _lightVolumeResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _lightVolumeLayout, _lightVolumeBuffer));

            // --- Light sphere mesh for point light volumes ---
            _lightSphereMesh = PrimitiveGenerator.CreateSphere(12, 8);
            _lightSphereMesh.UploadToGPU(device);

            // --- Create size-dependent resources (GBuffer, HDR, pipelines) ---
            CreateSizeDependentResources(width, height);

            // --- PostProcessing Stack ---
            _postProcessStack = new PostProcessStack();
            _postProcessStack.Initialize(device, width, height, _shaderDir);
            _postProcessStack.AddEffect(new BloomEffect());
            _postProcessStack.AddEffect(new TonemapEffect());

            Console.WriteLine("[RenderSystem] Light volume PBR pipeline initialized");
        }

        private void CreateSizeDependentResources(uint width, uint height)
        {
            var factory = _device!.ResourceFactory;

            // Dispose old size-dependent resources
            _gBufferResourceSet?.Dispose();
            _ambientResourceSet?.Dispose();
            _hdrView?.Dispose();
            _hdrFramebuffer?.Dispose();
            _hdrTexture?.Dispose();
            _geometryPipeline?.Dispose();
            _ambientPipeline?.Dispose();
            _directionalLightPipeline?.Dispose();
            _pointLightPipeline?.Dispose();
            _skyboxPipeline?.Dispose();
            _forwardPipeline?.Dispose();
            _wireframePipeline?.Dispose();
            _spritePipeline?.Dispose();

            // --- GBuffer ---
            _gBuffer ??= new GBuffer();
            _gBuffer.Initialize(_device, width, height);

            // --- HDR intermediate texture ---
            _hdrTexture = factory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1,
                PixelFormat.R16_G16_B16_A16_Float,
                TextureUsage.RenderTarget | TextureUsage.Sampled));
            _hdrView = factory.CreateTextureView(_hdrTexture);

            // HDR framebuffer shares depth with GBuffer (for forward pass depth testing)
            _hdrFramebuffer = factory.CreateFramebuffer(new FramebufferDescription(
                _gBuffer.DepthTexture,
                _hdrTexture));

            // --- Vertex layout ---
            var vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

            // --- Geometry Pipeline (→ GBuffer, 4 color + depth) ---
            // Explicitly provide 4 blend attachments for MRT
            var gBufferBlend = new BlendStateDescription
            {
                AttachmentStates = new[]
                {
                    BlendAttachmentDescription.OverrideBlend, // RT0: Albedo
                    BlendAttachmentDescription.OverrideBlend, // RT1: Normal
                    BlendAttachmentDescription.OverrideBlend, // RT2: Material
                    BlendAttachmentDescription.OverrideBlend, // RT3: WorldPos
                }
            };
            _geometryPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = gBufferBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true, depthWriteEnabled: true, comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.Back, fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _perObjectLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: new[] { vertexLayout },
                    shaders: _geometryShaders!),
                Outputs = _gBuffer.Framebuffer.OutputDescription,
            });

            // --- Additive blend state (shared by directional + point light passes) ---
            var additiveBlend = new BlendStateDescription(
                RgbaFloat.Black,
                new BlendAttachmentDescription(
                    blendEnabled: true,
                    sourceColorFactor: BlendFactor.One,
                    destinationColorFactor: BlendFactor.One,
                    colorFunction: BlendFunction.Add,
                    sourceAlphaFactor: BlendFactor.One,
                    destinationAlphaFactor: BlendFactor.One,
                    alphaFunction: BlendFunction.Add));

            // --- Ambient Pipeline (→ HDR, fullscreen, overwrite) ---
            _ambientPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.None, fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _gBufferLayout!, _ambientLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: Array.Empty<VertexLayoutDescription>(),
                    shaders: _ambientShaders!),
                Outputs = _hdrFramebuffer.OutputDescription,
            });

            // --- Directional Light Pipeline (→ HDR, fullscreen, additive) ---
            _directionalLightPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = additiveBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.None, fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _gBufferLayout!, _lightVolumeLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: Array.Empty<VertexLayoutDescription>(),
                    shaders: _directionalLightShaders!),
                Outputs = _hdrFramebuffer.OutputDescription,
            });

            // --- Point Light Pipeline (→ HDR, sphere mesh, additive) ---
            // CullMode.Back + GreaterEqual: keep front faces (engine's winding convention),
            // which are the far hemisphere from inside the sphere → depth > scene = pixel inside volume
            _pointLightPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = additiveBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true, depthWriteEnabled: false, comparisonKind: ComparisonKind.GreaterEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.Back, fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _gBufferLayout!, _lightVolumeLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: new[] { vertexLayout },
                    shaders: _pointLightShaders!),
                Outputs = _hdrFramebuffer.OutputDescription,
            });

            // --- Skybox Pipeline (→ HDR, depth test LessEqual, no depth write) ---
            _skyboxPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true, depthWriteEnabled: false, comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.None, fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _skyboxLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: Array.Empty<VertexLayoutDescription>(),
                    shaders: _skyboxShaders!),
                Outputs = _hdrFramebuffer.OutputDescription,
            });

            // --- Forward Pipeline (→ HDR, for fallback solid rendering) ---
            _forwardPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true, depthWriteEnabled: true, comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.Back, fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _perObjectLayout!, _perFrameLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: new[] { vertexLayout },
                    shaders: _forwardShaders!),
                Outputs = _hdrFramebuffer.OutputDescription,
            });

            // --- Wireframe Pipeline (→ HDR) ---
            _wireframePipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true, depthWriteEnabled: false, comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.None, fillMode: PolygonFillMode.Wireframe,
                    frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _perObjectLayout!, _perFrameLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: new[] { vertexLayout },
                    shaders: _forwardShaders!),
                Outputs = _hdrFramebuffer.OutputDescription,
            });

            // --- Sprite Pipeline (→ HDR, alpha blend) ---
            _spritePipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = new BlendStateDescription(
                    RgbaFloat.Black,
                    new BlendAttachmentDescription(
                        blendEnabled: true,
                        sourceColorFactor: BlendFactor.SourceAlpha,
                        destinationColorFactor: BlendFactor.InverseSourceAlpha,
                        colorFunction: BlendFunction.Add,
                        sourceAlphaFactor: BlendFactor.One,
                        destinationAlphaFactor: BlendFactor.InverseSourceAlpha,
                        alphaFunction: BlendFunction.Add)),
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true, depthWriteEnabled: false, comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.None, fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _perObjectLayout!, _perFrameLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: new[] { vertexLayout },
                    shaders: _forwardShaders!),
                Outputs = _hdrFramebuffer.OutputDescription,
            });

            // --- GBuffer resource set (shared by all lighting passes) ---
            _gBufferResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _gBufferLayout!,
                _gBuffer.AlbedoView,
                _gBuffer.NormalView,
                _gBuffer.MaterialView,
                _gBuffer.WorldPosView,
                _defaultSampler!));

            // --- Ambient resource set ---
            _ambientResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _ambientLayout!,
                _ambientBuffer!,
                _whiteCubemap!.TextureView!,
                _envMapBuffer!));
            _currentAmbientEnvMapView = null;
        }

        public void Resize(uint width, uint height)
        {
            if (_device == null || width == 0 || height == 0) return;

            _device.WaitForIdle();

            CreateSizeDependentResources(width, height);

            _postProcessStack?.Resize(width, height);

            Console.WriteLine($"[RenderSystem] Resized to {width}x{height}");
        }

        // ==============================
        // Render
        // ==============================

        public void Render(CommandList cl, Camera? camera, float aspectRatio)
        {
            if (_device == null || _gBuffer == null || camera == null)
                return;

            bool debugLog = _debugFrameCount < DebugLogFrames;
            _debugFrameCount++;

            if (debugLog)
            {
                Console.WriteLine($"[Render] === Frame {_debugFrameCount} ===");
                Console.WriteLine($"[Render] Camera: pos=({camera.transform.position.x:F2},{camera.transform.position.y:F2},{camera.transform.position.z:F2}) fov={camera.fieldOfView} clearFlags={camera.clearFlags}");
                Console.WriteLine($"[Render] GBuffer: {_gBuffer.Width}x{_gBuffer.Height}");
                Console.WriteLine($"[Render] MeshRenderers: {MeshRenderer._allRenderers.Count}, Lights: {Light._allLights.Count}");
            }

            var viewMatrix = camera.GetViewMatrix().ToNumerics();
            var projMatrix = camera.GetProjectionMatrix(aspectRatio).ToNumerics();
            var viewProj = viewMatrix * projMatrix;

            // === 1. Geometry Pass → G-Buffer ===
            cl.SetFramebuffer(_gBuffer.Framebuffer);
            cl.ClearColorTarget(0, RgbaFloat.Clear);     // Albedo
            cl.ClearColorTarget(1, RgbaFloat.Clear);     // Normal
            cl.ClearColorTarget(2, RgbaFloat.Clear);     // Material
            cl.ClearColorTarget(3, RgbaFloat.Clear);               // WorldPos (alpha=0 → no geometry)
            cl.ClearDepthStencil(1f);
            cl.SetPipeline(_geometryPipeline);
            int opaqueCount = DrawOpaqueRenderersDebug(cl, viewProj, debugLog);
            if (debugLog)
                Console.WriteLine($"[Render] GeometryPass: drew {opaqueCount} opaque objects");

            // === 2. Ambient/IBL Pass → HDR (Overwrite) ===
            cl.SetFramebuffer(_hdrFramebuffer);
            if (camera.clearFlags == CameraClearFlags.SolidColor)
            {
                var bg = camera.backgroundColor;
                cl.ClearColorTarget(0, new RgbaFloat(bg.r, bg.g, bg.b, bg.a));
                if (debugLog)
                    Console.WriteLine($"[Render] HDR clear: SolidColor ({bg.r:F2},{bg.g:F2},{bg.b:F2})");
            }
            else
            {
                cl.ClearColorTarget(0, RgbaFloat.Clear);
                if (debugLog)
                    Console.WriteLine($"[Render] HDR clear: Transparent (Skybox mode)");
            }
            // (depth is shared with GBuffer — do NOT clear it)

            UpdateEnvMapForAmbient();
            UploadAmbientData(cl, camera);
            UploadEnvMapData(cl);

            cl.SetPipeline(_ambientPipeline);
            cl.SetGraphicsResourceSet(0, _gBufferResourceSet);
            cl.SetGraphicsResourceSet(1, _ambientResourceSet);
            cl.Draw(3, 1, 0, 0);
            if (debugLog)
                Console.WriteLine($"[Render] AmbientPass: fullscreen draw");

            // === 3. Direct Lights → HDR (Additive) ===
            int dirCount = 0, pointCount = 0;
            foreach (var light in Light._allLights)
            {
                if (!light.enabled || !light.gameObject.activeInHierarchy) continue;

                UploadSingleLightUniforms(cl, light, camera, viewProj);

                if (light.type == LightType.Directional)
                {
                    cl.SetPipeline(_directionalLightPipeline);
                    cl.SetGraphicsResourceSet(0, _gBufferResourceSet);
                    cl.SetGraphicsResourceSet(1, _lightVolumeResourceSet);
                    cl.Draw(3, 1, 0, 0);
                    dirCount++;

                    if (debugLog)
                    {
                        var fwd = light.transform.forward;
                        Console.WriteLine($"[Render] DirectionalLight: dir=({fwd.x:F2},{fwd.y:F2},{fwd.z:F2}) color=({light.color.r:F2},{light.color.g:F2},{light.color.b:F2}) intensity={light.intensity:F2}");
                    }
                }
                else // Point
                {
                    cl.SetPipeline(_pointLightPipeline);
                    cl.SetGraphicsResourceSet(0, _gBufferResourceSet);
                    cl.SetGraphicsResourceSet(1, _lightVolumeResourceSet);
                    cl.SetVertexBuffer(0, _lightSphereMesh!.VertexBuffer);
                    cl.SetIndexBuffer(_lightSphereMesh.IndexBuffer!, IndexFormat.UInt32);
                    cl.DrawIndexed((uint)_lightSphereMesh.indices.Length);
                    pointCount++;

                    if (debugLog)
                    {
                        var pos = light.transform.position;
                        float scale = light.range * 2.0f;
                        Console.WriteLine($"[Render] PointLight: pos=({pos.x:F2},{pos.y:F2},{pos.z:F2}) range={light.range:F1} sphereScale={scale:F1} color=({light.color.r:F2},{light.color.g:F2},{light.color.b:F2}) intensity={light.intensity:F2} indices={_lightSphereMesh.indices.Length}");
                    }
                }
            }
            if (debugLog)
                Console.WriteLine($"[Render] LightPass: {dirCount} directional, {pointCount} point lights");

            // === 4. Skybox Pass → HDR (depth test LessEqual, only empty pixels) ===
            if (camera.clearFlags == CameraClearFlags.Skybox)
            {
                RenderSkybox(cl, camera, viewProj);
                if (debugLog)
                    Console.WriteLine($"[Render] SkyboxPass: rendered");
            }
            else if (debugLog)
            {
                Console.WriteLine($"[Render] SkyboxPass: skipped (clearFlags={camera.clearFlags})");
            }

            // === 5. Forward Pass → HDR (sprites, text, wireframe) ===
            if (Debug.wireframe && _wireframePipeline != null)
            {
                UploadForwardLightData(cl, camera);
                cl.SetPipeline(_wireframePipeline);
                DrawAllRenderers(cl, viewProj, useWireframeColor: true);
            }

            if (_spritePipeline != null && SpriteRenderer._allSpriteRenderers.Count > 0)
            {
                DrawAllSprites(cl, viewProj, camera);
            }

            if (_spritePipeline != null && TextRenderer._allTextRenderers.Count > 0)
            {
                DrawAllTexts(cl, viewProj, camera);
                if (debugLog)
                    Console.WriteLine($"[Render] ForwardPass: sprites={SpriteRenderer._allSpriteRenderers.Count} texts={TextRenderer._allTextRenderers.Count}");
            }

            // === 6. Post-Processing → Swapchain ===
            _postProcessStack?.Execute(cl, _hdrView!, _device.SwapchainFramebuffer);
            if (debugLog)
                Console.WriteLine($"[Render] PostProcess: done → swapchain");
        }

        // ==============================
        // Deferred light upload
        // ==============================

        private static LightInfoGPU CollectLightInfo(Light light)
        {
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
            return info;
        }

        private void UploadAmbientData(CommandList cl, Camera camera)
        {
            var camPos = camera.transform.position;
            var uniforms = new AmbientUniforms
            {
                CameraPos = new Vector4(camPos.x, camPos.y, camPos.z, 0),
                SkyAmbientColor = ComputeSkyAmbientColor(),
            };
            cl.UpdateBuffer(_ambientBuffer, 0, uniforms);
        }

        private void UploadSingleLightUniforms(CommandList cl, Light light, Camera camera,
            System.Numerics.Matrix4x4 viewProj)
        {
            var camPos = camera.transform.position;
            var lightInfo = CollectLightInfo(light);

            System.Numerics.Matrix4x4 mvp;
            if (light.type == LightType.Point)
            {
                var pos = light.transform.position;
                float scale = light.range * 2.0f;
                var world = RoseEngine.Matrix4x4.TRS(
                    new RoseEngine.Vector3(pos.x, pos.y, pos.z),
                    RoseEngine.Quaternion.identity,
                    new RoseEngine.Vector3(scale, scale, scale)).ToNumerics();
                mvp = world * viewProj;
            }
            else
            {
                mvp = System.Numerics.Matrix4x4.Identity;
            }

            var uniforms = new LightVolumeUniforms
            {
                WorldViewProjection = mvp,
                CameraPos = new Vector4(camPos.x, camPos.y, camPos.z, 0),
                ScreenParams = new Vector4(_gBuffer!.Width, _gBuffer.Height, 0, 0),
                Light = lightInfo,
            };

            cl.UpdateBuffer(_lightVolumeBuffer, 0, uniforms);
        }

        // ==============================
        // Environment map for deferred lighting
        // ==============================

        private void UpdateEnvMapForAmbient()
        {
            TextureView? envMapView = null;
            var skyboxMat = RenderSettings.skybox;
            if (skyboxMat?.shader?.name == "Skybox/Panoramic" && skyboxMat.mainTexture != null)
            {
                // Lazy cubemap conversion with caching
                if (skyboxMat._cachedCubemap == null || skyboxMat._cachedCubemapSource != skyboxMat.mainTexture)
                {
                    skyboxMat._cachedCubemap?.Dispose();
                    skyboxMat._cachedCubemap = Cubemap.CreateFromEquirectangular(skyboxMat.mainTexture, 512);
                    skyboxMat._cachedCubemap.UploadToGPU(_device!, generateMipmaps: true);
                    skyboxMat._cachedCubemapSource = skyboxMat.mainTexture;
                }
                envMapView = skyboxMat._cachedCubemap.TextureView;
            }

            if (envMapView != _currentAmbientEnvMapView)
            {
                _ambientResourceSet?.Dispose();
                var texView = envMapView ?? _whiteCubemap!.TextureView!;
                _ambientResourceSet = _device!.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                    _ambientLayout!,
                    _ambientBuffer!,
                    texView,
                    _envMapBuffer!));
                _currentAmbientEnvMapView = envMapView;
            }
        }

        private void UploadEnvMapData(CommandList cl)
        {
            var skyboxMat = RenderSettings.skybox;
            bool usePanoramic = skyboxMat?.shader?.name == "Skybox/Panoramic" && skyboxMat.mainTexture != null;

            // Get sun direction from first directional light (or default)
            var sunDir = new Vector4(0.3f, 0.8f, 0.5f, 0f);
            foreach (var light in Light._allLights)
            {
                if (!light.enabled || !light.gameObject.activeInHierarchy) continue;
                if (light.type == LightType.Directional)
                {
                    var forward = light.transform.forward;
                    sunDir = new Vector4(-forward.x, -forward.y, -forward.z, 0f);
                    break;
                }
            }

            float exposure = skyboxMat?.exposure ?? 1.0f;
            float rotation = skyboxMat?.rotation ?? 0.0f;
            float rotationRad = rotation * (MathF.PI / 180f);

            var envUniforms = new EnvMapUniforms
            {
                TextureParams = new Vector4(usePanoramic ? 1f : 0f, exposure, rotationRad, 0f),
                SunDirection = sunDir,
                SkyParams = new Vector4(DefaultZenithIntensity, DefaultHorizonIntensity, DefaultSunAngularRadius, DefaultSunIntensity),
                ZenithColor = DefaultZenithColor,
                HorizonColor = DefaultHorizonColor,
            };

            cl.UpdateBuffer(_envMapBuffer, 0, envUniforms);
        }

        // ==============================
        // Skybox rendering
        // ==============================

        // Default procedural sky parameters
        private static readonly Vector4 DefaultZenithColor = new Vector4(0.15f, 0.3f, 0.65f, 1f);
        private static readonly Vector4 DefaultHorizonColor = new Vector4(0.6f, 0.7f, 0.85f, 1f);
        private const float DefaultZenithIntensity = 0.8f;
        private const float DefaultHorizonIntensity = 1.0f;
        private const float DefaultSunAngularRadius = 0.02f;
        private const float DefaultSunIntensity = 20.0f;

        private void RenderSkybox(CommandList cl, Camera camera, System.Numerics.Matrix4x4 viewProj)
        {
            if (_skyboxPipeline == null || _skyboxResourceSet == null) return;

            // Compute inverse view-projection
            System.Numerics.Matrix4x4.Invert(viewProj, out var invViewProj);

            // Get sun direction from first directional light (or default)
            var sunDir = new Vector4(0.3f, 0.8f, 0.5f, 0f);
            foreach (var light in Light._allLights)
            {
                if (!light.enabled || !light.gameObject.activeInHierarchy) continue;
                if (light.type == LightType.Directional)
                {
                    var forward = light.transform.forward;
                    sunDir = new Vector4(-forward.x, -forward.y, -forward.z, 0f);
                    break;
                }
            }

            // Check for skybox material from RenderSettings
            var skyboxMat = RenderSettings.skybox;
            bool usePanoramic = skyboxMat?.shader?.name == "Skybox/Panoramic" && skyboxMat.mainTexture != null;

            // Update resource set if skybox texture changed
            if (usePanoramic)
            {
                // Lazy cubemap conversion with caching (shared with deferred lighting)
                if (skyboxMat!._cachedCubemap == null || skyboxMat._cachedCubemapSource != skyboxMat.mainTexture)
                {
                    skyboxMat._cachedCubemap?.Dispose();
                    skyboxMat._cachedCubemap = Cubemap.CreateFromEquirectangular(skyboxMat.mainTexture!, 512);
                    skyboxMat._cachedCubemap.UploadToGPU(_device!, generateMipmaps: true);
                    skyboxMat._cachedCubemapSource = skyboxMat.mainTexture;
                }
                var texView = skyboxMat._cachedCubemap.TextureView;
                if (texView != null && texView != _currentSkyboxTextureView)
                {
                    _skyboxResourceSet?.Dispose();
                    _skyboxResourceSet = _device!.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                        _skyboxLayout!, _skyboxUniformBuffer!, texView, _defaultSampler!));
                    _currentSkyboxTextureView = texView;
                }
            }
            else if (_currentSkyboxTextureView != null)
            {
                // Reset to default (white cubemap = procedural mode)
                _skyboxResourceSet?.Dispose();
                _skyboxResourceSet = _device!.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                    _skyboxLayout!, _skyboxUniformBuffer!, _whiteCubemap!.TextureView!, _defaultSampler!));
                _currentSkyboxTextureView = null;
            }

            float exposure = skyboxMat?.exposure ?? 1.0f;
            float rotation = skyboxMat?.rotation ?? 0.0f;
            float rotationRad = rotation * (MathF.PI / 180f);

            var uniforms = new SkyboxUniforms
            {
                InverseViewProjection = invViewProj,
                SunDirection = sunDir,
                SkyParams = new Vector4(DefaultZenithIntensity, DefaultHorizonIntensity, DefaultSunAngularRadius, DefaultSunIntensity),
                ZenithColor = DefaultZenithColor,
                HorizonColor = DefaultHorizonColor,
                TextureParams = new Vector4(usePanoramic ? 1f : 0f, exposure, rotationRad, 0f),
            };

            cl.UpdateBuffer(_skyboxUniformBuffer, 0, uniforms);

            cl.SetPipeline(_skyboxPipeline);
            cl.SetGraphicsResourceSet(0, _skyboxResourceSet);
            cl.Draw(3, 1, 0, 0);
        }

        private Vector4 ComputeSkyAmbientColor()
        {
            var skyboxMat = RenderSettings.skybox;
            float intensity = RenderSettings.ambientIntensity;

            // If panoramic skybox with texture: use cubemap average color
            if (skyboxMat?.shader?.name == "Skybox/Panoramic" && skyboxMat.mainTexture != null)
            {
                var avg = skyboxMat._cachedCubemap?.GetAverageColor() ?? skyboxMat.mainTexture.GetAverageColor();
                float exposure = skyboxMat.exposure;
                return new Vector4(avg.r * exposure * intensity, avg.g * exposure * intensity, avg.b * exposure * intensity, 1f);
            }

            // If any skybox material set with custom ambient color
            if (skyboxMat != null)
            {
                var c = RenderSettings.ambientLight;
                return new Vector4(c.r * intensity, c.g * intensity, c.b * intensity, 1f);
            }

            // Default procedural sky ambient (average of zenith + horizon)
            var skyR = (DefaultZenithColor.X * DefaultZenithIntensity + DefaultHorizonColor.X * DefaultHorizonIntensity) * 0.5f;
            var skyG = (DefaultZenithColor.Y * DefaultZenithIntensity + DefaultHorizonColor.Y * DefaultHorizonIntensity) * 0.5f;
            var skyB = (DefaultZenithColor.Z * DefaultZenithIntensity + DefaultHorizonColor.Z * DefaultHorizonIntensity) * 0.5f;
            return new Vector4(skyR * intensity, skyG * intensity, skyB * intensity, 1f);
        }

        // ==============================
        // Forward light upload (for sprites/text)
        // ==============================

        private const int MaxForwardLights = 8;

        private void UploadForwardLightData(CommandList cl, Camera camera)
        {
            var camPos = camera.transform.position;
            var lightData = new LightUniforms
            {
                CameraPos = new Vector4(camPos.x, camPos.y, camPos.z, 0),
            };

            int count = 0;
            foreach (var light in Light._allLights)
            {
                if (count >= MaxForwardLights) break;
                if (!light.enabled || !light.gameObject.activeInHierarchy) continue;
                SetLightInfo(ref lightData, count++, CollectLightInfo(light));
            }

            lightData.LightCount = count;
            cl.UpdateBuffer(_lightBuffer, 0, lightData);
        }

        private static unsafe void SetLightInfo(ref LightUniforms data, int index, LightInfoGPU info)
        {
            fixed (LightInfoGPU* ptr = &data.Light0)
                ptr[index] = info;
        }

        // ==============================
        // Draw helpers
        // ==============================

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

        /// <summary>공통 draw call — Transform/Material 업로드 + ResourceSet 바인딩 + DrawIndexed.</summary>
        private void DrawMesh(CommandList cl, System.Numerics.Matrix4x4 viewProj,
            Mesh mesh, Transform t, MaterialUniforms matUniforms, TextureView? texView,
            bool bindPerFrame)
        {
            cl.UpdateBuffer(_transformBuffer, 0, new TransformUniforms
            {
                World = RoseEngine.Matrix4x4.TRS(t.position, t.rotation, t.localScale).ToNumerics(),
                ViewProjection = viewProj,
            });
            cl.UpdateBuffer(_materialBuffer, 0, matUniforms);

            cl.SetGraphicsResourceSet(0, GetOrCreateResourceSet(texView));
            if (bindPerFrame)
                cl.SetGraphicsResourceSet(1, _perFrameResourceSet);

            cl.SetVertexBuffer(0, mesh.VertexBuffer);
            cl.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
            cl.DrawIndexed((uint)mesh.indices.Length);
        }

        private (MaterialUniforms mat, TextureView? tex) PrepareMaterial(Material? material)
        {
            var color = material?.color ?? Color.white;
            var emission = material?.emission ?? Color.black;
            TextureView? texView = null;
            float hasTexture = 0f;
            if (material?.mainTexture != null)
            {
                material.mainTexture.UploadToGPU(_device!);
                if (material.mainTexture.TextureView != null)
                { texView = material.mainTexture.TextureView; hasTexture = 1f; }
            }
            return (new MaterialUniforms
            {
                Color = new Vector4(color.r, color.g, color.b, color.a),
                Emission = new Vector4(emission.r, emission.g, emission.b, emission.a),
                HasTexture = hasTexture,
                Metallic = material?.metallic ?? 0f,
                Roughness = material?.roughness ?? 0.5f,
                Occlusion = material?.occlusion ?? 1f,
            }, texView);
        }

        private void DrawOpaqueRenderers(CommandList cl, System.Numerics.Matrix4x4 viewProj)
        {
            DrawOpaqueRenderersDebug(cl, viewProj, false);
        }

        private int DrawOpaqueRenderersDebug(CommandList cl, System.Numerics.Matrix4x4 viewProj, bool debugLog)
        {
            int drawn = 0;
            foreach (var renderer in MeshRenderer._allRenderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter?.mesh == null) continue;
                var mesh = filter.mesh;
                mesh.UploadToGPU(_device!);
                if (mesh.VertexBuffer == null || mesh.IndexBuffer == null) continue;

                var (matUniforms, texView) = PrepareMaterial(renderer.material);
                DrawMesh(cl, viewProj, mesh, renderer.transform, matUniforms, texView, bindPerFrame: false);
                drawn++;

                if (debugLog)
                {
                    var pos = renderer.transform.position;
                    var scale = renderer.transform.localScale;
                    Console.WriteLine($"[Render]   Object '{renderer.gameObject.name}': pos=({pos.x:F2},{pos.y:F2},{pos.z:F2}) scale=({scale.x:F2},{scale.y:F2},{scale.z:F2}) color=({matUniforms.Color.X:F2},{matUniforms.Color.Y:F2},{matUniforms.Color.Z:F2}) metallic={matUniforms.Metallic:F2} roughness={matUniforms.Roughness:F2} verts={mesh.vertices.Length} indices={mesh.indices.Length}");
                }
            }
            return drawn;
        }

        private void DrawAllRenderers(CommandList cl, System.Numerics.Matrix4x4 viewProj, bool useWireframeColor)
        {
            foreach (var renderer in MeshRenderer._allRenderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter?.mesh == null) continue;
                var mesh = filter.mesh;
                mesh.UploadToGPU(_device!);
                if (mesh.VertexBuffer == null || mesh.IndexBuffer == null) continue;

                MaterialUniforms matUniforms;
                TextureView? texView;
                if (useWireframeColor)
                {
                    var wc = Debug.wireframeColor;
                    matUniforms = new MaterialUniforms
                    {
                        Color = new Vector4(wc.r, wc.g, wc.b, wc.a),
                        Roughness = 0.5f, Occlusion = 1f,
                    };
                    texView = null;
                }
                else
                {
                    (matUniforms, texView) = PrepareMaterial(renderer.material);
                }

                DrawMesh(cl, viewProj, mesh, renderer.transform, matUniforms, texView, bindPerFrame: true);
            }
        }

        private void SetUnlitLightData(CommandList cl, Camera camera)
        {
            cl.UpdateBuffer(_lightBuffer, 0, new LightUniforms
            {
                CameraPos = new Vector4(camera.transform.position.x, camera.transform.position.y, camera.transform.position.z, 0),
                LightCount = -1,
            });
        }

        private void DrawAllSprites(CommandList cl, System.Numerics.Matrix4x4 viewProj, Camera camera)
        {
            SetUnlitLightData(cl, camera);
            cl.SetPipeline(_spritePipeline);

            var active = SpriteRenderer._allSpriteRenderers
                .Where(sr => sr.enabled && sr.sprite != null &&
                             sr.gameObject.activeInHierarchy && !sr._isDestroyed)
                .ToList();
            if (active.Count == 0) return;

            var camPos = camera.transform.position;
            active.Sort((a, b) =>
            {
                int orderCmp = a.sortingOrder.CompareTo(b.sortingOrder);
                if (orderCmp != 0) return orderCmp;
                return (b.transform.position - camPos).sqrMagnitude
                    .CompareTo((a.transform.position - camPos).sqrMagnitude);
            });

            foreach (var sr in active)
            {
                sr.EnsureMesh();
                if (sr._cachedMesh == null) continue;
                var mesh = sr._cachedMesh;
                mesh.UploadToGPU(_device!);
                if (mesh.VertexBuffer == null || mesh.IndexBuffer == null) continue;

                TextureView? texView = null;
                float hasTexture = 0f;
                sr.sprite!.texture.UploadToGPU(_device!);
                if (sr.sprite.texture.TextureView != null)
                { texView = sr.sprite.texture.TextureView; hasTexture = 1f; }

                var c = sr.color;
                DrawMesh(cl, viewProj, mesh, sr.transform, new MaterialUniforms
                {
                    Color = new Vector4(c.r, c.g, c.b, c.a),
                    HasTexture = hasTexture,
                }, texView, bindPerFrame: true);
            }
        }

        private void DrawAllTexts(CommandList cl, System.Numerics.Matrix4x4 viewProj, Camera camera)
        {
            SetUnlitLightData(cl, camera);
            cl.SetPipeline(_spritePipeline);

            var active = TextRenderer._allTextRenderers
                .Where(tr => tr.enabled && tr.font?.atlasTexture != null &&
                             !string.IsNullOrEmpty(tr.text) &&
                             tr.gameObject.activeInHierarchy && !tr._isDestroyed)
                .ToList();
            if (active.Count == 0) return;

            var camPos = camera.transform.position;
            active.Sort((a, b) =>
            {
                int orderCmp = a.sortingOrder.CompareTo(b.sortingOrder);
                if (orderCmp != 0) return orderCmp;
                return (b.transform.position - camPos).sqrMagnitude
                    .CompareTo((a.transform.position - camPos).sqrMagnitude);
            });

            foreach (var tr in active)
            {
                tr.EnsureMesh();
                if (tr._cachedMesh == null) continue;
                var mesh = tr._cachedMesh;
                mesh.UploadToGPU(_device!);
                if (mesh.VertexBuffer == null || mesh.IndexBuffer == null) continue;

                TextureView? texView = null;
                float hasTexture = 0f;
                tr.font!.atlasTexture!.UploadToGPU(_device!);
                if (tr.font.atlasTexture.TextureView != null)
                { texView = tr.font.atlasTexture.TextureView; hasTexture = 1f; }

                DrawMesh(cl, viewProj, mesh, tr.transform, new MaterialUniforms
                {
                    Color = new Vector4(tr.color.r, tr.color.g, tr.color.b, tr.color.a),
                    HasTexture = hasTexture,
                }, texView, bindPerFrame: true);
            }
        }

        // ==============================
        // Utilities
        // ==============================

        private static string FindShaderDirectory()
        {
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
            _postProcessStack?.Dispose();

            _geometryPipeline?.Dispose();
            _ambientPipeline?.Dispose();
            _directionalLightPipeline?.Dispose();
            _pointLightPipeline?.Dispose();
            _skyboxPipeline?.Dispose();
            _forwardPipeline?.Dispose();
            _wireframePipeline?.Dispose();
            _spritePipeline?.Dispose();

            _skyboxResourceSet?.Dispose();
            _skyboxLayout?.Dispose();
            _skyboxUniformBuffer?.Dispose();

            _gBufferResourceSet?.Dispose();
            _ambientResourceSet?.Dispose();
            _lightVolumeResourceSet?.Dispose();

            _gBufferLayout?.Dispose();
            _ambientLayout?.Dispose();
            _lightVolumeLayout?.Dispose();

            _ambientBuffer?.Dispose();
            _lightVolumeBuffer?.Dispose();
            _envMapBuffer?.Dispose();

            _hdrView?.Dispose();
            _hdrFramebuffer?.Dispose();
            _hdrTexture?.Dispose();

            _gBuffer?.Dispose();

            _transformBuffer?.Dispose();
            _materialBuffer?.Dispose();
            _lightBuffer?.Dispose();
            _defaultSampler?.Dispose();
            _whiteTexture?.Dispose();
            _whiteCubemap?.Dispose();
            _defaultResourceSet?.Dispose();
            _perFrameResourceSet?.Dispose();
            _perObjectLayout?.Dispose();
            _perFrameLayout?.Dispose();

            foreach (var rs in _resourceSetCache.Values)
                rs.Dispose();
            _resourceSetCache.Clear();

            if (_forwardShaders != null)
                foreach (var s in _forwardShaders) s.Dispose();
            if (_geometryShaders != null)
                foreach (var s in _geometryShaders) s.Dispose();
            if (_ambientShaders != null)
                foreach (var s in _ambientShaders) s.Dispose();
            if (_directionalLightShaders != null)
                foreach (var s in _directionalLightShaders) s.Dispose();
            if (_pointLightShaders != null)
                foreach (var s in _pointLightShaders) s.Dispose();
            if (_skyboxShaders != null)
                foreach (var s in _skyboxShaders) s.Dispose();

            Console.WriteLine("[RenderSystem] Disposed");
        }
    }
}
