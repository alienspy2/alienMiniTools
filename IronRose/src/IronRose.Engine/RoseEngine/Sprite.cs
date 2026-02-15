namespace RoseEngine
{
    public class Sprite
    {
        public Texture2D texture { get; }
        public Rect rect { get; }
        public Vector2 pivot { get; }
        public float pixelsPerUnit { get; }

        internal Vector2 uvMin;
        internal Vector2 uvMax;

        public Vector2 bounds => new(rect.width / pixelsPerUnit, rect.height / pixelsPerUnit);

        private Sprite(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit)
        {
            this.texture = texture;
            this.rect = rect;
            this.pivot = pivot;
            this.pixelsPerUnit = pixelsPerUnit;

            // Compute normalized UV coordinates
            uvMin = new Vector2(rect.x / texture.width, rect.y / texture.height);
            uvMax = new Vector2(rect.xMax / texture.width, rect.yMax / texture.height);
        }

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit = 100f)
        {
            return new Sprite(texture, rect, pivot, pixelsPerUnit);
        }
    }
}
