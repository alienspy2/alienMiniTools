using System;
using System.IO;
using System.Runtime.InteropServices;
using Veldrid;

namespace IronRose.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct TonemapParamsGPU
    {
        public float Exposure;
        public float _pad1;
        public float _pad2;
        public float _pad3;
    }

    public class TonemapEffect : PostProcessEffect
    {
        public override string Name => "Tonemap";

        [EffectParam("Exposure", Min = 0.01f, Max = 10f)]
        public float Exposure { get; set; } = 1.0f;

        // Pipeline
        private Pipeline? _pipeline;
        private ResourceLayout? _layout;
        private DeviceBuffer? _paramsBuffer;
        private Shader[]? _shaders;

        protected override void OnInitialize(uint width, uint height)
        {
            var factory = Device.ResourceFactory;

            // Compile shader
            string fullscreenVert = Path.Combine(ShaderDir, "fullscreen.vert");
            _shaders = ShaderCompiler.CompileGLSL(Device, fullscreenVert,
                Path.Combine(ShaderDir, "tonemap.frag"));

            // Uniform buffer
            _paramsBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<TonemapParamsGPU>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            // Resource layout
            _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("SourceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("TonemapParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            CreateSizeDependentResources(width, height);

            Console.WriteLine($"[TonemapEffect] Initialized ({width}x{height})");
        }

        public override void Resize(uint width, uint height)
        {
            _pipeline?.Dispose();
            CreateSizeDependentResources(width, height);
        }

        public override void Execute(CommandList cl, TextureView sourceView, Framebuffer destinationFB)
        {
            var factory = Device.ResourceFactory;

            using var resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _layout!, sourceView, LinearSampler, _paramsBuffer!));

            cl.UpdateBuffer(_paramsBuffer, 0, new TonemapParamsGPU
            {
                Exposure = Exposure,
            });

            cl.SetFramebuffer(destinationFB);
            cl.SetPipeline(_pipeline);
            cl.SetGraphicsResourceSet(0, resourceSet);
            cl.Draw(3, 1, 0, 0);
        }

        private void CreateSizeDependentResources(uint width, uint height)
        {
            // Tonemap outputs to swapchain — use its output description
            // We create the pipeline with alpha blend for clear color compositing
            var blend = new BlendStateDescription(
                RgbaFloat.Black,
                new BlendAttachmentDescription(
                    blendEnabled: true,
                    sourceColorFactor: BlendFactor.SourceAlpha,
                    destinationColorFactor: BlendFactor.InverseSourceAlpha,
                    colorFunction: BlendFunction.Add,
                    sourceAlphaFactor: BlendFactor.One,
                    destinationAlphaFactor: BlendFactor.InverseSourceAlpha,
                    alphaFunction: BlendFunction.Add));

            _pipeline = CreateFullscreenPipeline(_layout!, _shaders!,
                Device.SwapchainFramebuffer.OutputDescription, blend);
        }

        public override void Dispose()
        {
            _pipeline?.Dispose();
            _paramsBuffer?.Dispose();
            _layout?.Dispose();

            if (_shaders != null)
                foreach (var s in _shaders) s.Dispose();

            Console.WriteLine("[TonemapEffect] Disposed");
        }
    }
}
