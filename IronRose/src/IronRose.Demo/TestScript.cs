using UnityEngine;
using UnityEngine.InputSystem;
using IronRose.API;

public class TestScript : MonoBehaviour
{
    private int _currentDemo = 0;
    private static Font? _hudFont;
    private static bool _isLoading;
    private GameObject? _hudGo;

    public override void Awake()
    {
        // Load HUD font once (static — survives SceneManager.Clear)
        if (_hudFont == null)
        {
            var fontPath = System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(), "Assets", "Fonts", "NotoSans_eng.ttf");
            try { _hudFont = Font.CreateFromFile(fontPath, 16); }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[TestScript] HUD font load failed: {ex.Message}");
            }
        }

        Debug.Log("=== IronRose Demo Selector ===");
        Debug.Log("[1] Cornell Box");
        Debug.Log("[2] Asset Import");
        Debug.Log("[3] Sprite Renderer");
        Debug.Log("[4] Text Renderer");
        Debug.Log("[5] 3D Physics");
        Debug.Log("[6] PBR Demo");
        Debug.Log("[F1] Wireframe | [F12] Screenshot | [ESC] Quit");
        Debug.Log("==============================");

        // Default camera + HUD when no demo is loaded
        if (!_isLoading)
        {
            EnsureCamera();
            CreateHud();
        }
    }

    public override void Update()
    {
        // Demo selection
        if (Input.GetKeyDown(KeyCode.Alpha1)) LoadDemo(1);
        if (Input.GetKeyDown(KeyCode.Alpha2)) LoadDemo(2);
        if (Input.GetKeyDown(KeyCode.Alpha3)) LoadDemo(3);
        if (Input.GetKeyDown(KeyCode.Alpha4)) LoadDemo(4);
        if (Input.GetKeyDown(KeyCode.Alpha5)) LoadDemo(5);
        if (Input.GetKeyDown(KeyCode.Alpha6)) LoadDemo(6);

        // Wireframe toggle
        if (Input.GetKeyDown(KeyCode.F1))
        {
            Debug.wireframe = !Debug.wireframe;
            Debug.Log($"[Debug] Wireframe: {(Debug.wireframe ? "ON" : "OFF")}");
        }

        if (Input.GetKeyDown(KeyCode.Escape))
            Application.Quit();

        UpdateHudLayout();
    }

    private void LoadDemo(int demoIndex)
    {
        if (demoIndex == _currentDemo)
        {
            Debug.Log($"[Demo] Demo {demoIndex} already active");
            return;
        }

        Debug.Log($"[Demo] Loading demo {demoIndex}...");
        _isLoading = true;

        // Clear current scene (except this selector)
        SceneManager.Clear();

        // Re-register self (Awake runs immediately, _isLoading prevents re-entrant LoadDemo)
        var selectorGo = new GameObject("DemoSelector");
        var selector = selectorGo.AddComponent<TestScript>();
        selector._currentDemo = demoIndex;

        _isLoading = false;

        // Launch selected demo
        switch (demoIndex)
        {
            case 1:
                var go1 = new GameObject("CornellBoxDemo");
                go1.AddComponent<CornellBoxDemo>();
                Debug.Log("[Demo] >> Cornell Box");
                break;

            case 2:
                var go2 = new GameObject("AssetImportDemo");
                go2.AddComponent<AssetImportDemo>();
                Debug.Log("[Demo] >> Asset Import");
                break;

            case 3:
                var go3 = new GameObject("SpriteDemo");
                go3.AddComponent<SpriteDemo>();
                Debug.Log("[Demo] >> Sprite Renderer");
                break;

            case 4:
                var go4 = new GameObject("TextDemo");
                go4.AddComponent<TextDemo>();
                Debug.Log("[Demo] >> Text Renderer");
                break;

            case 5:
                var go5 = new GameObject("PhysicsDemo3D");
                go5.AddComponent<PhysicsDemo3D>();
                Debug.Log("[Demo] >> 3D Physics");
                break;

            case 6:
                var go6 = new GameObject("PBRDemo");
                go6.AddComponent<PBRDemo>();
                Debug.Log("[Demo] >> PBR Demo");
                break;
        }

        // HUD: demo menu overlay (parented to camera)
        CreateHud();
    }

    private void EnsureCamera()
    {
        if (Camera.main != null) return;
        var camGo = new GameObject("DefaultCamera");
        var cam = camGo.AddComponent<Camera>();
        camGo.transform.position = new Vector3(0, 0, -5);
    }

    private void CreateHud()
    {
        EnsureCamera();
        if (_hudFont == null || Camera.main == null) return;

        _hudGo = new GameObject("HudMenu");
        var tr = _hudGo.AddComponent<TextRenderer>();
        tr.font = _hudFont;
        tr.text = "[1] Cornell Box\n"
                 + "[2] Asset Import\n"
                 + "[3] Sprite Renderer\n"
                 + "[4] Text Renderer\n"
                 + "[5] 3D Physics\n"
                 + "[6] PBR\n"
                 + "[F1] Wireframe | [F12] Screenshot\n"
                 + "[ESC] Quit";
        tr.color = Color.black;
        tr.alignment = TextAlignment.Left;
        tr.sortingOrder = 100;

        _hudGo.transform.SetParent(Camera.main.transform);
        UpdateHudLayout();
    }

    /// <summary>
    /// Recompute HUD position and scale so the text keeps a constant
    /// pixel-size on screen regardless of window resolution.
    /// </summary>
    private void UpdateHudLayout()
    {
        if (_hudGo == null || Camera.main == null) return;

        const float z = 3f;
        float halfH = z * Mathf.Tan(Camera.main.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * ((float)UnityEngine.Screen.width / UnityEngine.Screen.height);

        // World units per screen pixel at this Z plane
        float worldPerPx = 2f * halfH / UnityEngine.Screen.height;

        // Fixed screen-pixel margin from the top-left corner
        const float marginPx = 20f;
        float x = -halfW + marginPx * worldPerPx;
        float y = halfH - marginPx * worldPerPx;

        // Scale: keep constant pixel-size (reference = 720p)
        float scale = 720f / UnityEngine.Screen.height;

        _hudGo.transform.localPosition = new Vector3(x, y + -0.3f, z);
        _hudGo.transform.localScale = new Vector3(scale, scale, scale);
    }
}
