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
        public Vector4 PositionOrDirection; // xyz = pos/dir, w = type (0=dir, 1=point, 2=spot)
        public Vector4 ColorIntensity;      // rgb = color, a = intensity
        public Vector4 Params;              // x = range, y = cosInnerAngle (spot), z = cosOuterAngle (spot)
        public Vector4 SpotDirection;       // xyz = spot forward direction (spot only)
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
        public System.Numerics.Matrix4x4 LightViewProjection;  // 64 bytes (shadow map VP)
        public Vector4 CameraPos;                               // 16 bytes
        public Vector4 ScreenParams;                            // 16 bytes (x=width, y=height)
        public Vector4 ShadowParams;                            // 16 bytes (x=hasShadow, y=bias, z=normalBias, w=strength)
        public Vector4 ShadowAtlasParams;                       // 16 bytes (xy=tileOffset, zw=tileScale)
        public LightInfoGPU Light;                              // 64 bytes
        // Point light face data (6 faces for cubemap → atlas mapping):
        public System.Numerics.Matrix4x4 FaceVP0;              // 64 bytes
        public System.Numerics.Matrix4x4 FaceVP1;              // 64 bytes
        public System.Numerics.Matrix4x4 FaceVP2;              // 64 bytes
        public System.Numerics.Matrix4x4 FaceVP3;              // 64 bytes
        public System.Numerics.Matrix4x4 FaceVP4;              // 64 bytes
        public System.Numerics.Matrix4x4 FaceVP5;              // 64 bytes
        public Vector4 FaceAtlasParams0;                        // 16 bytes
        public Vector4 FaceAtlasParams1;                        // 16 bytes
        public Vector4 FaceAtlasParams2;                        // 16 bytes
        public Vector4 FaceAtlasParams3;                        // 16 bytes
        public Vector4 FaceAtlasParams4;                        // 16 bytes
        public Vector4 FaceAtlasParams5;                        // 16 bytes
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ShadowTransformUniforms
    {
        public System.Numerics.Matrix4x4 LightMVP;  // 64 bytes
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DebugOverlayParamsGPU
    {
        public float Mode;
        public float _pad1;
        public float _pad2;
        public float _pad3;
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
        private Veldrid.Shader[]? _spotLightShaders;

        private Pipeline? _ambientPipeline;
        private Pipeline? _directionalLightPipeline;
        private Pipeline? _pointLightPipeline;
        private Pipeline? _spotLightPipeline;

        private ResourceLayout? _gBufferLayout;
        private ResourceLayout? _ambientLayout;
        private ResourceLayout? _lightVolumeShadowLayout; // All light types (UBO + Texture2D + Sampler)

        private ResourceSet? _gBufferResourceSet;
        private ResourceSet? _ambientResourceSet;
        private ResourceSet? _atlasShadowSet;             // Unified atlas resource set for all lights

        private DeviceBuffer? _ambientBuffer;
        private DeviceBuffer? _lightVolumeBuffer;
        private DeviceBuffer? _envMapBuffer;

        // --- Shadow atlas ---
        private const int AtlasSize = 4096;
        private Veldrid.Shader[]? _shadowAtlasShaders;
        private Pipeline? _shadowAtlasPipeline;
        private Pipeline? _shadowAtlasFrontCullPipeline;
        private ResourceLayout? _shadowLayout;
        private DeviceBuffer? _shadowTransformBuffer;
        private Sampler? _shadowSampler;
        private Texture? _atlasTexture;
        private Texture? _atlasDepthTexture;
        private TextureView? _atlasView;
        private Framebuffer? _atlasFramebuffer;

        // Per-frame shadow tile info (rebuilt each shadow pass)
        private struct FrameShadowTile
        {
            public System.Numerics.Matrix4x4 LightVP;
            public Vector4 AtlasParams;          // xy=offset, zw=scale
            public System.Numerics.Matrix4x4[]? FaceVPs;         // Point: 6
            public Vector4[]? FaceAtlasParams;   // Point: 6
        }
        private readonly Dictionary<Light, FrameShadowTile> _frameShadows = new();

        // Atlas tile allocator state
        private int _atlasPackX, _atlasPackY, _atlasRowHeight;

        private Mesh? _lightSphereMesh;
        private Mesh? _lightConeMesh;
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

        // --- Debug overlay ---
        private Veldrid.Shader[]? _debugOverlayShaders;
        private Pipeline? _debugOverlayPipeline;
        private ResourceLayout? _debugOverlayLayout;
        private DeviceBuffer? _debugOverlayParamsBuffer;
        private Sampler? _debugOverlaySampler;


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

            _spotLightShaders = ShaderCompiler.CompileGLSL(device,
                Path.Combine(_shaderDir, "deferred_spotlight.vert"),
                Path.Combine(_shaderDir, "deferred_spotlight.frag"));

            _shadowAtlasShaders = ShaderCompiler.CompileGLSL(device,
                Path.Combine(_shaderDir, "shadow.vert"),
                Path.Combine(_shaderDir, "shadow_atlas.frag"));

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

            // --- Shadow sampler (clamp to border white, for outside-frustum reads) ---
            _shadowSampler = factory.CreateSampler(new SamplerDescription(
                SamplerAddressMode.Border, SamplerAddressMode.Border, SamplerAddressMode.Border,
                SamplerFilter.MinLinear_MagLinear_MipLinear,
                null, 0, 0, 0, 0, SamplerBorderColor.OpaqueWhite));

            // --- Shadow transform buffer ---
            _shadowTransformBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<ShadowTransformUniforms>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

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

            // Light volume shadow layout (set 1, all light types — UBO + shadow map + sampler)
            _lightVolumeShadowLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("LightVolumeData", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ShadowMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ShadowSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            // Shadow pass layout (set 0): just the MVP transform
            _shadowLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ShadowTransforms", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

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

            // --- Shadow Atlas (single R32_Float texture for all shadow maps) ---
            _atlasTexture = factory.CreateTexture(TextureDescription.Texture2D(
                (uint)AtlasSize, (uint)AtlasSize, 1, 1, PixelFormat.R32_Float,
                TextureUsage.RenderTarget | TextureUsage.Sampled));
            _atlasDepthTexture = factory.CreateTexture(TextureDescription.Texture2D(
                (uint)AtlasSize, (uint)AtlasSize, 1, 1, PixelFormat.D32_Float_S8_UInt,
                TextureUsage.DepthStencil));
            _atlasView = factory.CreateTextureView(_atlasTexture);
            _atlasFramebuffer = factory.CreateFramebuffer(new FramebufferDescription(
                _atlasDepthTexture, _atlasTexture));

            // --- Unified atlas shadow resource set (for all light types) ---
            _atlasShadowSet = factory.CreateResourceSet(new ResourceSetDescription(
                _lightVolumeShadowLayout, _lightVolumeBuffer, _atlasView, _shadowSampler));

            // --- Light volume meshes ---
            _lightSphereMesh = PrimitiveGenerator.CreateSphere(12, 8);
            _lightSphereMesh.UploadToGPU(device);

            _lightConeMesh = PrimitiveGenerator.CreateCone(16);
            _lightConeMesh.UploadToGPU(device);

            // --- Create size-dependent resources (GBuffer, HDR, pipelines) ---
            CreateSizeDependentResources(width, height);

            // --- PostProcessing Stack ---
            _postProcessStack = new PostProcessStack();
            _postProcessStack.Initialize(device, width, height, _shaderDir);
            _postProcessStack.AddEffect(new BloomEffect());
            _postProcessStack.AddEffect(new TonemapEffect());

            // --- Debug Overlay ---
            _debugOverlayShaders = ShaderCompiler.CompileGLSL(device,
                Path.Combine(_shaderDir, "fullscreen.vert"),
                Path.Combine(_shaderDir, "debug_overlay.frag"));

            _debugOverlayParamsBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<DebugOverlayParamsGPU>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            _debugOverlayLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("SourceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("DebugParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            _debugOverlaySampler = factory.CreateSampler(new SamplerDescription(
                SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
                SamplerFilter.MinLinear_MagLinear_MipLinear,
                null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));

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
            _spotLightPipeline?.Dispose();
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

            // --- Shadow Atlas Pipeline (R32_Float color + depth, for all shadow types) ---
            _shadowAtlasPipeline?.Dispose();
            _shadowAtlasPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true, depthWriteEnabled: true, comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.Back, fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _shadowLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: new[] { vertexLayout },
                    shaders: _shadowAtlasShaders!),
                Outputs = _atlasFramebuffer!.OutputDescription,
            });

            // --- Shadow Atlas Pipeline (front-face cull variant — renders back faces for natural bias) ---
            _shadowAtlasFrontCullPipeline?.Dispose();
            _shadowAtlasFrontCullPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true, depthWriteEnabled: true, comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.Front, fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _shadowLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: new[] { vertexLayout },
                    shaders: _shadowAtlasShaders!),
                Outputs = _atlasFramebuffer!.OutputDescription,
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
                ResourceLayouts = new[] { _gBufferLayout!, _lightVolumeShadowLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: Array.Empty<VertexLayoutDescription>(),
                    shaders: _directionalLightShaders!),
                Outputs = _hdrFramebuffer.OutputDescription,
            });

            // --- Point Light Pipeline (→ HDR, sphere mesh, additive, cubemap shadow) ---
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
                ResourceLayouts = new[] { _gBufferLayout!, _lightVolumeShadowLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: new[] { vertexLayout },
                    shaders: _pointLightShaders!),
                Outputs = _hdrFramebuffer.OutputDescription,
            });

            // --- Spot Light Pipeline (→ HDR, cone mesh, additive, same depth as point) ---
            _spotLightPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = additiveBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true, depthWriteEnabled: false, comparisonKind: ComparisonKind.GreaterEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.Back, fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _gBufferLayout!, _lightVolumeShadowLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: new[] { vertexLayout },
                    shaders: _spotLightShaders!),
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

            // --- Debug Overlay Pipeline (→ Swapchain, overwrite) ---
            _debugOverlayPipeline?.Dispose();
            if (_debugOverlayShaders != null && _debugOverlayLayout != null)
            {
                _debugOverlayPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
                {
                    BlendState = BlendStateDescription.SingleOverrideBlend,
                    DepthStencilState = DepthStencilStateDescription.Disabled,
                    RasterizerState = new RasterizerStateDescription(
                        cullMode: FaceCullMode.None, fillMode: PolygonFillMode.Solid,
                        frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: true),
                    PrimitiveTopology = PrimitiveTopology.TriangleList,
                    ResourceLayouts = new[] { _debugOverlayLayout },
                    ShaderSet = new ShaderSetDescription(
                        vertexLayouts: Array.Empty<VertexLayoutDescription>(),
                        shaders: _debugOverlayShaders),
                    Outputs = _device.SwapchainFramebuffer.OutputDescription,
                });
            }

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

            // === 1.5 Shadow Pass ===
            RenderShadowPass(cl, camera);

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
            // Restore viewport to HDR size after shadow pass
            cl.SetFramebuffer(_hdrFramebuffer);
            cl.SetFullViewports();

            int dirCount = 0, pointCount = 0, spotCount = 0;
            foreach (var light in Light._allLights)
            {
                if (!light.enabled || !light.gameObject.activeInHierarchy) continue;

                UploadSingleLightUniforms(cl, light, camera, viewProj);
                var lightSet = GetLightVolumeResourceSet(light);

                if (light.type == LightType.Directional)
                {
                    cl.SetPipeline(_directionalLightPipeline);
                    cl.SetGraphicsResourceSet(0, _gBufferResourceSet);
                    cl.SetGraphicsResourceSet(1, lightSet);
                    cl.Draw(3, 1, 0, 0);
                    dirCount++;

                    if (debugLog)
                    {
                        var fwd = light.transform.forward;
                        Console.WriteLine($"[Render] DirectionalLight: dir=({fwd.x:F2},{fwd.y:F2},{fwd.z:F2}) color=({light.color.r:F2},{light.color.g:F2},{light.color.b:F2}) intensity={light.intensity:F2} shadow={light.shadows}");
                    }
                }
                else if (light.type == LightType.Point)
                {
                    cl.SetPipeline(_pointLightPipeline);
                    cl.SetGraphicsResourceSet(0, _gBufferResourceSet);
                    cl.SetGraphicsResourceSet(1, lightSet);
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
                else if (light.type == LightType.Spot)
                {
                    cl.SetPipeline(_spotLightPipeline);
                    cl.SetGraphicsResourceSet(0, _gBufferResourceSet);
                    cl.SetGraphicsResourceSet(1, lightSet);
                    cl.SetVertexBuffer(0, _lightConeMesh!.VertexBuffer);
                    cl.SetIndexBuffer(_lightConeMesh.IndexBuffer!, IndexFormat.UInt32);
                    cl.DrawIndexed((uint)_lightConeMesh.indices.Length);
                    spotCount++;

                    if (debugLog)
                    {
                        var pos = light.transform.position;
                        Console.WriteLine($"[Render] SpotLight: pos=({pos.x:F2},{pos.y:F2},{pos.z:F2}) range={light.range:F1} innerAngle={light.spotAngle:F1} outerAngle={light.spotOuterAngle:F1} color=({light.color.r:F2},{light.color.g:F2},{light.color.b:F2}) intensity={light.intensity:F2} shadow={light.shadows}");
                    }
                }
            }
            if (debugLog)
                Console.WriteLine($"[Render] LightPass: {dirCount} directional, {pointCount} point, {spotCount} spot lights");

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

            // === 7. Debug Overlay → Swapchain ===
            if (Debug.overlay != DebugOverlay.None)
                RenderDebugOverlay(cl);
        }

        // ==============================
        // Debug Overlay
        // ==============================

        private void RenderDebugOverlay(CommandList cl)
        {
            if (_debugOverlayPipeline == null || _debugOverlayLayout == null ||
                _debugOverlayParamsBuffer == null || _debugOverlaySampler == null ||
                _device == null || _gBuffer == null)
                return;

            var factory = _device.ResourceFactory;
            var swapFB = _device.SwapchainFramebuffer;
            uint screenW = swapFB.Width;
            uint screenH = swapFB.Height;

            cl.SetFramebuffer(swapFB);
            cl.SetPipeline(_debugOverlayPipeline);

            if (Debug.overlay == DebugOverlay.GBuffer)
            {
                // 4 thumbnails at screen bottom, each 25% width x 25% height
                uint thumbH = screenH / 4;
                uint thumbW = screenW / 4;
                uint thumbY = screenH - thumbH;

                var textures = new (TextureView view, float mode)[]
                {
                    (_gBuffer.AlbedoView, 0f),
                    (_gBuffer.NormalView, 1f),
                    (_gBuffer.MaterialView, 2f),
                    (_gBuffer.WorldPosView, 3f),
                };

                for (int i = 0; i < textures.Length; i++)
                {
                    uint thumbX = (uint)i * thumbW;

                    using var resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                        _debugOverlayLayout, textures[i].view, _debugOverlaySampler, _debugOverlayParamsBuffer));

                    cl.UpdateBuffer(_debugOverlayParamsBuffer, 0, new DebugOverlayParamsGPU { Mode = textures[i].mode });
                    cl.SetViewport(0, new Viewport(thumbX, thumbY, thumbW, thumbH, 0f, 1f));
                    cl.SetScissorRect(0, thumbX, thumbY, thumbW, thumbH);
                    cl.SetGraphicsResourceSet(0, resourceSet);
                    cl.Draw(3, 1, 0, 0);
                }
            }
            else if (Debug.overlay == DebugOverlay.ShadowMap)
            {
                // Shadow atlas thumbnail at bottom-left, 30% screen height (square)
                if (_atlasView == null) return;

                uint thumbSize = (uint)(screenH * 0.3f);

                using var resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                    _debugOverlayLayout, _atlasView, _debugOverlaySampler, _debugOverlayParamsBuffer));

                cl.UpdateBuffer(_debugOverlayParamsBuffer, 0, new DebugOverlayParamsGPU { Mode = 4f });
                cl.SetViewport(0, new Viewport(0, screenH - thumbSize, thumbSize, thumbSize, 0f, 1f));
                cl.SetScissorRect(0, 0, screenH - thumbSize, thumbSize, thumbSize);
                cl.SetGraphicsResourceSet(0, resourceSet);
                cl.Draw(3, 1, 0, 0);
            }

            // Restore full viewport
            cl.SetFullViewports();
            cl.SetFullScissorRects();
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
                info.PositionOrDirection = new Vector4(pos.x, pos.y, pos.z, (float)light.type);
            }
            info.ColorIntensity = new Vector4(light.color.r, light.color.g, light.color.b, light.intensity);

            if (light.type == LightType.Spot)
            {
                float cosInner = MathF.Cos(light.spotAngle * 0.5f * MathF.PI / 180f);
                float cosOuter = MathF.Cos(light.spotOuterAngle * 0.5f * MathF.PI / 180f);
                info.Params = new Vector4(light.range, cosInner, cosOuter, 0);
                var fwd = light.transform.forward;
                info.SpotDirection = new Vector4(fwd.x, fwd.y, fwd.z, 0);
            }
            else
            {
                info.Params = new Vector4(light.range, 0, 0, 0);
            }
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
            else if (light.type == LightType.Spot)
            {
                var pos = light.transform.position;
                float height = light.range;
                float halfAngle = light.spotOuterAngle * 0.5f * MathF.PI / 180f;
                float baseRadius = height * MathF.Tan(halfAngle);
                var rotation = RoseEngine.Quaternion.FromToRotation(
                    RoseEngine.Vector3.forward, light.transform.forward);
                var world = RoseEngine.Matrix4x4.TRS(
                    new RoseEngine.Vector3(pos.x, pos.y, pos.z),
                    rotation,
                    new RoseEngine.Vector3(baseRadius, baseRadius, height)).ToNumerics();
                mvp = world * viewProj;
            }
            else
            {
                mvp = System.Numerics.Matrix4x4.Identity;
            }

            // Shadow atlas params
            var lightVP = System.Numerics.Matrix4x4.Identity;
            var shadowParams = Vector4.Zero; // x=0 means no shadow
            var atlasParams = Vector4.Zero;

            var uniforms = new LightVolumeUniforms
            {
                WorldViewProjection = mvp,
                CameraPos = new Vector4(camPos.x, camPos.y, camPos.z, 0),
                ScreenParams = new Vector4(_gBuffer!.Width, _gBuffer.Height, 0, 0),
                Light = lightInfo,
            };

            if (light.shadows && _frameShadows.TryGetValue(light, out var shadowTile))
            {
                shadowParams = new Vector4(1f, light.shadowBias, light.shadowNormalBias, 1f);

                if (light.type == LightType.Point && shadowTile.FaceVPs != null && shadowTile.FaceAtlasParams != null)
                {
                    uniforms.FaceVP0 = shadowTile.FaceVPs[0];
                    uniforms.FaceVP1 = shadowTile.FaceVPs[1];
                    uniforms.FaceVP2 = shadowTile.FaceVPs[2];
                    uniforms.FaceVP3 = shadowTile.FaceVPs[3];
                    uniforms.FaceVP4 = shadowTile.FaceVPs[4];
                    uniforms.FaceVP5 = shadowTile.FaceVPs[5];
                    uniforms.FaceAtlasParams0 = shadowTile.FaceAtlasParams[0];
                    uniforms.FaceAtlasParams1 = shadowTile.FaceAtlasParams[1];
                    uniforms.FaceAtlasParams2 = shadowTile.FaceAtlasParams[2];
                    uniforms.FaceAtlasParams3 = shadowTile.FaceAtlasParams[3];
                    uniforms.FaceAtlasParams4 = shadowTile.FaceAtlasParams[4];
                    uniforms.FaceAtlasParams5 = shadowTile.FaceAtlasParams[5];
                }
                else
                {
                    lightVP = shadowTile.LightVP;
                    atlasParams = shadowTile.AtlasParams;
                }
            }

            uniforms.LightViewProjection = lightVP;
            uniforms.ShadowParams = shadowParams;
            uniforms.ShadowAtlasParams = atlasParams;

            cl.UpdateBuffer(_lightVolumeBuffer, 0, uniforms);
        }

        // ==============================
        // Shadow mapping
        // ==============================

        private static readonly System.Numerics.Vector3[] _cubeFaceTargets =
        {
            System.Numerics.Vector3.UnitX,   // +X
            -System.Numerics.Vector3.UnitX,  // -X
            System.Numerics.Vector3.UnitY,   // +Y
            -System.Numerics.Vector3.UnitY,  // -Y
            System.Numerics.Vector3.UnitZ,   // +Z
            -System.Numerics.Vector3.UnitZ,  // -Z
        };
        private static readonly System.Numerics.Vector3[] _cubeFaceUps =
        {
            -System.Numerics.Vector3.UnitY,  // +X
            -System.Numerics.Vector3.UnitY,  // -X
            System.Numerics.Vector3.UnitZ,   // +Y
            -System.Numerics.Vector3.UnitZ,  // -Y
            -System.Numerics.Vector3.UnitY,  // +Z
            -System.Numerics.Vector3.UnitY,  // -Z
        };

        private void ComputeShadowVP(Light light, Camera camera, out System.Numerics.Matrix4x4 lightVP)
        {
            if (light.type == LightType.Directional)
            {
                var camPos = camera.transform.position;
                var lightDir = light.transform.forward;
                float shadowRange = 20f;
                var eye = new System.Numerics.Vector3(
                    camPos.x - lightDir.x * shadowRange,
                    camPos.y - lightDir.y * shadowRange,
                    camPos.z - lightDir.z * shadowRange);
                var target = new System.Numerics.Vector3(camPos.x, camPos.y, camPos.z);
                var lightView = System.Numerics.Matrix4x4.CreateLookAt(eye, target, System.Numerics.Vector3.UnitY);
                var lightProj = System.Numerics.Matrix4x4.CreateOrthographic(
                    shadowRange * 2, shadowRange * 2, 0.1f, shadowRange * 2);
                lightVP = lightView * lightProj;
            }
            else if (light.type == LightType.Spot)
            {
                var pos = light.transform.position;
                var fwd = light.transform.forward;
                var eye = new System.Numerics.Vector3(pos.x, pos.y, pos.z);
                var target = new System.Numerics.Vector3(pos.x + fwd.x, pos.y + fwd.y, pos.z + fwd.z);
                var lightView = System.Numerics.Matrix4x4.CreateLookAt(eye, target, System.Numerics.Vector3.UnitY);
                var lightProj = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView(
                    light.spotOuterAngle * MathF.PI / 180f, 1f, light.shadowNearPlane, light.range);
                lightVP = lightView * lightProj;
            }
            else
            {
                lightVP = System.Numerics.Matrix4x4.Identity;
            }
        }

        private void RenderShadowPass(CommandList cl, Camera camera)
        {
            if (_shadowAtlasPipeline == null || _shadowLayout == null) return;

            _frameShadows.Clear();
            _atlasPackX = 0;
            _atlasPackY = 0;
            _atlasRowHeight = 0;

            // Check if any shadow lights exist
            bool hasShadowLights = false;
            foreach (var light in Light._allLights)
            {
                if (light.enabled && light.shadows)
                { hasShadowLights = true; break; }
            }
            if (!hasShadowLights) return;

            // Clear atlas
            cl.SetFramebuffer(_atlasFramebuffer);
            cl.ClearColorTarget(0, new RgbaFloat(1f, 1f, 1f, 1f)); // white = max depth = no shadow
            cl.ClearDepthStencil(1f);
            var shadowResourceSet = _device!.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                _shadowLayout, _shadowTransformBuffer));

            foreach (var light in Light._allLights)
            {
                if (!light.enabled || !light.shadows) continue;

                // Per-light cull mode: front-face cull renders back faces for natural shadow bias
                cl.SetPipeline(light.shadowCullFront ? _shadowAtlasFrontCullPipeline! : _shadowAtlasPipeline);

                if (light.type == LightType.Point)
                {
                    RenderPointShadowToAtlas(cl, light, shadowResourceSet);
                }
                else
                {
                    RenderDirSpotShadowToAtlas(cl, light, camera, shadowResourceSet);
                }
            }

            shadowResourceSet.Dispose();
        }

        private bool AllocateAtlasTile(int size, out int tileX, out int tileY)
        {
            if (_atlasPackX + size > AtlasSize)
            {
                _atlasPackX = 0;
                _atlasPackY += _atlasRowHeight;
                _atlasRowHeight = 0;
            }
            if (_atlasPackY + size > AtlasSize)
            {
                tileX = 0;
                tileY = 0;
                return false;
            }
            tileX = _atlasPackX;
            tileY = _atlasPackY;
            _atlasPackX += size;
            _atlasRowHeight = Math.Max(_atlasRowHeight, size);
            return true;
        }

        private static Vector4 ComputeAtlasParams(int tileX, int tileY, int tileSize)
        {
            return new Vector4(
                (float)tileX / AtlasSize, (float)tileY / AtlasSize,
                (float)tileSize / AtlasSize, (float)tileSize / AtlasSize);
        }

        private void RenderDirSpotShadowToAtlas(CommandList cl, Light light, Camera camera, ResourceSet shadowResourceSet)
        {
            int res = light.shadowResolution;
            if (!AllocateAtlasTile(res, out int tileX, out int tileY))
                return; // Atlas full — skip this light's shadow

            ComputeShadowVP(light, camera, out var lightVP);
            var atlasParams = ComputeAtlasParams(tileX, tileY, res);

            cl.SetViewport(0, new Viewport((uint)tileX, (uint)tileY, (uint)res, (uint)res, 0, 1));
            cl.SetScissorRect(0, (uint)tileX, (uint)tileY, (uint)res, (uint)res);

            foreach (var renderer in MeshRenderer._allRenderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter?.mesh == null) continue;
                var mesh = filter.mesh;
                if (mesh.VertexBuffer == null || mesh.IndexBuffer == null) continue;

                var t = renderer.transform;
                var world = RoseEngine.Matrix4x4.TRS(t.position, t.rotation, t.localScale).ToNumerics();
                var mvp = world * lightVP;

                cl.UpdateBuffer(_shadowTransformBuffer, 0, new ShadowTransformUniforms { LightMVP = mvp });
                cl.SetGraphicsResourceSet(0, shadowResourceSet);
                cl.SetVertexBuffer(0, mesh.VertexBuffer);
                cl.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
                cl.DrawIndexed((uint)mesh.indices.Length);
            }

            _frameShadows[light] = new FrameShadowTile
            {
                LightVP = lightVP,
                AtlasParams = atlasParams,
            };
        }

        private void RenderPointShadowToAtlas(CommandList cl, Light light, ResourceSet shadowResourceSet)
        {
            int res = light.shadowResolution;
            var faceVPs = new System.Numerics.Matrix4x4[6];
            var faceAtlasParams = new Vector4[6];

            // Allocate 6 tiles
            for (int face = 0; face < 6; face++)
            {
                if (!AllocateAtlasTile(res, out int tileX, out int tileY))
                    return; // Atlas full — skip this light entirely
                faceAtlasParams[face] = ComputeAtlasParams(tileX, tileY, res);
            }

            // Compute 6 face VPs
            var pos = light.transform.position;
            var eye = new System.Numerics.Vector3(pos.x, pos.y, pos.z);
            var faceProj = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 2f, 1f, light.shadowNearPlane, light.range);

            for (int face = 0; face < 6; face++)
            {
                var faceView = System.Numerics.Matrix4x4.CreateLookAt(
                    eye, eye + _cubeFaceTargets[face], _cubeFaceUps[face]);
                faceVPs[face] = faceView * faceProj;
            }

            // Render each face into its atlas tile
            for (int face = 0; face < 6; face++)
            {
                var ap = faceAtlasParams[face];
                int tileX = (int)(ap.X * AtlasSize);
                int tileY = (int)(ap.Y * AtlasSize);

                cl.SetViewport(0, new Viewport((uint)tileX, (uint)tileY, (uint)res, (uint)res, 0, 1));
                cl.SetScissorRect(0, (uint)tileX, (uint)tileY, (uint)res, (uint)res);

                foreach (var renderer in MeshRenderer._allRenderers)
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter?.mesh == null) continue;
                    var mesh = filter.mesh;
                    if (mesh.VertexBuffer == null || mesh.IndexBuffer == null) continue;

                    var t = renderer.transform;
                    var world = RoseEngine.Matrix4x4.TRS(t.position, t.rotation, t.localScale).ToNumerics();
                    var mvp = world * faceVPs[face];

                    cl.UpdateBuffer(_shadowTransformBuffer, 0, new ShadowTransformUniforms { LightMVP = mvp });
                    cl.SetGraphicsResourceSet(0, shadowResourceSet);
                    cl.SetVertexBuffer(0, mesh.VertexBuffer);
                    cl.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
                    cl.DrawIndexed((uint)mesh.indices.Length);
                }
            }

            _frameShadows[light] = new FrameShadowTile
            {
                FaceVPs = faceVPs,
                FaceAtlasParams = faceAtlasParams,
            };
        }

        private ResourceSet GetLightVolumeResourceSet(Light light)
        {
            return _atlasShadowSet!;
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
                SkyParams = new Vector4(SkyZenithIntensity, SkyHorizonIntensity, DefaultSunAngularRadius, RenderSettings.sunIntensity),
                ZenithColor = SkyZenithColor,
                HorizonColor = SkyHorizonColor,
            };

            cl.UpdateBuffer(_envMapBuffer, 0, envUniforms);
        }

        // ==============================
        // Skybox rendering
        // ==============================

        // Procedural sky parameters (read from RenderSettings)
        private static Vector4 SkyZenithColor
        {
            get { var c = RenderSettings.skyZenithColor; return new Vector4(c.r, c.g, c.b, 1f); }
        }
        private static Vector4 SkyHorizonColor
        {
            get { var c = RenderSettings.skyHorizonColor; return new Vector4(c.r, c.g, c.b, 1f); }
        }
        private static float SkyZenithIntensity => RenderSettings.skyZenithIntensity;
        private static float SkyHorizonIntensity => RenderSettings.skyHorizonIntensity;
        private const float DefaultSunAngularRadius = 0.02f;

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
                SkyParams = new Vector4(SkyZenithIntensity, SkyHorizonIntensity, DefaultSunAngularRadius, RenderSettings.sunIntensity),
                ZenithColor = SkyZenithColor,
                HorizonColor = SkyHorizonColor,
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
                return new Vector4(avg.r * exposure * intensity, avg.g * exposure * intensity, avg.b * exposure * intensity, intensity);
            }

            // If any skybox material set with custom ambient color
            if (skyboxMat != null)
            {
                var c = RenderSettings.ambientLight;
                return new Vector4(c.r * intensity, c.g * intensity, c.b * intensity, intensity);
            }

            // Default procedural sky ambient (average of zenith + horizon)
            var skyR = (SkyZenithColor.X * SkyZenithIntensity + SkyHorizonColor.X * SkyHorizonIntensity) * 0.5f;
            var skyG = (SkyZenithColor.Y * SkyZenithIntensity + SkyHorizonColor.Y * SkyHorizonIntensity) * 0.5f;
            var skyB = (SkyZenithColor.Z * SkyZenithIntensity + SkyHorizonColor.Z * SkyHorizonIntensity) * 0.5f;
            return new Vector4(skyR * intensity, skyG * intensity, skyB * intensity, intensity);
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
            _spotLightPipeline?.Dispose();
            _shadowAtlasPipeline?.Dispose();
            _shadowAtlasFrontCullPipeline?.Dispose();
            _skyboxPipeline?.Dispose();
            _forwardPipeline?.Dispose();
            _wireframePipeline?.Dispose();
            _spritePipeline?.Dispose();

            // Shadow atlas
            _atlasShadowSet?.Dispose();
            _atlasView?.Dispose();
            _atlasFramebuffer?.Dispose();
            _atlasTexture?.Dispose();
            _atlasDepthTexture?.Dispose();
            _shadowSampler?.Dispose();
            _shadowTransformBuffer?.Dispose();
            _shadowLayout?.Dispose();

            _skyboxResourceSet?.Dispose();
            _skyboxLayout?.Dispose();
            _skyboxUniformBuffer?.Dispose();

            _gBufferResourceSet?.Dispose();
            _ambientResourceSet?.Dispose();

            _gBufferLayout?.Dispose();
            _ambientLayout?.Dispose();
            _lightVolumeShadowLayout?.Dispose();

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

            _debugOverlayPipeline?.Dispose();
            _debugOverlayParamsBuffer?.Dispose();
            _debugOverlayLayout?.Dispose();
            _debugOverlaySampler?.Dispose();
            if (_debugOverlayShaders != null)
                foreach (var s in _debugOverlayShaders) s.Dispose();

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
            if (_spotLightShaders != null)
                foreach (var s in _spotLightShaders) s.Dispose();
            if (_shadowAtlasShaders != null)
                foreach (var s in _shadowAtlasShaders) s.Dispose();
            if (_skyboxShaders != null)
                foreach (var s in _skyboxShaders) s.Dispose();

            Console.WriteLine("[RenderSystem] Disposed");
        }
    }
}
