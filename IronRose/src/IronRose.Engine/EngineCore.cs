using IronRose.Contracts;
using System;
using System.Linq;

namespace IronRose.Engine
{
    public class EngineCore : IEngineCore
    {
        private object? _graphicsManager;
        private object? _scriptDomain;
        private object? _window;
        private int _frameCount = 0;

        public void Initialize(object? windowHandle = null)
        {
            Console.WriteLine("╔════════════════════════════════════════════════════╗");
            Console.WriteLine("║ ✨ Engine.dll Hot-Reloadable! ✨                  ║");
            Console.WriteLine("╚════════════════════════════════════════════════════╝");
            Console.WriteLine("[Engine] EngineCore initializing...");

            _window = windowHandle;

            // 리플렉션으로 GraphicsManager 생성
            var renderingAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "IronRose.Rendering");

            if (renderingAssembly != null)
            {
                var graphicsManagerType = renderingAssembly.GetType("IronRose.Rendering.GraphicsManager");
                if (graphicsManagerType != null)
                {
                    _graphicsManager = Activator.CreateInstance(graphicsManagerType);

                    // Initialize(window) 호출
                    if (_window != null)
                    {
                        Console.WriteLine($"[Engine] Passing window to GraphicsManager: {_window.GetType().Name}");
                        var initMethod = graphicsManagerType.GetMethod("Initialize");
                        initMethod?.Invoke(_graphicsManager, new[] { _window });
                        Console.WriteLine("[Engine] GraphicsManager initialized with existing window");
                    }
                    else
                    {
                        Console.WriteLine("[Engine] No window provided, GraphicsManager will create new one");
                        var initMethod = graphicsManagerType.GetMethod("Initialize");
                        initMethod?.Invoke(_graphicsManager, new object?[] { null });
                    }
                }
            }
            else
            {
                Console.WriteLine("[Engine] ERROR: Rendering assembly not found");
            }
        }

        public bool ProcessEvents()
        {
            // Bootstrapper가 처리하므로 항상 true 반환
            return true;
        }

        public void Update(double deltaTime)
        {
            // TODO: GameObject/Component 업데이트 (Phase 3에서 구현)
        }

        public void Render()
        {
            if (_graphicsManager == null) return;

            // 스크린샷 자동 캡처 (첫 프레임, 60프레임, 그리고 매 300프레임)
            _frameCount++;
            if (_frameCount == 1 || _frameCount == 60 || _frameCount % 300 == 0)
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var filename = $"logs/screenshot_frame{_frameCount}_{timestamp}.png";

                var requestMethod = _graphicsManager.GetType().GetMethod("RequestScreenshot");
                requestMethod?.Invoke(_graphicsManager, new object[] { filename });
            }

            var method = _graphicsManager.GetType().GetMethod("Render");
            method?.Invoke(_graphicsManager, null);
        }

        public void Shutdown()
        {
            Console.WriteLine("[Engine] EngineCore shutting down...");

            if (_graphicsManager != null)
            {
                var method = _graphicsManager.GetType().GetMethod("Dispose");
                method?.Invoke(_graphicsManager, null);
            }
        }
    }
}
