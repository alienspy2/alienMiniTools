namespace RoseEngine
{
    public class Material
    {
        public Shader? shader { get; set; }
        public Color color { get; set; } = Color.white;
        public Texture2D? mainTexture { get; set; }
        public Color emission { get; set; } = Color.black;

        // PBR properties
        public float metallic { get; set; } = 0.0f;
        public float roughness { get; set; } = 0.5f;
        public float occlusion { get; set; } = 1.0f;
        public Texture2D? normalMap { get; set; }
        public Texture2D? MROMap { get; set; }

        // Skybox properties (used when shader is Skybox/Panoramic or Skybox/Procedural)
        public float exposure { get; set; } = 1.0f;
        public float rotation { get; set; } = 0.0f;

        // RenderSystem caches the lazy-converted cubemap here
        internal Cubemap? _cachedCubemap;
        internal Texture2D? _cachedCubemapSource; // change detection

        public Material() { }

        public Material(Shader shader)
        {
            this.shader = shader;
        }

        public Material(Color color)
        {
            this.color = color;
        }
    }
}
