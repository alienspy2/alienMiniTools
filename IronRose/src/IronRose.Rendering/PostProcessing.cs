using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace IronRose.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct BloomParamsGPU
    {
        public float Threshold;
        public float SoftKnee;
        public float _pad1;
        public float _pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BlurParamsGPU
    {
        public Vector2 Direction;
        public float _pad1;
        public float _pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TonemapParamsGPU
    {
        public float BloomIntensity;
        public float Exposure;
        public float _pad1;
        public float _pad2;
    }

    public class PostProcessing : IDisposable
    {
        private GraphicsDevice _device = null!;

        // Pipelines
        private Pipeline? _bloomThresholdPipeline;
        private Pipeline? _blurPipeline;
        private Pipeline? _compositePipeline;

        // Resource layouts
        private ResourceLayout? _bloomThresholdLayout;
        private ResourceLayout? _blurLayout;
        private ResourceLayout? _compositeLayout;

        // Textures (half resolution)
        private Texture? _bloomHalf;
        private TextureView? _bloomHalfView;
        private Framebuffer? _bloomHalfFB;

        private Texture? _blurPingPong;
        private TextureView? _blurPingPongView;
        private Framebuffer? _blurPingPongFB;

        // Uniform buffers
        private DeviceBuffer? _bloomParamsBuffer;
        private DeviceBuffer? _blurParamsBuffer;
        private DeviceBuffer? _tonemapParamsBuffer;

        // Sampler
        private Sampler? _linearSampler;

        // Resource sets
        private ResourceSet? _bloomThresholdSet;
        private ResourceSet? _blurHSet;
        private ResourceSet? _blurVSet;
        private ResourceSet? _compositeSet;

        // Shaders
        private Shader[]? _bloomThresholdShaders;
        private Shader[]? _blurShaders;
        private Shader[]? _compositeShaders;

        public float BloomThreshold { get; set; } = 1.0f;
        public float BloomSoftKnee { get; set; } = 0.5f;
        public float BloomIntensity { get; set; } = 0.5f;
        public float Exposure { get; set; } = 1.0f;

        private uint _halfWidth;
        private uint _halfHeight;

        public void Initialize(GraphicsDevice device, uint width, uint height, string shaderDir)
        {
            _device = device;
            var factory = device.ResourceFactory;

            // Linear sampler (clamp)
            _linearSampler = factory.CreateSampler(new SamplerDescription(
                SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
                SamplerFilter.MinLinear_MagLinear_MipLinear,
                null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));

            // Compile shaders
            string fullscreenVert = Path.Combine(shaderDir, "fullscreen.vert");
            string bloomThresholdFrag = Path.Combine(shaderDir, "bloom_threshold.frag");
            string gaussianBlurFrag = Path.Combine(shaderDir, "gaussian_blur.frag");
            string tonemapCompositeFrag = Path.Combine(shaderDir, "tonemap_composite.frag");

            _bloomThresholdShaders = ShaderCompiler.CompileGLSL(device, fullscreenVert, bloomThresholdFrag);
            _blurShaders = ShaderCompiler.CompileGLSL(device, fullscreenVert, gaussianBlurFrag);
            _compositeShaders = ShaderCompiler.CompileGLSL(device, fullscreenVert, tonemapCompositeFrag);

            // Uniform buffers
            _bloomParamsBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<BloomParamsGPU>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _blurParamsBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<BlurParamsGPU>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _tonemapParamsBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<TonemapParamsGPU>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            // --- Bloom Threshold Layout: texture + sampler + params ---
            _bloomThresholdLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("SourceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("BloomParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            // --- Blur Layout: texture + sampler + params ---
            _blurLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("SourceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("BlurParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            // --- Composite Layout: HDR + Bloom + sampler + params ---
            _compositeLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("HDRTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("BloomTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("TexSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("TonemapParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            // Create half-res textures and pipelines
            CreateSizeDependentResources(width, height);

            Console.WriteLine($"[PostProcessing] Initialized ({width}x{height})");
        }

        private void CreateSizeDependentResources(uint width, uint height)
        {
            // Dispose old size-dependent resources
            DisposeSizeDependentResources();

            var factory = _device.ResourceFactory;
            _halfWidth = Math.Max(width / 2, 1);
            _halfHeight = Math.Max(height / 2, 1);

            // Bloom half-res texture
            _bloomHalf = factory.CreateTexture(TextureDescription.Texture2D(
                _halfWidth, _halfHeight, 1, 1,
                PixelFormat.R16_G16_B16_A16_Float,
                TextureUsage.RenderTarget | TextureUsage.Sampled));
            _bloomHalfView = factory.CreateTextureView(_bloomHalf);
            _bloomHalfFB = factory.CreateFramebuffer(new FramebufferDescription(null, _bloomHalf));

            // Blur ping-pong texture
            _blurPingPong = factory.CreateTexture(TextureDescription.Texture2D(
                _halfWidth, _halfHeight, 1, 1,
                PixelFormat.R16_G16_B16_A16_Float,
                TextureUsage.RenderTarget | TextureUsage.Sampled));
            _blurPingPongView = factory.CreateTextureView(_blurPingPong);
            _blurPingPongFB = factory.CreateFramebuffer(new FramebufferDescription(null, _blurPingPong));

            // --- Bloom Threshold Pipeline ---
            _bloomThresholdPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(
                    FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _bloomThresholdLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: Array.Empty<VertexLayoutDescription>(),
                    shaders: _bloomThresholdShaders!),
                Outputs = _bloomHalfFB.OutputDescription,
            });

            // --- Blur Pipeline (shared for H and V passes) ---
            _blurPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(
                    FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _blurLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: Array.Empty<VertexLayoutDescription>(),
                    shaders: _blurShaders!),
                Outputs = _blurPingPongFB.OutputDescription,
            });

            // --- Composite Pipeline (outputs to swapchain, alpha blend for clear color) ---
            var compositeBlend = new BlendStateDescription(
                RgbaFloat.Black,
                new BlendAttachmentDescription(
                    blendEnabled: true,
                    sourceColorFactor: BlendFactor.SourceAlpha,
                    destinationColorFactor: BlendFactor.InverseSourceAlpha,
                    colorFunction: BlendFunction.Add,
                    sourceAlphaFactor: BlendFactor.One,
                    destinationAlphaFactor: BlendFactor.InverseSourceAlpha,
                    alphaFunction: BlendFunction.Add));
            _compositePipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = compositeBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(
                    FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _compositeLayout! },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: Array.Empty<VertexLayoutDescription>(),
                    shaders: _compositeShaders!),
                Outputs = _device.SwapchainFramebuffer.OutputDescription,
            });
        }

        public void CreateResourceSets(TextureView hdrView)
        {
            // Dispose old resource sets
            _bloomThresholdSet?.Dispose();
            _blurHSet?.Dispose();
            _blurVSet?.Dispose();
            _compositeSet?.Dispose();

            var factory = _device.ResourceFactory;

            // Bloom threshold: reads HDR texture
            _bloomThresholdSet = factory.CreateResourceSet(new ResourceSetDescription(
                _bloomThresholdLayout!, hdrView, _linearSampler!, _bloomParamsBuffer!));

            // Blur H: reads bloom half → writes to pingpong
            _blurHSet = factory.CreateResourceSet(new ResourceSetDescription(
                _blurLayout!, _bloomHalfView!, _linearSampler!, _blurParamsBuffer!));

            // Blur V: reads pingpong → writes to bloom half
            _blurVSet = factory.CreateResourceSet(new ResourceSetDescription(
                _blurLayout!, _blurPingPongView!, _linearSampler!, _blurParamsBuffer!));

            // Composite: reads HDR + bloom half
            _compositeSet = factory.CreateResourceSet(new ResourceSetDescription(
                _compositeLayout!, hdrView, _bloomHalfView!, _linearSampler!, _tonemapParamsBuffer!));
        }

        public void Execute(CommandList cl, Framebuffer swapchainFB)
        {
            // Upload bloom params
            cl.UpdateBuffer(_bloomParamsBuffer, 0, new BloomParamsGPU
            {
                Threshold = BloomThreshold,
                SoftKnee = BloomSoftKnee,
            });

            // 1. Bloom threshold → _bloomHalf (half resolution)
            cl.SetFramebuffer(_bloomHalfFB);
            cl.SetPipeline(_bloomThresholdPipeline);
            cl.SetGraphicsResourceSet(0, _bloomThresholdSet);
            cl.Draw(3, 1, 0, 0);

            // 2. Gaussian blur H → _blurPingPong
            cl.UpdateBuffer(_blurParamsBuffer, 0, new BlurParamsGPU
            {
                Direction = new Vector2(1f / _halfWidth, 0f),
            });
            cl.SetFramebuffer(_blurPingPongFB);
            cl.SetPipeline(_blurPipeline);
            cl.SetGraphicsResourceSet(0, _blurHSet);
            cl.Draw(3, 1, 0, 0);

            // 3. Gaussian blur V → _bloomHalf
            cl.UpdateBuffer(_blurParamsBuffer, 0, new BlurParamsGPU
            {
                Direction = new Vector2(0f, 1f / _halfHeight),
            });
            cl.SetFramebuffer(_bloomHalfFB);
            cl.SetPipeline(_blurPipeline);
            cl.SetGraphicsResourceSet(0, _blurVSet);
            cl.Draw(3, 1, 0, 0);

            // 4. Composite (HDR + Bloom) + Tone Mapping → Swapchain
            cl.UpdateBuffer(_tonemapParamsBuffer, 0, new TonemapParamsGPU
            {
                BloomIntensity = BloomIntensity,
                Exposure = Exposure,
            });
            cl.SetFramebuffer(swapchainFB);
            cl.SetPipeline(_compositePipeline);
            cl.SetGraphicsResourceSet(0, _compositeSet);
            cl.Draw(3, 1, 0, 0);
        }

        public void Resize(uint width, uint height)
        {
            CreateSizeDependentResources(width, height);
        }

        private void DisposeSizeDependentResources()
        {
            _bloomThresholdSet?.Dispose();
            _blurHSet?.Dispose();
            _blurVSet?.Dispose();
            _compositeSet?.Dispose();
            _bloomThresholdSet = null;
            _blurHSet = null;
            _blurVSet = null;
            _compositeSet = null;

            _bloomThresholdPipeline?.Dispose();
            _blurPipeline?.Dispose();
            _compositePipeline?.Dispose();

            _bloomHalfView?.Dispose();
            _bloomHalfFB?.Dispose();
            _bloomHalf?.Dispose();
            _blurPingPongView?.Dispose();
            _blurPingPongFB?.Dispose();
            _blurPingPong?.Dispose();
        }

        public void Dispose()
        {
            DisposeSizeDependentResources();

            _bloomParamsBuffer?.Dispose();
            _blurParamsBuffer?.Dispose();
            _tonemapParamsBuffer?.Dispose();
            _linearSampler?.Dispose();
            _bloomThresholdLayout?.Dispose();
            _blurLayout?.Dispose();
            _compositeLayout?.Dispose();

            if (_bloomThresholdShaders != null)
                foreach (var s in _bloomThresholdShaders) s.Dispose();
            if (_blurShaders != null)
                foreach (var s in _blurShaders) s.Dispose();
            if (_compositeShaders != null)
                foreach (var s in _compositeShaders) s.Dispose();

            Console.WriteLine("[PostProcessing] Disposed");
        }
    }
}
