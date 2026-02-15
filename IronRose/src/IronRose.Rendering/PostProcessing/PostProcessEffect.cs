using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Veldrid;

namespace IronRose.Rendering
{
    public abstract class PostProcessEffect : IDisposable
    {
        public abstract string Name { get; }
        public bool Enabled { get; set; } = true;

        protected GraphicsDevice Device = null!;
        protected Sampler LinearSampler = null!;
        protected string ShaderDir = "";

        private List<EffectParameterInfo>? _cachedParams;

        public void InitializeBase(GraphicsDevice device, string shaderDir, Sampler linearSampler, uint width, uint height)
        {
            Device = device;
            ShaderDir = shaderDir;
            LinearSampler = linearSampler;
            OnInitialize(width, height);
        }

        protected abstract void OnInitialize(uint width, uint height);
        public abstract void Resize(uint width, uint height);
        public abstract void Execute(CommandList cl, TextureView sourceView, Framebuffer destinationFB);
        public abstract void Dispose();

        public IReadOnlyList<EffectParameterInfo> GetParameters()
        {
            if (_cachedParams != null) return _cachedParams;

            _cachedParams = new List<EffectParameterInfo>();
            var props = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in props)
            {
                var attr = prop.GetCustomAttribute<EffectParamAttribute>();
                if (attr == null) continue;

                var p = prop; // capture for lambda
                _cachedParams.Add(new EffectParameterInfo(
                    attr.DisplayName,
                    p.PropertyType,
                    attr.Min,
                    attr.Max,
                    () => p.GetValue(this)!,
                    v => p.SetValue(this, v)));
            }

            return _cachedParams;
        }

        protected Pipeline CreateFullscreenPipeline(ResourceLayout layout, Shader[] shaders,
            OutputDescription outputDesc, BlendStateDescription? blendState = null)
        {
            return Device.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = blendState ?? BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(
                    FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { layout },
                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: Array.Empty<VertexLayoutDescription>(),
                    shaders: shaders),
                Outputs = outputDesc,
            });
        }
    }
}
