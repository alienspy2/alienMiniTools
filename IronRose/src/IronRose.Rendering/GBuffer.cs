using System;
using Veldrid;

namespace IronRose.Rendering
{
    public class GBuffer : IDisposable
    {
        public Texture AlbedoTexture { get; private set; } = null!;
        public Texture NormalTexture { get; private set; } = null!;
        public Texture MaterialTexture { get; private set; } = null!;
        public Texture DepthTexture { get; private set; } = null!;
        public Texture WorldPosTexture { get; private set; } = null!;

        public TextureView AlbedoView { get; private set; } = null!;
        public TextureView NormalView { get; private set; } = null!;
        public TextureView MaterialView { get; private set; } = null!;
        public TextureView WorldPosView { get; private set; } = null!;

        public Framebuffer Framebuffer { get; private set; } = null!;

        public uint Width { get; private set; }
        public uint Height { get; private set; }

        public void Initialize(GraphicsDevice device, uint width, uint height)
        {
            Dispose();

            Width = width;
            Height = height;
            var factory = device.ResourceFactory;

            // RT0: Albedo (RGBA8)
            AlbedoTexture = factory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.RenderTarget | TextureUsage.Sampled));

            // RT1: Normal + Roughness (RGBA16F)
            NormalTexture = factory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1,
                PixelFormat.R16_G16_B16_A16_Float,
                TextureUsage.RenderTarget | TextureUsage.Sampled));

            // RT2: Material (RGBA8)
            MaterialTexture = factory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.RenderTarget | TextureUsage.Sampled));

            // Depth-Stencil (render only)
            DepthTexture = factory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1,
                PixelFormat.D32_Float_S8_UInt,
                TextureUsage.DepthStencil));

            // RT3: World Position (RGBA16F — written directly by geometry shader)
            WorldPosTexture = factory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1,
                PixelFormat.R16_G16_B16_A16_Float,
                TextureUsage.RenderTarget | TextureUsage.Sampled));

            // TextureViews
            AlbedoView = factory.CreateTextureView(AlbedoTexture);
            NormalView = factory.CreateTextureView(NormalTexture);
            MaterialView = factory.CreateTextureView(MaterialTexture);
            WorldPosView = factory.CreateTextureView(WorldPosTexture);

            // Framebuffer (4 color + 1 depth)
            Framebuffer = factory.CreateFramebuffer(new FramebufferDescription(
                DepthTexture,
                AlbedoTexture,
                NormalTexture,
                MaterialTexture,
                WorldPosTexture));

            Console.WriteLine($"[GBuffer] Initialized ({width}x{height})");
        }

        public void Dispose()
        {
            AlbedoView?.Dispose();
            NormalView?.Dispose();
            MaterialView?.Dispose();
            WorldPosView?.Dispose();
            Framebuffer?.Dispose();
            AlbedoTexture?.Dispose();
            NormalTexture?.Dispose();
            MaterialTexture?.Dispose();
            DepthTexture?.Dispose();
            WorldPosTexture?.Dispose();
        }
    }
}
