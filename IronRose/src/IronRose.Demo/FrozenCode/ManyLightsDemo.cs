using System.Collections.Generic;
using RoseEngine;

public class ManyLightsDemo : MonoBehaviour
{
    private const float OrbitRadius = 6f;
    private const float OrbitHeight = 6f;
    private const float OrbitSpeed = 0.4f;
    private int _spotLightCount = 6;
    private readonly List<(GameObject go, float phase)> _spots = new();
    private TextRenderer _hudText;
    private float _keyRepeatTimer;

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

RenderSettings.ambientLight = Color.black;
RenderSettings.skyHorizonColor = Color.black;
RenderSettings.skyZenithColor = Color.black;
RenderSettings.sunIntensity = 0;
RenderSettings.ambientIntensity = 0;

        // --- HUD (오른쪽 위) ---
        var hudObj = new GameObject("SpotLight HUD");
        _hudText = hudObj.AddComponent<TextRenderer>();
        _hudText.font = Font.CreateDefault(24);
        _hudText.color = Color.white;
        _hudText.alignment = TextAlignment.Right;
        _hudText.sortingOrder = 100;
        _hudText.pixelsPerUnit = 200f;
        hudObj.transform.position = new Vector3(3.5f, 1.8f, -4f);

        RebuildSpotLights();
    }

    public override void Update()
    {
        bool up = Input.GetKey(KeyCode.PageUp);
        bool down = Input.GetKey(KeyCode.PageDown);
        if (Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.PageDown))
            _keyRepeatTimer = 0f;
        if (up || down)
        {
            _keyRepeatTimer -= Time.deltaTime;
            if (_keyRepeatTimer <= 0f)
            {
                _keyRepeatTimer = 0.1f;
                if (up) _spotLightCount++;
                if (down && _spotLightCount > 1) _spotLightCount--;
                RebuildSpotLights();
            }
        }

        float time = Time.time;
        foreach (var (go, phase) in _spots)
        {
            float angle = time * OrbitSpeed + phase;
            go.transform.position = new Vector3(
                Mathf.Cos(angle) * OrbitRadius, OrbitHeight, Mathf.Sin(angle) * OrbitRadius);
            go.transform.LookAt(Vector3.zero);
        }
    }

    private void RebuildSpotLights()
    {
        foreach (var (go, _) in _spots)
            RoseEngine.Object.Destroy(go);
        _spots.Clear();

        for (int i = 0; i < _spotLightCount; i++)
        {
            float t = i / (float)_spotLightCount;
            float angle = t * Mathf.PI * 2f;
            var spotObj = new GameObject($"SpotLight_{i}");
            var spot = spotObj.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = Color.HSVToRGB(t, 0.85f, 1f);
            spot.intensity = 30f / _spotLightCount;
            spot.range = 15f;
            spot.spotAngle = 30f;
            spot.spotOuterAngle = 60f;
            spot.shadows = true;
            spot.shadowResolution = 128;
            spotObj.transform.position = new Vector3(
                Mathf.Cos(angle) * OrbitRadius, OrbitHeight, Mathf.Sin(angle) * OrbitRadius);
            spotObj.transform.LookAt(Vector3.zero);
            _spots.Add((spotObj, angle));
        }

        if (_hudText != null)
            _hudText.text = $"Spot Lights: {_spotLightCount}";
        Debug.Log($"[ManyLightsDemo] Spot lights: {_spotLightCount}");
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
