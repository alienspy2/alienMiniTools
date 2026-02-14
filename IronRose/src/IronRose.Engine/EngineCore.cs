using IronRose.API;
using IronRose.Rendering;
using IronRose.Scripting;
using UnityEngine;
using Silk.NET.Input;
using Silk.NET.Windowing;
using System;
using System.IO;

namespace IronRose.Engine
{
    public class EngineCore
    {
        private GraphicsManager? _graphicsManager;
        private RenderSystem? _renderSystem;
        private IWindow? _window;
        private int _frameCount = 0;

        // 스크립팅
        private ScriptCompiler? _compiler;
        private ScriptDomain? _scriptDomain;
        private FileSystemWatcher? _liveCodeWatcher;
        private bool _reloadRequested = false;
        private DateTime _lastReloadTime = DateTime.MinValue;

        // 디버깅 스크린캡처 (기본 off)
        public bool ScreenCaptureEnabled { get; set; } = false;

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
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Engine] WARNING: RenderSystem init failed: {ex.Message}");
                    Console.WriteLine("[Engine] Falling back to clear-only rendering");
                    _renderSystem = null;
                }
            }

            // Screen 치수 설정
            UnityEngine.Screen.SetSize(_window.Size.X, _window.Size.Y);
            _window.Resize += size =>
            {
                if (size.X > 0 && size.Y > 0)
                    UnityEngine.Screen.SetSize(size.X, size.Y);
            };

            // 플러그인 API 연결
            IronRose.API.Screen.SetClearColorImpl = (r, g, b) => _graphicsManager.SetClearColor(r, g, b);

            // LiveCode 핫 리로드 초기화
            InitializeLiveCode();
        }

        private void InitializeLiveCode()
        {
            Console.WriteLine("[Engine] Initializing LiveCode hot-reload...");

            _compiler = new ScriptCompiler();
            _compiler.AddReference(typeof(IronRose.API.Screen)); // IronRose.Contracts
            _compiler.AddReference(typeof(EngineCore).Assembly.Location); // IronRose.Engine
            _scriptDomain = new ScriptDomain();

            var monoBehaviourType = typeof(MonoBehaviour);
            _scriptDomain.SetTypeFilter(type => !monoBehaviourType.IsAssignableFrom(type));

            string liveCodePath = Path.GetFullPath("LiveCode");
            if (!Directory.Exists(liveCodePath))
            {
                Directory.CreateDirectory(liveCodePath);
                Console.WriteLine($"[Engine] Created LiveCode directory: {liveCodePath}");
            }

            // 초기 LiveCode 컴파일
            CompileAndLoadLiveCode(liveCodePath);

            // FileSystemWatcher
            _liveCodeWatcher = new FileSystemWatcher(liveCodePath, "*.cs");
            _liveCodeWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
            _liveCodeWatcher.Changed += OnLiveCodeChanged;
            _liveCodeWatcher.Created += OnLiveCodeChanged;
            _liveCodeWatcher.Deleted += OnLiveCodeChanged;
            _liveCodeWatcher.Renamed += (s, e) => OnLiveCodeChanged(s, e);
            _liveCodeWatcher.EnableRaisingEvents = true;

            Console.WriteLine("[Engine] FileSystemWatcher active on LiveCode/");
        }

        private void CompileAndLoadLiveCode(string liveCodePath)
        {
            var csFiles = Directory.GetFiles(liveCodePath, "*.cs");
            if (csFiles.Length == 0)
                return;

            Console.WriteLine($"[Engine] Compiling {csFiles.Length} LiveCode files...");

            var result = _compiler!.CompileFromFiles(csFiles, "LiveCode");
            if (result.Success && result.AssemblyBytes != null)
            {
                if (_scriptDomain!.IsLoaded)
                    _scriptDomain.Reload(result.AssemblyBytes);
                else
                    _scriptDomain.LoadScripts(result.AssemblyBytes);

                // LiveCode MonoBehaviour 등록
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

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (!monoBehaviourType.IsAssignableFrom(type)) continue;

                try
                {
                    var go = new GameObject(type.Name);
                    var behaviour = (MonoBehaviour)go.AddComponent(type);
                    SceneManager.RegisterBehaviour(behaviour);
                    Console.WriteLine($"[Engine] LiveCode: {type.Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Engine] ERROR registering {type.Name}: {ex.Message}");
                }
            }
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
            UnityEngine.InputSystem.InputSystem.Update();

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

                // LiveCode 씬 초기화
                SceneManager.Clear();

                string liveCodePath = Path.GetFullPath("LiveCode");
                CompileAndLoadLiveCode(liveCodePath);
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
            _liveCodeWatcher?.Dispose();
            _renderSystem?.Dispose();
            _graphicsManager?.Dispose();
        }
    }
}
