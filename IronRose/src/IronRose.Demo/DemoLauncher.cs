using UnityEngine;
using UnityEngine.InputSystem;
using IronRose.API;
using IronRose.Engine;

public class DemoLauncher : MonoBehaviour
{
    private int _currentDemo = 0;
    private string _currentLiveCodeDemo = "";
    private static Font? _hudFont;
    private static bool _isLoading;
    private GameObject? _hudGo;

    // LiveCode 데모 감지를 위한 캐시
    private int _lastLiveCodeCount = -1;

    // 핫 리로드 후 활성 데모 자동 복원용 (static → SceneManager.Clear 후에도 유지)
    private static int _activeBuiltinDemo;
    private static string _activeLiveCodeDemo = "";

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
                Debug.LogWarning($"[DemoLauncher] HUD font load failed: {ex.Message}");
            }
        }

        PrintDemoMenu();

        // LiveCode 카운트 동기화 (첫 Update에서 불필요한 CreateHud 재호출 방지)
        _lastLiveCodeCount = EngineCore.LiveCodeDemoTypes.Length;

        // 데모 복원 또는 기본 카메라 + HUD 설정
        if (!_isLoading)
        {
            // 핫 리로드 후: 이전에 활성이던 데모 자동 재시작
            // 데모가 자체 카메라를 생성하므로 EnsureCamera를 먼저 호출하면 안됨
            // (DefaultCamera가 Camera.main을 선점하여 데모 카메라가 무시됨)
            if (_activeLiveCodeDemo != "")
            {
                AutoRestoreLiveCodeDemo();
            }
            else if (_activeBuiltinDemo > 0)
            {
                _currentDemo = _activeBuiltinDemo;
                LaunchBuiltinDemo(_activeBuiltinDemo);
            }
            else
            {
                EnsureCamera();
            }

            CreateHud();
        }
    }

    private void PrintDemoMenu()
    {
        Debug.Log("=== IronRose Demo Selector ===");
        Debug.Log("[1] Cornell Box");
        Debug.Log("[2] Asset Import");
        Debug.Log("[3] Sprite Renderer");
        Debug.Log("[4] Text Renderer");
        Debug.Log("[5] 3D Physics");
        Debug.Log("[6] PBR Demo");

        // LiveCode 데모 출력
        var liveTypes = EngineCore.LiveCodeDemoTypes;
        for (int i = 0; i < liveTypes.Length && i < 4; i++)
        {
            int key = 7 + i;
            Debug.Log($"[{key}] {liveTypes[i].Name} (LiveCode)");
        }

        Debug.Log("[F1] Wireframe | [F12] Screenshot | [ESC] Quit");
        Debug.Log("==============================");
    }

    public override void Update()
    {
        // 빌트인 데모 선택
        if (Input.GetKeyDown(KeyCode.Alpha1)) LoadDemo(1);
        if (Input.GetKeyDown(KeyCode.Alpha2)) LoadDemo(2);
        if (Input.GetKeyDown(KeyCode.Alpha3)) LoadDemo(3);
        if (Input.GetKeyDown(KeyCode.Alpha4)) LoadDemo(4);
        if (Input.GetKeyDown(KeyCode.Alpha5)) LoadDemo(5);
        if (Input.GetKeyDown(KeyCode.Alpha6)) LoadDemo(6);

        // LiveCode 데모 선택 (키 7~0)
        var liveTypes = EngineCore.LiveCodeDemoTypes;
        KeyCode[] liveKeys = { KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0 };
        for (int i = 0; i < liveTypes.Length && i < liveKeys.Length; i++)
        {
            if (Input.GetKeyDown(liveKeys[i]))
                LoadLiveCodeDemo(liveTypes[i]);
        }

        // LiveCode 데모 추가/삭제 감지 → HUD 갱신
        if (liveTypes.Length != _lastLiveCodeCount)
        {
            _lastLiveCodeCount = liveTypes.Length;
            if (liveTypes.Length > 0)
                Debug.Log($"[DemoLauncher] LiveCode demos detected: {liveTypes.Length}");
            CreateHud();
        }

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

    private void AutoRestoreLiveCodeDemo()
    {
        var liveTypes = EngineCore.LiveCodeDemoTypes;
        System.Type? type = null;
        for (int i = 0; i < liveTypes.Length; i++)
        {
            if (liveTypes[i].Name == _activeLiveCodeDemo)
            { type = liveTypes[i]; break; }
        }

        if (type == null)
        {
            Debug.Log($"[Demo] LiveCode demo '{_activeLiveCodeDemo}' not found after reload");
            _activeLiveCodeDemo = "";
            return;
        }

        _currentDemo = -1;
        _currentLiveCodeDemo = _activeLiveCodeDemo;

        var go = new GameObject(type.Name);
        go.AddComponent(type);
        Debug.Log($"[Demo] >> {type.Name} (hot-reloaded)");
    }

    private void LoadLiveCodeDemo(System.Type demoType)
    {
        if (_currentLiveCodeDemo == demoType.Name)
        {
            Debug.Log($"[Demo] LiveCode demo {demoType.Name} already active");
            return;
        }

        Debug.Log($"[Demo] Loading LiveCode demo: {demoType.Name}...");
        _isLoading = true;

        // static 기억 (핫 리로드 시 복원용)
        _activeBuiltinDemo = 0;
        _activeLiveCodeDemo = demoType.Name;

        SceneManager.Clear();

        var selectorGo = new GameObject("DemoSelector");
        var selector = selectorGo.AddComponent<DemoLauncher>();
        selector._currentDemo = -1;
        selector._currentLiveCodeDemo = demoType.Name;

        _isLoading = false;

        // LiveCode MonoBehaviour를 Type 기반으로 인스턴스화
        var go = new GameObject(demoType.Name);
        go.AddComponent(demoType);
        Debug.Log($"[Demo] >> {demoType.Name} (LiveCode)");

        CreateHud();
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

        // static 기억 (핫 리로드 시 복원용)
        _activeBuiltinDemo = demoIndex;
        _activeLiveCodeDemo = "";

        // Clear current scene (except this selector)
        SceneManager.Clear();

        // Re-register self (Awake runs immediately, _isLoading prevents re-entrant LoadDemo)
        var selectorGo = new GameObject("DemoSelector");
        var selector = selectorGo.AddComponent<DemoLauncher>();
        selector._currentDemo = demoIndex;
        selector._currentLiveCodeDemo = "";

        _isLoading = false;

        LaunchBuiltinDemo(demoIndex);

        // HUD: demo menu overlay (parented to camera)
        CreateHud();
    }

    private static void LaunchBuiltinDemo(int demoIndex)
    {
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

        // 기존 HUD 즉시 제거 (Destroy는 프레임 끝까지 지연되어 중복 렌더링됨)
        if (_hudGo != null)
            UnityEngine.Object.DestroyImmediate(_hudGo);

        _hudGo = new GameObject("HudMenu");
        var tr = _hudGo.AddComponent<TextRenderer>();
        tr.font = _hudFont;

        // HUD 텍스트 생성 (빌트인 + LiveCode 데모)
        var hudText = "[1] Cornell Box\n"
                    + "[2] Asset Import\n"
                    + "[3] Sprite Renderer\n"
                    + "[4] Text Renderer\n"
                    + "[5] 3D Physics\n"
                    + "[6] PBR\n";

        var liveTypes = EngineCore.LiveCodeDemoTypes;
        string[] keyLabels = { "7", "8", "9", "0" };
        for (int i = 0; i < liveTypes.Length && i < keyLabels.Length; i++)
        {
            hudText += $"[{keyLabels[i]}] {liveTypes[i].Name} *\n";
        }

        hudText += "[F1] Wireframe | [F12] Screenshot\n"
                 + "[ESC] Quit";

        tr.text = hudText;
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
