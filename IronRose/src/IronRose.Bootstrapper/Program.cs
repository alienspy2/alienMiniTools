using System;
using System.Diagnostics;
using IronRose.Contracts;

namespace IronRose.Bootstrapper
{
    class Program
    {
        private static EngineLoader? _engineLoader;
        private static IEngineCore? _engine;
        private static EngineWatcher? _engineWatcher;

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

            // 엔진 동적 로드
            _engineLoader = new EngineLoader();
            _engine = _engineLoader.LoadEngine();
            _engine.Initialize();

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
            Console.WriteLine("[Bootstrapper] Engine stopped");
        }

        static void OnEngineRebuilt(string hotBuildPath)
        {
            try
            {
                Console.WriteLine($"[DEBUG] OnEngineRebuilt START: {hotBuildPath}");
                Console.WriteLine("[Bootstrapper] Hot build complete");
                Console.WriteLine("[Bootstrapper] Unloading old engine...");

                Console.WriteLine("[DEBUG] Step 1: Setting engine to null");
                _engine = null;

                Console.WriteLine("[DEBUG] Step 2: Sleeping 100ms");
                System.Threading.Thread.Sleep(100);

                Console.WriteLine("[DEBUG] Step 3: Calling UnloadEngine");
                _engineLoader?.UnloadEngine();

                Console.WriteLine("[DEBUG] Step 4: Sleeping 200ms for GC");
                System.Threading.Thread.Sleep(200);

                Console.WriteLine("[Bootstrapper] Loading new engine from hot build...");
                Console.WriteLine($"[DEBUG] Step 5: Loading from {hotBuildPath}");

                // 새 엔진 로드 (bin-hot 폴더에서)
                _engine = _engineLoader?.LoadEngine(hotBuildPath);
                Console.WriteLine("[DEBUG] Step 6: LoadEngine returned");

                Console.WriteLine("[DEBUG] Step 7: Calling Initialize");
                _engine?.Initialize();
                Console.WriteLine("[DEBUG] Step 8: Initialize complete");

                Console.WriteLine("[Bootstrapper] ✅ HOT RELOAD SUCCESS!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bootstrapper] ❌ HOT RELOAD FAILED!");
                Console.WriteLine($"[Bootstrapper] Exception: {ex.GetType().Name}");
                Console.WriteLine($"[Bootstrapper] Message: {ex.Message}");
                Console.WriteLine($"[Bootstrapper] StackTrace:");
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

                // 엔진이 없으면 대기 (핫 리로드 중일 수 있음)
                if (_engine == null)
                {
                    System.Threading.Thread.Sleep(10);
                    continue;
                }

                // 이벤트 처리
                try
                {
                    if (!_engine.ProcessEvents())
                    {
                        Console.WriteLine("[Bootstrapper] Window closed");
                        break;
                    }

                    // 엔진 업데이트
                    _engine.Update(_deltaTime);

                    // 렌더링
                    _engine.Render();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Bootstrapper] ERROR in engine loop: {ex.Message}");
                    // 핫 리로드 중 오류는 무시하고 계속
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
