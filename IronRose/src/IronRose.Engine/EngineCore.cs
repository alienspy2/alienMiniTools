using IronRose.API;
using IronRose.AssetPipeline;
using IronRose.Rendering;
using IronRose.Scripting;
using RoseEngine;
using Silk.NET.Input;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IronRose.Engine
{
    public class EngineCore
    {
        private GraphicsManager? _graphicsManager;
        private RenderSystem? _renderSystem;
        private IWindow? _window;
        private int _frameCount = 0;
        private AssetDatabase? _assetDatabase;
        private PhysicsManager? _physicsManager;

        // Fixed timestep (물리)
        private const float FixedDeltaTime = 1f / 50f;
        private double _fixedAccumulator = 0;

        // 스크립팅
        private ScriptCompiler? _compiler;
        private ScriptDomain? _scriptDomain;
        private readonly List<string> _liveCodePaths = new();
        private readonly List<FileSystemWatcher> _liveCodeWatchers = new();
        private bool _reloadRequested = false;
        private DateTime _lastReloadTime = DateTime.MinValue;
        private readonly Dictionary<string, string> _savedHotReloadStates = new();

        // 디버깅 스크린캡처 (기본 off)
        public bool ScreenCaptureEnabled { get; set; } = false;

        // 핫 리로드 후 씬 복원 콜백
        public Action? OnAfterReload { get; set; }

        // LiveCode에서 발견된 MonoBehaviour 데모 타입 목록 (DemoLauncher에서 참조)
        public static Type[] LiveCodeDemoTypes { get; private set; } = Array.Empty<Type>();

        public void Initialize(IWindow window)
        {
            Console.WriteLine("[Engine] EngineCore initializing...");

            _window = window;

            // Application 초기화
            Application.isPlaying = true;
            Application.QuitAction = () => _window.Close();

            // 입력 시스템 초기화
            var inputContext = _window.CreateInput();
            Input.Initialize(inputContext);

            _graphicsManager = new GraphicsManager();
            Console.WriteLine($"[Engine] Passing window to GraphicsManager: {_window.GetType().Name}");
            _graphicsManager.Initialize(_window);
            Console.WriteLine("[Engine] GraphicsManager initialized");

            // RenderSystem 초기화
            if (_graphicsManager.Device != null)
            {
                try
                {
                    _renderSystem = new RenderSystem();
                    _renderSystem.Initialize(_graphicsManager.Device);
                    Console.WriteLine("[Engine] RenderSystem initialized");
                    RoseEngine.RenderSettings.postProcessing = _renderSystem.PostProcessing;

                    // 리사이즈 이벤트 → RenderSystem (GBuffer, HDR, PostProcessing 재생성)
                    _graphicsManager.Resized += (w, h) => _renderSystem?.Resize(w, h);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Engine] WARNING: RenderSystem init failed: {ex.Message}");
                    Console.WriteLine("[Engine] Falling back to clear-only rendering");
                    _renderSystem = null;
                }
            }

            // Screen 치수 설정
            RoseEngine.Screen.SetSize(_window.Size.X, _window.Size.Y);
            _window.Resize += size =>
            {
                if (size.X > 0 && size.Y > 0)
                    RoseEngine.Screen.SetSize(size.X, size.Y);
            };

            // 플러그인 API 연결
            IronRose.API.Screen.SetClearColorImpl = (r, g, b) => _graphicsManager.SetClearColor(r, g, b);

            // PhysicsManager 초기화
            _physicsManager = new PhysicsManager();
            _physicsManager.Initialize();

            // AssetDatabase 초기화
            _assetDatabase = new AssetDatabase();
            string assetsPath = Path.GetFullPath("Assets");
            if (Directory.Exists(assetsPath))
            {
                _assetDatabase.ScanAssets(assetsPath);
            }
            RoseEngine.Resources.SetAssetDatabase(_assetDatabase);

            // LiveCode 핫 리로드 초기화
            InitializeLiveCode();
        }

        private void InitializeLiveCode()
        {
            Console.WriteLine("[Engine] Initializing LiveCode hot-reload...");

            _compiler = new ScriptCompiler();
            _compiler.AddReference(typeof(IronRose.API.Screen)); // IronRose.Contracts
            _compiler.AddReference(typeof(EngineCore).Assembly.Location); // IronRose.Engine
            _compiler.AddReference(typeof(PostProcessStack).Assembly.Location); // IronRose.Rendering
            _compiler.AddReference(typeof(IHotReloadable).Assembly.Location); // IronRose.Scripting
            _scriptDomain = new ScriptDomain();

            var monoBehaviourType = typeof(MonoBehaviour);
            _scriptDomain.SetTypeFilter(type => !monoBehaviourType.IsAssignableFrom(type));

            // LiveCode 디렉토리 탐색: 루트 LiveCode/ + src/*/LiveCode/
            FindLiveCodeDirectories();

            foreach (var path in _liveCodePaths)
            {
                var watcher = new FileSystemWatcher(path, "*.cs");
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                watcher.Changed += OnLiveCodeChanged;
                watcher.Created += OnLiveCodeChanged;
                watcher.Deleted += OnLiveCodeChanged;
                watcher.Renamed += (s, e) => OnLiveCodeChanged(s, e);
                watcher.EnableRaisingEvents = true;
                _liveCodeWatchers.Add(watcher);
                Console.WriteLine($"[Engine] FileSystemWatcher active on {path}");
            }

            // 초기 컴파일
            CompileAllLiveCode();
        }

        private void FindLiveCodeDirectories()
        {
            // src/*/LiveCode/ 탐색 (프로젝트별 LiveCode 폴더만 사용)
            string[] searchRoots = { ".", "..", "../.." };
            foreach (var root in searchRoots)
            {
                string srcDir = Path.GetFullPath(Path.Combine(root, "src"));
                if (!Directory.Exists(srcDir)) continue;

                foreach (var projectDir in Directory.GetDirectories(srcDir))
                {
                    string liveCodeDir = Path.Combine(projectDir, "LiveCode");
                    if (!Directory.Exists(liveCodeDir)) continue;

                    string fullPath = Path.GetFullPath(liveCodeDir);
                    if (!_liveCodePaths.Contains(fullPath))
                    {
                        _liveCodePaths.Add(fullPath);
                        Console.WriteLine($"[Engine] Found LiveCode directory: {fullPath}");
                    }
                }
                break; // src/ 찾으면 중복 탐색 방지
            }

            // LiveCode 디렉토리가 하나도 없으면 Demo/LiveCode 생성
            if (_liveCodePaths.Count == 0)
            {
                string fallback = Path.GetFullPath("LiveCode");
                Directory.CreateDirectory(fallback);
                _liveCodePaths.Add(fallback);
                Console.WriteLine($"[Engine] Created LiveCode directory: {fallback}");
            }
        }

        private void CompileAllLiveCode()
        {
            var csFiles = _liveCodePaths
                .Where(Directory.Exists)
                .SelectMany(p => Directory.GetFiles(p, "*.cs"))
                .ToArray();

            if (csFiles.Length == 0)
                return;

            Console.WriteLine($"[Engine] Compiling {csFiles.Length} LiveCode files from {_liveCodePaths.Count} directories...");

            var result = _compiler!.CompileFromFiles(csFiles, "LiveCode");
            if (result.Success && result.AssemblyBytes != null)
            {
                if (_scriptDomain!.IsLoaded)
                    _scriptDomain.Reload(result.AssemblyBytes);
                else
                    _scriptDomain.LoadScripts(result.AssemblyBytes);

                RegisterLiveCodeBehaviours();

                Console.WriteLine("[Engine] LiveCode loaded!");
            }
            else
            {
                Console.WriteLine("[Engine] LiveCode compilation failed");
            }
        }

        private void RegisterLiveCodeBehaviours()
        {
            var monoBehaviourType = typeof(MonoBehaviour);
            var types = _scriptDomain!.GetLoadedTypes();
            var demoTypes = new List<Type>();

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (!monoBehaviourType.IsAssignableFrom(type)) continue;

                demoTypes.Add(type);
                Console.WriteLine($"[Engine] LiveCode demo detected: {type.Name}");
            }

            LiveCodeDemoTypes = demoTypes.ToArray();
            Console.WriteLine($"[Engine] LiveCode demos available: {LiveCodeDemoTypes.Length}");
        }

        private void SaveHotReloadableState()
        {
            _savedHotReloadStates.Clear();
            foreach (var go in SceneManager.AllGameObjects)
            {
                foreach (var comp in go.InternalComponents)
                {
                    if (comp is IHotReloadable reloadable)
                    {
                        try
                        {
                            var state = reloadable.SerializeState();
                            _savedHotReloadStates[comp.GetType().Name] = state;
                            Console.WriteLine($"[Engine] State saved: {comp.GetType().Name}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Engine] State save failed for {comp.GetType().Name}: {ex.Message}");
                        }
                    }
                }
            }
        }

        private void RestoreHotReloadableState()
        {
            if (_savedHotReloadStates.Count == 0) return;

            foreach (var go in SceneManager.AllGameObjects)
            {
                foreach (var comp in go.InternalComponents)
                {
                    if (comp is IHotReloadable reloadable)
                    {
                        string typeName = comp.GetType().Name;
                        if (_savedHotReloadStates.TryGetValue(typeName, out var state))
                        {
                            try
                            {
                                reloadable.DeserializeState(state);
                                Console.WriteLine($"[Engine] State restored: {typeName}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Engine] State restore failed for {typeName}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            _savedHotReloadStates.Clear();
        }

        private void OnLiveCodeChanged(object sender, FileSystemEventArgs e)
        {
            var now = DateTime.Now;
            if ((now - _lastReloadTime).TotalSeconds < 1.0)
                return;

            _lastReloadTime = now;
            _reloadRequested = true;
            Console.WriteLine($"[Engine] LiveCode changed: {e.Name} -> reload scheduled");
        }

        public void Update(double deltaTime)
        {
            // 입력은 항상 갱신 (pause 상태에서도 키 입력 감지 필요)
            Input.Update();
            RoseEngine.InputSystem.InputSystem.Update();

            // 엔진 레벨 키 처리 (MonoBehaviour 밖에서 동작)
            ProcessEngineKeys();

            // pause 시 게임 로직 완전 중단
            if (Application.isPaused)
                return;

            // 핫 리로드 요청 처리 (메인 스레드에서)
            if (_reloadRequested)
            {
                _reloadRequested = false;
                Console.WriteLine("[Engine] Hot reloading LiveCode...");

                // 1. IHotReloadable 상태 저장 (Clear 전)
                SaveHotReloadableState();

                // 2. 씬 초기화 + 리컴파일
                SceneManager.Clear();
                CompileAllLiveCode();

                // 3. 씬 복원 (DemoLauncher가 활성 데모를 자동 재시작)
                OnAfterReload?.Invoke();

                // 4. IHotReloadable 상태 복원 (데모 재시작 후)
                RestoreHotReloadableState();
            }

            // Fixed timestep 물리 루프
            _fixedAccumulator += deltaTime;
            while (_fixedAccumulator >= FixedDeltaTime)
            {
                Time.fixedDeltaTime = FixedDeltaTime;
                Time.fixedTime += FixedDeltaTime;
                _physicsManager?.FixedUpdate(FixedDeltaTime);
                SceneManager.FixedUpdate(FixedDeltaTime);
                _fixedAccumulator -= FixedDeltaTime;
            }

            // Legacy 스크립트 Update 호출
            _scriptDomain?.Update();

            // MonoBehaviour SceneManager Update 호출
            SceneManager.Update((float)deltaTime);
        }

        private void ProcessEngineKeys()
        {
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.P))
            {
                if (Application.isPaused)
                    Application.Resume();
                else
                    Application.Pause();
            }

            // F12: Screenshot
            if (Input.GetKeyDown(KeyCode.F12) && _graphicsManager != null)
            {
                var dir = Path.Combine("Screenshots");
                Directory.CreateDirectory(dir);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                var filename = Path.Combine(dir, $"screenshot_{timestamp}.png");
                _graphicsManager.RequestScreenshot(filename);
            }
        }

        public void Render()
        {
            if (_graphicsManager == null) return;

            // 스크린샷 자동 캡처
            _frameCount++;
            if (ScreenCaptureEnabled && (_frameCount == 1 || _frameCount == 60 || _frameCount % 300 == 0))
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var filename = Path.Combine("logs", $"screenshot_frame{_frameCount}_{timestamp}.png");
                _graphicsManager.RequestScreenshot(filename);
            }

            _graphicsManager.BeginFrame();

            // RenderSystem: 3D mesh rendering
            if (_renderSystem != null && _graphicsManager.CommandList != null)
            {
                _renderSystem.Render(
                    _graphicsManager.CommandList,
                    Camera.main,
                    _graphicsManager.AspectRatio);
            }

            _graphicsManager.EndFrame();
        }

        public void Shutdown()
        {
            Console.WriteLine("[Engine] EngineCore shutting down...");
            Application.isPlaying = false;
            Application.QuitAction = null;
            SceneManager.Clear();
            _assetDatabase?.UnloadAll();
            _physicsManager?.Dispose();
            foreach (var watcher in _liveCodeWatchers)
                watcher.Dispose();
            _liveCodeWatchers.Clear();
            _renderSystem?.Dispose();
            _graphicsManager?.Dispose();
        }
    }
}
