namespace UnityEngine
{
    public class Material
    {
        public Color color { get; set; } = Color.white;
        public Texture2D? mainTexture { get; set; }
        public Color emission { get; set; } = Color.black;

        public Material() { }

        public Material(Color color)
        {
            this.color = color;
        }
    }
}
