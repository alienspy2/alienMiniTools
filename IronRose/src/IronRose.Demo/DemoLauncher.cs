using RoseEngine;
using RoseEngine.InputSystem;
using IronRose.API;
using IronRose.Engine;

public class DemoLauncher : MonoBehaviour
{
    // ── 데모 등록: 여기에 추가하면 메뉴·키·HUD 자동 반영 ──
    private static readonly (string Label, System.Type Type)[] _builtinDemos =
    {
        ("Cornell Box",     typeof(CornellBoxDemo)),
        ("Asset Import",    typeof(AssetImportDemo)),
        ("3D Physics",      typeof(PhysicsDemo3D)),
        ("PBR",             typeof(PBRDemo)),
    };

    private static readonly KeyCode[] _numKeys =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
        KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0,
    };

    private int _currentDemo = -1;
    private string _currentLiveCodeDemo = "";
    private static Font? _hudFont;
    private static bool _isLoading;
    private GameObject? _hudGo;

    // LiveCode 데모 감지를 위한 캐시
    private int _lastLiveCodeCount = -1;

    // 핫 리로드 후 활성 데모 자동 복원용 (static → SceneManager.Clear 후에도 유지)
    private static int _activeBuiltinDemo = -1;
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
            else if (_activeBuiltinDemo >= 0 && _activeBuiltinDemo < _builtinDemos.Length)
            {
                _currentDemo = _activeBuiltinDemo;
                var (label, type) = _builtinDemos[_activeBuiltinDemo];
                var go = new GameObject(type.Name);
                go.AddComponent(type);
                Debug.Log($"[Demo] >> {label} (hot-reloaded)");
            }
            else
            {
                EnsureCamera();
            }

            CreateHud();
        }
    }

    private static string KeyLabel(int slot) => slot < 9 ? $"{slot + 1}" : "0";

    private void PrintDemoMenu()
    {
        Debug.Log("=== IronRose Demo Selector ===");
        for (int i = 0; i < _builtinDemos.Length && i < _numKeys.Length; i++)
            Debug.Log($"[{KeyLabel(i)}] {_builtinDemos[i].Label}");

        // LiveCode 데모 출력
        var liveTypes = EngineCore.LiveCodeDemoTypes;
        int liveStart = _builtinDemos.Length;
        for (int i = 0; i < liveTypes.Length && liveStart + i < _numKeys.Length; i++)
            Debug.Log($"[{KeyLabel(liveStart + i)}] {liveTypes[i].Name} (LiveCode)");

        Debug.Log("[F1] Wireframe | [F12] Screenshot | [ESC] Quit");
        Debug.Log("==============================");
    }

    public override void Update()
    {
        // 빌트인 데모 선택
        for (int i = 0; i < _builtinDemos.Length && i < _numKeys.Length; i++)
        {
            if (Input.GetKeyDown(_numKeys[i]))
                LoadDemo(i);
        }

        // LiveCode 데모 선택 (빌트인 이후 키)
        var liveTypes = EngineCore.LiveCodeDemoTypes;
        int liveStart = _builtinDemos.Length;
        for (int i = 0; i < liveTypes.Length && liveStart + i < _numKeys.Length; i++)
        {
            if (Input.GetKeyDown(_numKeys[liveStart + i]))
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

    /// <summary>공통 데모 전환: Scene Clear → DemoLauncher 재등록 → 데모 인스턴스화.</summary>
    private void SwitchDemo(int builtinIndex, System.Type? liveCodeType)
    {
        _isLoading = true;
        _activeBuiltinDemo = builtinIndex;
        _activeLiveCodeDemo = liveCodeType?.Name ?? "";

        SceneManager.Clear();

        var selectorGo = new GameObject("DemoSelector");
        var selector = selectorGo.AddComponent<DemoLauncher>();
        selector._currentDemo = builtinIndex;
        selector._currentLiveCodeDemo = _activeLiveCodeDemo;

        _isLoading = false;

        if (liveCodeType != null)
        {
            var go = new GameObject(liveCodeType.Name);
            go.AddComponent(liveCodeType);
            Debug.Log($"[Demo] >> {liveCodeType.Name}");
        }
        else if (builtinIndex >= 0 && builtinIndex < _builtinDemos.Length)
        {
            var (label, type) = _builtinDemos[builtinIndex];
            var go = new GameObject(type.Name);
            go.AddComponent(type);
            Debug.Log($"[Demo] >> {label}");
        }

        CreateHud();
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

        // 핫리로드 복원은 Scene Clear 없이 직접 인스턴스화
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
        SwitchDemo(-1, demoType);
    }

    private void LoadDemo(int demoIndex)
    {
        if (demoIndex == _currentDemo)
        {
            Debug.Log($"[Demo] Demo {demoIndex} already active");
            return;
        }
        SwitchDemo(demoIndex, null);
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
            RoseEngine.Object.DestroyImmediate(_hudGo);

        _hudGo = new GameObject("HudMenu");
        var tr = _hudGo.AddComponent<TextRenderer>();
        tr.font = _hudFont;

        // HUD 텍스트 생성 (빌트인 + LiveCode 데모)
        var hudText = "";
        for (int i = 0; i < _builtinDemos.Length && i < _numKeys.Length; i++)
            hudText += $"[{KeyLabel(i)}] {_builtinDemos[i].Label}\n";

        var liveTypes = EngineCore.LiveCodeDemoTypes;
        int liveStart = _builtinDemos.Length;
        for (int i = 0; i < liveTypes.Length && liveStart + i < _numKeys.Length; i++)
            hudText += $"[{KeyLabel(liveStart + i)}] {liveTypes[i].Name} *\n";

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
        float halfW = halfH * ((float)RoseEngine.Screen.width / RoseEngine.Screen.height);

        // World units per screen pixel at this Z plane
        float worldPerPx = 2f * halfH / RoseEngine.Screen.height;

        // Fixed screen-pixel margin from the top-left corner
        const float marginPx = 20f;
        float x = -halfW + marginPx * worldPerPx;
        float y = halfH - marginPx * worldPerPx;

        // Scale: keep constant pixel-size (reference = 720p)
        float scale = 720f / RoseEngine.Screen.height;

        _hudGo.transform.localPosition = new Vector3(x, y + -0.3f, z);
        _hudGo.transform.localScale = new Vector3(scale, scale, scale);
    }
}
