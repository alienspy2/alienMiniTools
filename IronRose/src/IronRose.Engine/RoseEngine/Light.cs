using System.Collections.Generic;

namespace RoseEngine
{
    public enum LightType
    {
        Directional = 0,
        Point = 1,
    }

    public class Light : Component
    {
        public Color color { get; set; } = Color.white;
        public float intensity { get; set; } = 1f;
        public float range { get; set; } = 10f;
        public LightType type { get; set; } = LightType.Directional;
        public bool enabled { get; set; } = true;

        internal static readonly List<Light> _allLights = new();

        internal override void OnAddedToGameObject() => _allLights.Add(this);
        internal override void OnComponentDestroy() => _allLights.Remove(this);
        internal static void ClearAll() => _allLights.Clear();
    }
}
