using IronRose.Rendering;
using Veldrid.Sdl2;
using System;

namespace IronRose.Engine
{
    public class EngineCore
    {
        private GraphicsManager? _graphicsManager;
        private Sdl2Window? _window;
        private int _frameCount = 0;

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
                _graphicsManager.RequestScreenshot(filename);
            }

            _graphicsManager.Render();
        }

        public void Shutdown()
        {
            Console.WriteLine("[Engine] EngineCore shutting down...");
            _graphicsManager?.Dispose();
        }
    }
}
