using System;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace IronRose.Engine
{
    class Program
    {
        private static EngineCore? _engine;
        private static IWindow? _window;
        private static IInputContext? _inputContext;

        // FPS 카운터
        private static int _frameCount = 0;
        private static double _fpsTimer = 0;

        static void Main(string[] args)
        {
            Console.WriteLine("[IronRose] Engine Starting...");

            var options = WindowOptions.DefaultVulkan;
            options.Size = new Vector2D<int>(1280, 720);
            options.Position = new Vector2D<int>(100, 100);
            options.Title = "IronRose Engine";
            options.UpdatesPerSecond = 60;
            options.FramesPerSecond = 60;
            options.API = GraphicsAPI.None; // Veldrid가 Vulkan 직접 관리

            _window = Window.Create(options);
            _window.Load += OnLoad;
            _window.Update += OnUpdate;
            _window.Render += OnRender;
            _window.Closing += OnClosing;

            _window.Run();

            Console.WriteLine("[IronRose] Engine stopped");
        }

        static void OnLoad()
        {
            Console.WriteLine($"[IronRose] Window created: {_window!.Size.X}x{_window.Size.Y}");

            // 입력 시스템 초기화
            _inputContext = _window.CreateInput();
            UnityEngine.Input.Initialize(_inputContext);

            // 엔진 생성 및 초기화
            _engine = new EngineCore();
            _engine.Initialize(_window);
        }

        static void OnUpdate(double deltaTime)
        {
            // FPS 카운터
            _frameCount++;
            _fpsTimer += deltaTime;
            if (_fpsTimer >= 1.0)
            {
                var fps = _frameCount / _fpsTimer;
                Console.WriteLine($"[IronRose] FPS: {fps:F2} | Frame Time: {deltaTime * 1000:F2}ms");
                _frameCount = 0;
                _fpsTimer = 0;
            }

            // 입력 상태 갱신
            UnityEngine.Input.Update();

            // 엔진 업데이트
            try
            {
                _engine!.Update(deltaTime);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IronRose] ERROR: {ex.Message}");
            }
        }

        static void OnRender(double deltaTime)
        {
            try
            {
                _engine!.Render();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IronRose] ERROR: {ex.Message}");
            }
        }

        static void OnClosing()
        {
            Console.WriteLine("[IronRose] Shutting down...");
            _inputContext?.Dispose();
            _engine?.Shutdown();
        }
    }
}
