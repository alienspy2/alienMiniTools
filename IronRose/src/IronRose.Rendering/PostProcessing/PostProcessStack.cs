using System;
using System.Collections.Generic;
using System.Linq;
using Veldrid;

namespace IronRose.Rendering
{
    public class PostProcessStack : IDisposable
    {
        private GraphicsDevice _device = null!;
        private string _shaderDir = "";
        private Sampler? _linearSampler;

        // Ping-pong HDR textures for chaining effects
        private Texture? _pingTexture;
        private TextureView? _pingView;
        private Framebuffer? _pingFB;

        private Texture? _pongTexture;
        private TextureView? _pongView;
        private Framebuffer? _pongFB;

        private uint _width;
        private uint _height;

        private readonly List<PostProcessEffect> _effects = new();

        public IReadOnlyList<PostProcessEffect> Effects => _effects;

        public void Initialize(GraphicsDevice device, uint width, uint height, string shaderDir)
        {
            _device = device;
            _shaderDir = shaderDir;
            _width = width;
            _height = height;

            _linearSampler = device.ResourceFactory.CreateSampler(new SamplerDescription(
                SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
                SamplerFilter.MinLinear_MagLinear_MipLinear,
                null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));

            CreatePingPongBuffers(width, height);
        }

        public void AddEffect(PostProcessEffect effect)
        {
            effect.InitializeBase(_device, _shaderDir, _linearSampler!, _width, _height);
            _effects.Add(effect);
        }

        public void InsertEffect(int index, PostProcessEffect effect)
        {
            effect.InitializeBase(_device, _shaderDir, _linearSampler!, _width, _height);
            _effects.Insert(index, effect);
        }

        public bool RemoveEffect(PostProcessEffect effect)
        {
            if (_effects.Remove(effect))
            {
                effect.Dispose();
                return true;
            }
            return false;
        }

        public void MoveEffect(int fromIndex, int toIndex)
        {
            var effect = _effects[fromIndex];
            _effects.RemoveAt(fromIndex);
            _effects.Insert(toIndex, effect);
        }

        public T? GetEffect<T>() where T : PostProcessEffect
        {
            return _effects.OfType<T>().FirstOrDefault();
        }

        public void Execute(CommandList cl, TextureView hdrSourceView, Framebuffer swapchainFB)
        {
            var enabled = _effects.Where(e => e.Enabled).ToList();
            if (enabled.Count == 0) return;

            var currentSource = hdrSourceView;
            bool usePing = true;

            for (int i = 0; i < enabled.Count; i++)
            {
                bool isLast = (i == enabled.Count - 1);
                Framebuffer destFB;

                if (isLast)
                {
                    destFB = swapchainFB;
                }
                else
                {
                    destFB = usePing ? _pingFB! : _pongFB!;
                }

                enabled[i].Execute(cl, currentSource, destFB);

                if (!isLast)
                {
                    currentSource = usePing ? _pingView! : _pongView!;
                    usePing = !usePing;
                }
            }
        }

        public void Resize(uint width, uint height)
        {
            _width = width;
            _height = height;
            DisposePingPongBuffers();
            CreatePingPongBuffers(width, height);

            foreach (var effect in _effects)
                effect.Resize(width, height);
        }

        private void CreatePingPongBuffers(uint width, uint height)
        {
            var factory = _device.ResourceFactory;

            _pingTexture = factory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1,
                PixelFormat.R16_G16_B16_A16_Float,
                TextureUsage.RenderTarget | TextureUsage.Sampled));
            _pingView = factory.CreateTextureView(_pingTexture);
            _pingFB = factory.CreateFramebuffer(new FramebufferDescription(null, _pingTexture));

            _pongTexture = factory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1,
                PixelFormat.R16_G16_B16_A16_Float,
                TextureUsage.RenderTarget | TextureUsage.Sampled));
            _pongView = factory.CreateTextureView(_pongTexture);
            _pongFB = factory.CreateFramebuffer(new FramebufferDescription(null, _pongTexture));
        }

        private void DisposePingPongBuffers()
        {
            _pingView?.Dispose();
            _pingFB?.Dispose();
            _pingTexture?.Dispose();
            _pongView?.Dispose();
            _pongFB?.Dispose();
            _pongTexture?.Dispose();
        }

        public void Dispose()
        {
            foreach (var effect in _effects)
                effect.Dispose();
            _effects.Clear();

            DisposePingPongBuffers();
            _linearSampler?.Dispose();

            Console.WriteLine("[PostProcessStack] Disposed");
        }
    }
}
