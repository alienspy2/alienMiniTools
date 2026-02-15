using System.Collections.Generic;

namespace RoseEngine
{
    public enum LightType
    {
        Directional = 0,
        Point = 1,
        Spot = 2,
    }

    public class Light : Component
    {
        public Color color { get; set; } = Color.white;
        public float intensity { get; set; } = 1f;
        public float range { get; set; } = 10f;
        public LightType type { get; set; } = LightType.Directional;
        public bool enabled { get; set; } = true;

        // Spot Light
        public float spotAngle { get; set; } = 30f;        // inner cone angle (degrees, full)
        public float spotOuterAngle { get; set; } = 45f;    // outer cone angle (degrees, full)

        // Shadow
        public bool shadows { get; set; } = false;
        public int shadowResolution { get; set; } = 1024;
        public float shadowBias { get; set; } = 0.005f;
        public float shadowNormalBias { get; set; } = 0.02f;
        public float shadowNearPlane { get; set; } = 0.1f;

        internal static readonly List<Light> _allLights = new();

        internal override void OnAddedToGameObject() => _allLights.Add(this);
        internal override void OnComponentDestroy() => _allLights.Remove(this);
        internal static void ClearAll() => _allLights.Clear();
    }
}
