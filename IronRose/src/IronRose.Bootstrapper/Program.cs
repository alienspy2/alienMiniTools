using System;
using System.Diagnostics;
using IronRose.Contracts;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace IronRose.Bootstrapper
{
    class Program
    {
        private static EngineLoader? _engineLoader;
        private static IEngineCore? _engine;
        private static EngineWatcher? _engineWatcher;

        // 윈도우 (Bootstrapper가 관리)
        private static Sdl2Window? _window;

        private static Stopwatch _timer = new Stopwatch();
        private static double _lastTime = 0;
        private static double _deltaTime = 0;

        // FPS 카운터
        private static int _frameCount = 0;
        private static double _fpsTimer = 0;
        private static double _currentFps = 0;

        static void Main(string[] args)
        {
            Console.WriteLine("[Bootstrapper] IronRose Engine Starting...");
            Console.WriteLine("[Bootstrapper] === Everything is Hot-Reloadable ===");

            // 윈도우 생성 (Bootstrapper가 관리)
            CreateWindow();

            // 엔진 동적 로드
            _engineLoader = new EngineLoader();
            _engine = _engineLoader.LoadEngine();
            _engine.Initialize(_window);

            // 엔진 파일 감시 및 핫 리로드 설정
            _engineWatcher = new EngineWatcher();
            _engineWatcher.OnEngineRebuilt += OnEngineRebuilt;
            _engineWatcher.Enable();

            // 타이머 시작
            _timer.Start();
            _lastTime = _timer.Elapsed.TotalSeconds;

            // 메인 루프
            MainLoop();

            // 정리
            Console.WriteLine("[Bootstrapper] Shutting down...");
            _engineWatcher?.Dispose();
            _engine?.Shutdown();
            _window?.Close();
            Console.WriteLine("[Bootstrapper] Engine stopped");
        }

        static void CreateWindow()
        {
            Console.WriteLine("[Bootstrapper] Creating window...");

            WindowCreateInfo windowCI = new WindowCreateInfo()
            {
                X = 100,
                Y = 100,
                WindowWidth = 1280,
                WindowHeight = 720,
                WindowTitle = "IronRose Engine"
            };

            _window = VeldridStartup.CreateWindow(ref windowCI);
            Console.WriteLine($"[Bootstrapper] Window created: {_window.Width}x{_window.Height}");
        }

        static void OnEngineRebuilt(string hotBuildPath)
        {
            try
            {
                Console.WriteLine($"[Bootstrapper] Hot build complete: {hotBuildPath}");
                Console.WriteLine("[Bootstrapper] Unloading old engine...");

                _engine = null;
                System.Threading.Thread.Sleep(100);

                _engineLoader?.UnloadEngine();
                System.Threading.Thread.Sleep(200);

                Console.WriteLine("[Bootstrapper] Loading new engine (keeping window)...");

                // 새 엔진 로드 (같은 윈도우 재사용)
                _engine = _engineLoader?.LoadEngine(hotBuildPath);
                _engine?.Initialize(_window);

                Console.WriteLine("[Bootstrapper] ✅ HOT RELOAD SUCCESS - Window preserved!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bootstrapper] ❌ HOT RELOAD FAILED: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        static void MainLoop()
        {
            Console.WriteLine("[Bootstrapper] Entering main loop");

            while (true)
            {
                // 델타 타임 계산
                double currentTime = _timer.Elapsed.TotalSeconds;
                _deltaTime = currentTime - _lastTime;
                _lastTime = currentTime;

                // FPS 카운터
                _frameCount++;
                _fpsTimer += _deltaTime;
                if (_fpsTimer >= 1.0)
                {
                    _currentFps = _frameCount / _fpsTimer;
                    Console.WriteLine($"[Bootstrapper] FPS: {_currentFps:F2} | Frame Time: {_deltaTime * 1000:F2}ms");
                    _frameCount = 0;
                    _fpsTimer = 0;
                }

                // 윈도우 이벤트 처리 (Bootstrapper가 직접)
                if (_window != null && !_window.Exists)
                {
                    Console.WriteLine("[Bootstrapper] Window closed by user");
                    break;
                }

                _window?.PumpEvents();

                // 엔진이 없으면 대기 (핫 리로드 중)
                if (_engine == null)
                {
                    System.Threading.Thread.Sleep(10);
                    continue;
                }

                // 엔진 업데이트 및 렌더링
                try
                {
                    _engine.Update(_deltaTime);
                    _engine.Render();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Bootstrapper] ERROR: {ex.Message}");
                    System.Threading.Thread.Sleep(50);
                    continue;
                }

                // 프레임 제한 (60 FPS)
                double frameTime = _timer.Elapsed.TotalSeconds - currentTime;
                double targetFrameTime = 1.0 / 60.0;
                if (frameTime < targetFrameTime)
                {
                    System.Threading.Thread.Sleep((int)((targetFrameTime - frameTime) * 1000));
                }
            }
        }
    }
}
