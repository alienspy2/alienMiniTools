using UnityEngine;

public class AssetImportDemo : MonoBehaviour
{
    public override void Awake()
    {
        Debug.Log("[AssetImportDemo] Loading asset from pipeline...");

        // Camera
        var camObj = new GameObject("Main Camera");
        var cam = camObj.AddComponent<Camera>();
        camObj.transform.position = new Vector3(0, 0, -5f);

        // Light
        var lightObj = new GameObject("Main Light");
        var light = lightObj.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Color.white;
        light.intensity = 2.0f;
        light.range = 15f;
        lightObj.transform.position = new Vector3(2f, 3f, -2f);

        // Fill light
        var fillObj = new GameObject("Fill Light");
        var fill = fillObj.AddComponent<Light>();
        fill.type = LightType.Point;
        fill.color = new Color(0.6f, 0.7f, 0.9f);
        fill.intensity = 1.0f;
        fill.range = 10f;
        fillObj.transform.position = new Vector3(-3f, 1f, -3f);

        // Ground plane
        var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ground.name = "Ground";
        ground.GetComponent<MeshRenderer>()!.material = new Material(new Color(0.3f, 0.3f, 0.3f));
        ground.transform.position = new Vector3(0, -1.5f, 0);
        ground.transform.Rotate(-90, 0, 0);
        ground.transform.localScale = new Vector3(10f, 10f, 1f);

        // Load mesh from AssetDatabase via Resources
        var assetPath = FindAssetPath("HandcraftedWoodenLantern_01", "model.glb");
        if (assetPath == null)
        {
            Debug.LogWarning("[AssetImportDemo] Asset not found, showing placeholder cube");
            var placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placeholder.name = "Placeholder";
            placeholder.GetComponent<MeshRenderer>()!.material = new Material(new Color(0.8f, 0.4f, 0.1f));
            return;
        }

        var mesh = Resources.Load<Mesh>(assetPath);
        if (mesh == null)
        {
            Debug.LogError($"[AssetImportDemo] Failed to load mesh: {assetPath}");
            return;
        }

        Debug.Log($"[AssetImportDemo] Loaded: {assetPath} ({mesh.vertices.Length} verts, {mesh.indices.Length / 3} tris)");

        // Create GameObject with imported mesh
        var meshObj = new GameObject("Imported Mesh");
        var filter = meshObj.AddComponent<MeshFilter>();
        var renderer = meshObj.AddComponent<MeshRenderer>();
        filter.mesh = mesh;
        renderer.material = new Material(new Color(0.85f, 0.65f, 0.4f));
        meshObj.transform.position = new Vector3(0, 0, 0);

        // Load preview.png from the same asset folder as a sprite
        var assetDir = System.IO.Path.GetDirectoryName(assetPath)!;
        var pngPath = System.IO.Path.Combine(assetDir, "preview.png");
        if (System.IO.File.Exists(pngPath))
        {
            var tex = Texture2D.LoadFromFile(pngPath);
            var sprite = Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

            var spriteObj = new GameObject("Preview Sprite");
            var sr = spriteObj.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            spriteObj.transform.position = new Vector3(-2.5f, 1.5f, 0);
            spriteObj.transform.localScale = Vector3.one * 0.2f;
            Debug.Log($"[AssetImportDemo] Preview sprite loaded: {pngPath}");
        }

        Debug.Log("[AssetImportDemo] Asset import demo ready!");
    }

    private static string? FindAssetPath(string folderName, string fileName)
    {
        // Assets 디렉토리에서 에셋 경로 탐색
        var assetsDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Assets");
        var targetPath = System.IO.Path.Combine(assetsDir, "houseInTheForest", folderName, fileName);

        if (System.IO.File.Exists(targetPath))
        {
            Debug.Log($"[AssetImportDemo] Found asset: {targetPath}");
            return targetPath;
        }

        // fallback: 첫 번째 .glb 파일 찾기
        Debug.Log("[AssetImportDemo] Primary asset not found, searching for any .glb...");
        var houseDir = System.IO.Path.Combine(assetsDir, "houseInTheForest");
        if (System.IO.Directory.Exists(houseDir))
        {
            var files = System.IO.Directory.GetFiles(houseDir, "*.glb", System.IO.SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                Debug.Log($"[AssetImportDemo] Fallback asset: {files[0]}");
                return files[0];
            }
        }

        return null;
    }
}
