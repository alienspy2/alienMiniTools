namespace UnityEngine
{
    public class Material
    {
        public Color color { get; set; } = Color.white;

        public Material() { }

        public Material(Color color)
        {
            this.color = color;
        }
    }
}
