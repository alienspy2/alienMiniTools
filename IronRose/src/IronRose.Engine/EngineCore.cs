using IronRose.API;
using IronRose.Rendering;
using IronRose.Scripting;
using UnityEngine;
using Veldrid.Sdl2;
using System;
using System.IO;
using System.Linq;

namespace IronRose.Engine
{
    public class EngineCore
    {
        private GraphicsManager? _graphicsManager;
        private Sdl2Window? _window;
        private int _frameCount = 0;

        // LiveCode 스크립팅
        private ScriptCompiler? _compiler;
        private ScriptDomain? _scriptDomain;
        private FileSystemWatcher? _liveCodeWatcher;
        private bool _reloadRequested = false;
        private DateTime _lastReloadTime = DateTime.MinValue;

        // 디버깅 스크린캡처 (기본 off)
        public bool ScreenCaptureEnabled { get; set; } = false;

        public void Initialize(Sdl2Window? window = null)
        {
            Console.WriteLine("[Engine] EngineCore initializing...");

            _window = window;

            _graphicsManager = new GraphicsManager();

            if (_window != null)
            {
                Console.WriteLine($"[Engine] Passing window to GraphicsManager: {_window.GetType().Name}");
                _graphicsManager.Initialize(_window);
                Console.WriteLine("[Engine] GraphicsManager initialized with existing window");
            }
            else
            {
                Console.WriteLine("[Engine] No window provided, GraphicsManager will create new one");
                _graphicsManager.Initialize(null);
            }

            // 플러그인 API 연결
            Screen.SetClearColorImpl = (r, g, b) => _graphicsManager.SetClearColor(r, g, b);

            // LiveCode 스크립팅 초기화
            InitializeScripting();
        }

        private void InitializeScripting()
        {
            Console.WriteLine("[Engine] Initializing LiveCode scripting...");

            _compiler = new ScriptCompiler();
            _compiler.AddReference(typeof(Screen)); // IronRose.Contracts (플러그인 API)
            _compiler.AddReference(typeof(EngineCore).Assembly.Location); // IronRose.Engine (UnityEngine 타입)
            _scriptDomain = new ScriptDomain();

            // MonoBehaviour 타입은 ScriptDomain의 legacy 인스턴스화에서 제외
            var monoBehaviourType = typeof(MonoBehaviour);
            _scriptDomain.SetTypeFilter(type => !monoBehaviourType.IsAssignableFrom(type));

            // LiveCode 디렉토리 확인
            string liveCodePath = Path.GetFullPath("LiveCode");
            if (!Directory.Exists(liveCodePath))
            {
                Console.WriteLine($"[Engine] LiveCode directory not found: {liveCodePath}");
                return;
            }

            Console.WriteLine($"[Engine] LiveCode directory: {liveCodePath}");

            // 초기 컴파일 및 로드
            CompileAndLoadScripts(liveCodePath);

            // FileSystemWatcher 설정
            _liveCodeWatcher = new FileSystemWatcher(liveCodePath, "*.cs");
            _liveCodeWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
            _liveCodeWatcher.Changed += OnLiveCodeChanged;
            _liveCodeWatcher.Created += OnLiveCodeChanged;
            _liveCodeWatcher.Deleted += OnLiveCodeChanged;
            _liveCodeWatcher.EnableRaisingEvents = true;

            Console.WriteLine("[Engine] FileSystemWatcher active on LiveCode/");
        }

        private void CompileAndLoadScripts(string liveCodePath)
        {
            var csFiles = Directory.GetFiles(liveCodePath, "*.cs");
            if (csFiles.Length == 0)
            {
                Console.WriteLine("[Engine] No .cs files found in LiveCode/");
                return;
            }

            Console.WriteLine($"[Engine] Compiling {csFiles.Length} LiveCode files...");

            var result = _compiler!.CompileFromFiles(csFiles, "LiveCode");
            if (result.Success && result.AssemblyBytes != null)
            {
                // 기존 MonoBehaviour 정리 (OnDestroy 호출)
                SceneManager.Clear();

                if (_scriptDomain!.IsLoaded)
                    _scriptDomain.Reload(result.AssemblyBytes);
                else
                    _scriptDomain.LoadScripts(result.AssemblyBytes);

                // MonoBehaviour 등록
                RegisterMonoBehaviours();

                Console.WriteLine("[Engine] ✅ LiveCode loaded successfully!");
            }
            else
            {
                Console.WriteLine("[Engine] ❌ LiveCode compilation failed");
            }
        }

        private void RegisterMonoBehaviours()
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
                    Console.WriteLine($"[Engine] Registered MonoBehaviour: {type.Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Engine] ERROR registering {type.Name}: {ex.Message}");
                }
            }
        }

        private void OnLiveCodeChanged(object sender, FileSystemEventArgs e)
        {
            // 디바운싱 (1초 이내 중복 이벤트 무시)
            var now = DateTime.Now;
            if ((now - _lastReloadTime).TotalSeconds < 1.0)
                return;

            _lastReloadTime = now;
            _reloadRequested = true;
            Console.WriteLine($"[Engine] 🔄 LiveCode changed: {e.Name} → reload scheduled");
        }

        public void Update(double deltaTime)
        {
            // 핫 리로드 요청 처리 (메인 스레드에서)
            if (_reloadRequested)
            {
                _reloadRequested = false;
                string liveCodePath = Path.GetFullPath("LiveCode");
                Console.WriteLine("[Engine] 🔄 Hot reloading LiveCode...");
                CompileAndLoadScripts(liveCodePath);
            }

            // Legacy 스크립트 Update 호출
            _scriptDomain?.Update();

            // MonoBehaviour SceneManager Update 호출
            SceneManager.Update((float)deltaTime);
        }

        public void Render()
        {
            if (_graphicsManager == null) return;

            // 스크린샷 자동 캡처 (첫 프레임, 60프레임, 그리고 매 300프레임)
            _frameCount++;
            if (ScreenCaptureEnabled && (_frameCount == 1 || _frameCount == 60 || _frameCount % 300 == 0))
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var filename = Path.Combine("logs", $"screenshot_frame{_frameCount}_{timestamp}.png");
                _graphicsManager.RequestScreenshot(filename);
            }

            _graphicsManager.Render();
        }

        public void Shutdown()
        {
            Console.WriteLine("[Engine] EngineCore shutting down...");
            SceneManager.Clear();
            _liveCodeWatcher?.Dispose();
            _graphicsManager?.Dispose();
        }
    }
}
