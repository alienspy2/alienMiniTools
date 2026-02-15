using RoseEngine;

public class PBRDemo : MonoBehaviour
{
    public override void Awake()
    {
        Debug.Log("[PBRDemo] Setting up PBR sphere grid...");

        // Camera
        var camObj = new GameObject("Main Camera");
        var cam = camObj.AddComponent<Camera>();
        camObj.transform.position = new Vector3(0, 0, -14f);

        // Skybox — load panoramic environment map
        try
        {
            var envmapPath = System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(), "Assets", "Textures", "envmap_parking_garage.png");
            var envmapTex = Texture2D.LoadFromFile(envmapPath);

            var skyboxMat = new Material(Shader.Find("Skybox/Panoramic")!);
            skyboxMat.mainTexture = envmapTex;
            skyboxMat.exposure = 1.2f;
            skyboxMat.rotation = 0f;
            RenderSettings.skybox = skyboxMat;
            RenderSettings.ambientIntensity = 1.0f;

            Debug.Log("[PBRDemo] Panoramic skybox loaded: envmap_parking_garage.png");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PBRDemo] Skybox load failed (using procedural): {ex.Message}");
        }

        // Load font for labels
        Font? font = null;
        try
        {
            var fontPath = System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(), "Assets", "Fonts", "NotoSans_eng.ttf");
            font = Font.CreateFromFile(fontPath, 32);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PBRDemo] Font load failed: {ex.Message}");
            try { font = Font.CreateDefault(32); }
            catch { /* no font available */ }
        }

        // Main directional light — from upper-left, facing the sphere grid
        var dirLightObj = new GameObject("Directional Light");
        var dirLight = dirLightObj.AddComponent<Light>();
        dirLight.type = LightType.Directional;
        dirLight.color = Color.white;
        dirLight.intensity = 5.0f;
        dirLightObj.transform.position = new Vector3(-3, 5, -10);
        dirLightObj.transform.LookAt(new Vector3(0, 0, 0));

        // 4 colored point lights (far away for uniform lighting across grid)
        CreatePointLight("Red Light",   new Vector3(-20, 12, 20), new Color(1f, 0.2f, 0.1f), 5f, 80f);
        CreatePointLight("Green Light", new Vector3( 20, 12, 20), new Color(0.1f, 1f, 0.2f), 5f, 80f);
        CreatePointLight("Blue Light",  new Vector3(-20,-12, 20), new Color(0.1f, 0.3f, 1f), 5f, 80f);
        CreatePointLight("White Light", new Vector3( 20,-12, 20), new Color(1f, 0.9f, 0.8f), 1f, 80f);

        // 5x5 sphere grid: rows = metallic (0→1), columns = roughness (0→1)
        int gridSize = 5;
        float spacing = 2.2f;
        float startX = -(gridSize - 1) * spacing * 0.5f;
        float startY = -(gridSize - 1) * spacing * 0.5f;

        for (int row = 0; row < gridSize; row++)
        {
            for (int col = 0; col < gridSize; col++)
            {
                float metallic = row / (float)(gridSize - 1);
                float roughness = col / (float)(gridSize - 1);
                roughness = Mathf.Max(roughness, 0.05f); // avoid 0 roughness artifacts

                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"Sphere_M{metallic:F1}_R{roughness:F1}";
                sphere.transform.position = new Vector3(
                    startX + col * spacing,
                    startY + row * spacing,
                    0);
                sphere.transform.localScale = Vector3.one * 1.5f;

                var mat = new Material();
                mat.color = new Color(0.9f, 0.4f, 0.2f); // warm copper-ish base color
                mat.metallic = metallic;
                mat.roughness = roughness;
                mat.occlusion = 1.0f;
                sphere.GetComponent<MeshRenderer>()!.material = mat;
            }
        }

        // Floor plane (non-metallic, rough)
        // var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        // floor.name = "Floor";
        // floor.transform.position = new Vector3(0, -6.5f, 0);
        // var floorMat = new Material();
        // floorMat.color = new Color(0.5f, 0.5f, 0.5f);
        // floorMat.metallic = 0f;
        // floorMat.roughness = 0.7f;
        // floor.GetComponent<MeshRenderer>()!.material = floorMat;

        // === Labels ===
        if (font != null)
        {
            float labelZ = 0f;
            float bottomY = startY - spacing * 0.9f;
            float leftX = startX - spacing * 1.0f;
            var labelColor = new Color(0.0f, 0.0f, 0.1f, 1f);

            // Title
            CreateLabel("Title", font, "PBR Material Grid",
                new Vector3(0, startY + (gridSize - 1) * spacing + spacing * 0.8f, labelZ),
                labelColor, TextAlignment.Center, 1.5f);

            // Bottom axis title: "Roughness"
            CreateLabel("AxisRoughness", font, "Roughness -->",
                new Vector3(0, bottomY - spacing * 0.5f, labelZ),
                labelColor, TextAlignment.Center, 1f);

            // Left axis title: "Metallic" (rotated 90°)
            CreateLabel("AxisMetallic", font, "Metallic",
                new Vector3(leftX - spacing * 0.5f, 0, labelZ),
                labelColor, TextAlignment.Center, 1f,
                Quaternion.Euler(0, 0, 90));

            // Column labels (roughness values) along the bottom
            for (int col = 0; col < gridSize; col++)
            {
                float roughness = col / (float)(gridSize - 1);
                roughness = Mathf.Max(roughness, 0.05f);
                string label = roughness.ToString("F2");
                CreateLabel($"ColLabel_{col}", font, label,
                    new Vector3(startX + col * spacing, bottomY, labelZ),
                    labelColor, TextAlignment.Center, 1f);
            }

            // Row labels (metallic values) along the left
            for (int row = 0; row < gridSize; row++)
            {
                float metallic = row / (float)(gridSize - 1);
                string label = metallic.ToString("F2");
                CreateLabel($"RowLabel_{row}", font, label,
                    new Vector3(leftX, startY + row * spacing, labelZ),
                    labelColor, TextAlignment.Center, 1f);
            }
        }

        Debug.Log($"[PBRDemo] PBR grid ready - {gridSize}x{gridSize} spheres");
        Debug.Log("[PBRDemo] Rows: metallic 0->1 (bottom->top)");
        Debug.Log("[PBRDemo] Cols: roughness 0->1 (left->right)");
    }

    private static void CreatePointLight(string name, Vector3 position, Color color, float intensity, float range)
    {
        var lightObj = new GameObject(name);
        var light = lightObj.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        lightObj.transform.position = position;
    }

    private static void CreateLabel(string name, Font font, string text, Vector3 position, Color color, TextAlignment alignment, float scale, Quaternion? rotation = null)
    {
        var go = new GameObject(name);
        var tr = go.AddComponent<TextRenderer>();
        tr.font = font;
        tr.text = text;
        tr.color = color;
        tr.alignment = alignment;
        tr.sortingOrder = 10;
        go.transform.position = position;
        go.transform.localScale = new Vector3(scale, scale, scale);
        if (rotation.HasValue)
            go.transform.rotation = rotation.Value;
    }
}
