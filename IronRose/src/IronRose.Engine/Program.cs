using System;
using System.Diagnostics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace IronRose.Engine
{
    class Program
    {
        private static EngineCore? _engine;

        // 윈도우
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
            Console.WriteLine("[IronRose] Engine Starting...");

            // 윈도우 생성
            CreateWindow();

            // 엔진 생성
            _engine = new EngineCore();
            _engine.Initialize(_window);

            // 타이머 시작
            _timer.Start();
            _lastTime = _timer.Elapsed.TotalSeconds;

            // 메인 루프
            MainLoop();

            // 정리
            Console.WriteLine("[IronRose] Shutting down...");
            _engine.Shutdown();
            _window?.Close();
            Console.WriteLine("[IronRose] Engine stopped");
        }

        static void CreateWindow()
        {
            Console.WriteLine("[IronRose] Creating window...");

            WindowCreateInfo windowCI = new WindowCreateInfo()
            {
                X = 100,
                Y = 100,
                WindowWidth = 1280,
                WindowHeight = 720,
                WindowTitle = "IronRose Engine"
            };

            _window = VeldridStartup.CreateWindow(ref windowCI);
            Console.WriteLine($"[IronRose] Window created: {_window.Width}x{_window.Height}");
        }

        static void MainLoop()
        {
            Console.WriteLine("[IronRose] Entering main loop");

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
                    Console.WriteLine($"[IronRose] FPS: {_currentFps:F2} | Frame Time: {_deltaTime * 1000:F2}ms");
                    _frameCount = 0;
                    _fpsTimer = 0;
                }

                // 윈도우 이벤트 처리
                if (_window != null && !_window.Exists)
                {
                    Console.WriteLine("[IronRose] Window closed by user");
                    break;
                }

                _window?.PumpEvents();

                // 엔진 업데이트 및 렌더링
                try
                {
                    _engine!.Update(_deltaTime);
                    _engine.Render();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[IronRose] ERROR: {ex.Message}");
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
