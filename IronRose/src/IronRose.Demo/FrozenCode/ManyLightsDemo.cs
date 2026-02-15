using System.Collections.Generic;
using RoseEngine;

public class ManyLightsDemo : MonoBehaviour
{
    private const int LightCount = 64;
    private readonly List<(GameObject go, float radius, float speed, float height, float phase)> _lights = new();

    public override void Awake()
    {
        Debug.Log("[ManyLightsDemo] Setting up Many Lights scene...");

        // Camera — overhead angle
        DemoUtils.CreateCamera(
            new Vector3(0, 10f, -14f),
            lookAt: new Vector3(0, 0, 0),
            clearFlags: CameraClearFlags.SolidColor,
            backgroundColor: new Color(0.01f, 0.01f, 0.02f));

        // --- Checkerboard floor plane ---
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Checker Floor";
        floor.transform.position = new Vector3(0, -1f, 0);
        floor.transform.localScale = new Vector3(20f, 1f, 20f);

        var floorMat = new Material();
        floorMat.mainTexture = CreateCheckerTexture(128, 16,
            new Color(0.6f, 0.6f, 0.6f), new Color(0.15f, 0.15f, 0.15f));
        floorMat.metallic = 0.05f;
        floorMat.roughness = 0.4f;
        floor.GetComponent<MeshRenderer>()!.material = floorMat;

        // --- Central sphere ---
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Center Sphere";
        sphere.transform.position = new Vector3(0, 0.5f, 0);
        sphere.transform.localScale = new Vector3(3f, 3f, 3f);

        var sphereMat = new Material();
        sphereMat.color = new Color(0.95f, 0.95f, 0.95f);
        sphereMat.metallic = 0.1f;
        sphereMat.roughness = 0.15f;
        sphere.GetComponent<MeshRenderer>()!.material = sphereMat;

        // --- Many orbiting point lights (rainbow colors) ---
        for (int i = 0; i < LightCount; i++)
        {
            float t = i / (float)LightCount;
            Color c = Color.HSVToRGB(t, 0.85f, 1f);

            var lightObj = new GameObject($"Light_{i}");
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = c;
            light.intensity = 1.5f;
            light.range = 8f;

            light.shadows = true;
            light.shadowResolution = 256;

            // Vary orbit parameters per light
            float radius = 4f + (i % 4) * 1.2f;
            float speed  = 0.4f + (i % 5) * 0.12f;
            float height = 1.5f + (i % 3) * 1.0f;
            float phase  = t * Mathf.PI * 2f;

            _lights.Add((lightObj, radius, speed, height, phase));
        }

        // --- Directional light with shadow ---
        var dirLightObj = new GameObject("DirLight_Shadow");
        var dirLight = dirLightObj.AddComponent<Light>();
        dirLight.type = LightType.Directional;
        dirLight.color = new Color(1f, 0.95f, 0.9f);
        dirLight.intensity = 1.5f;
        dirLight.shadows = true;
        dirLight.shadowResolution = 2048;
        dirLight.shadowBias = 0.005f;
        dirLightObj.transform.Rotate(50, -30, 0);

        // --- Spot lights pointing down at corners ---
        var spotColors = new[] { Color.red, Color.green, Color.blue, Color.yellow };
        var spotPositions = new[]
        {
            new Vector3(-6, 6, -6), new Vector3(6, 6, -6),
            new Vector3(-6, 6, 6),  new Vector3(6, 6, 6),
        };
        for (int i = 0; i < 4; i++)
        {
            var spotObj = new GameObject($"SpotLight_{i}");
            var spot = spotObj.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = spotColors[i];
            spot.intensity = 8f;
            spot.range = 15f;
            spot.spotAngle = 25f;
            spot.spotOuterAngle = 40f;
            // Enable shadow on first two spot lights
            if (i < 2)
            {
                spot.shadows = true;
                spot.shadowResolution = 1024;
            }
            spotObj.transform.position = spotPositions[i];
            spotObj.transform.LookAt(new Vector3(spotPositions[i].x * 0.3f, -1f, spotPositions[i].z * 0.3f));
        }

        Debug.Log($"[ManyLightsDemo] {LightCount} point(2 shadow) + 1 dir(shadow) + 4 spot(2 shadow) — enjoy the show!");
    }

    public override void Update()
    {
        float time = Time.time;
        foreach (var (go, radius, speed, height, phase) in _lights)
        {
            float angle = time * speed + phase;
            go.transform.position = new Vector3(
                Mathf.Cos(angle) * radius,
                height,
                Mathf.Sin(angle) * radius);
        }
    }

    private static Texture2D CreateCheckerTexture(int size, int tiles, Color c1, Color c2)
    {
        var tex = new Texture2D(size, size);
        var data = new byte[size * size * 4];
        int tileSize = size / tiles;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool light = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                Color c = light ? c1 : c2;
                int off = (y * size + x) * 4;
                data[off]     = (byte)(c.r * 255);
                data[off + 1] = (byte)(c.g * 255);
                data[off + 2] = (byte)(c.b * 255);
                data[off + 3] = 255;
            }
        }

        tex.SetPixels(data);
        return tex;
    }
}
