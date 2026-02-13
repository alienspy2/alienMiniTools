using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using System;

namespace IronRose.Rendering
{
    public class GraphicsManager
    {
        private GraphicsDevice? _graphicsDevice;
        private CommandList? _commandList;
        private Sdl2Window? _window;

        public void Initialize()
        {
            Console.WriteLine("[Renderer] Initializing graphics device...");

            // Veldrid.Sdl2 윈도우 생성
            WindowCreateInfo windowCI = new WindowCreateInfo()
            {
                X = 100,
                Y = 100,
                WindowWidth = 1280,
                WindowHeight = 720,
                WindowTitle = "IronRose Engine"
            };

            _window = VeldridStartup.CreateWindow(ref windowCI);
            Console.WriteLine($"[Renderer] Window created: {_window.Width}x{_window.Height}");

            // GraphicsDevice 생성
            GraphicsDeviceOptions options = new GraphicsDeviceOptions
            {
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true,
                Debug = true
            };

            _graphicsDevice = VeldridStartup.CreateGraphicsDevice(_window, options, GraphicsBackend.Vulkan);
            Console.WriteLine($"[Renderer] Graphics Device Created: {_graphicsDevice.BackendType}");

            _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
            Console.WriteLine("[Renderer] Command list created");
        }

        public bool ProcessEvents()
        {
            if (_window == null)
                return false;

            var snapshot = _window.PumpEvents();
            return _window.Exists;
        }

        public void Render()
        {
            if (_graphicsDevice == null || _commandList == null)
            {
                Console.WriteLine("[Renderer] ERROR: GraphicsDevice or CommandList is null");
                return;
            }

            _commandList.Begin();

            // IronRose 테마 색상으로 화면 클리어 (금속의 백장미)
            _commandList.SetFramebuffer(_graphicsDevice.SwapchainFramebuffer);
            _commandList.ClearColorTarget(0, new RgbaFloat(0.902f, 0.863f, 0.824f, 1.0f));

            _commandList.End();

            _graphicsDevice.SubmitCommands(_commandList);
            _graphicsDevice.SwapBuffers();
        }

        public void Dispose()
        {
            Console.WriteLine("[Renderer] Disposing graphics resources...");

            try
            {
                if (_graphicsDevice != null)
                {
                    Console.WriteLine("[Renderer] DEBUG: Waiting for GPU idle...");
                    _graphicsDevice.WaitForIdle();
                    Console.WriteLine("[Renderer] DEBUG: GPU idle");
                }

                Console.WriteLine("[Renderer] DEBUG: Disposing CommandList");
                _commandList?.Dispose();
                Console.WriteLine("[Renderer] DEBUG: CommandList disposed");

                Console.WriteLine("[Renderer] DEBUG: Disposing GraphicsDevice");
                _graphicsDevice?.Dispose();
                Console.WriteLine("[Renderer] DEBUG: GraphicsDevice disposed");

                Console.WriteLine("[Renderer] DEBUG: Closing Window (skipping - causes hang)");
                // _window?.Close();  // ← 블록되므로 생략, GC가 처리
                _window = null;
                Console.WriteLine("[Renderer] DEBUG: Window set to null");

                Console.WriteLine("[Renderer] All resources disposed successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Renderer] ERROR during Dispose: {ex.Message}");
                Console.WriteLine($"[Renderer] Dispose error ignored, continuing...");
            }
        }
    }
}
