using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Veldrid.ImageSharp;
using System;
using System.IO;

namespace IronRose.Rendering
{
    public class GraphicsManager
    {
        private GraphicsDevice? _graphicsDevice;
        private CommandList? _commandList;
        private Sdl2Window? _window;
        private string? _pendingScreenshot;

        public void Initialize(object? windowHandle = null)
        {
            Console.WriteLine("[Renderer] Initializing graphics device...");

            if (windowHandle is Sdl2Window window)
            {
                _window = window;
                Console.WriteLine($"[Renderer] ✅ Using existing window: {_window.Width}x{_window.Height}");
            }
            else
            {
                // 윈도우가 없으면 새로 생성 (하위 호환성)
                WindowCreateInfo windowCI = new WindowCreateInfo()
                {
                    X = 100,
                    Y = 100,
                    WindowWidth = 1280,
                    WindowHeight = 720,
                    WindowTitle = "IronRose Engine"
                };

                _window = VeldridStartup.CreateWindow(ref windowCI);
                Console.WriteLine($"[Renderer] Created new window: {_window.Width}x{_window.Height}");
            }

            // GraphicsDevice 생성
            GraphicsDeviceOptions options = new GraphicsDeviceOptions
            {
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true,
                Debug = false
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

            // IronRose 테마 색상 (금속의 백장미)
            _commandList.SetFramebuffer(_graphicsDevice.SwapchainFramebuffer);
            _commandList.ClearColorTarget(0, new RgbaFloat(1.0f, 0.0f, 0.0f, 1.0f));

            _commandList.End();

            _graphicsDevice.SubmitCommands(_commandList);

            // 스크린샷 요청이 있으면 SwapBuffers() 전에 캡처
            if (_pendingScreenshot != null)
            {
                CaptureScreenshotInternal(_pendingScreenshot);
                _pendingScreenshot = null;
            }

            _graphicsDevice.SwapBuffers();
        }

        public void RequestScreenshot(string filename)
        {
            _pendingScreenshot = filename;
        }

        private void CaptureScreenshotInternal(string filename)
        {
            if (_graphicsDevice == null)
            {
                Console.WriteLine("[Renderer] ERROR: Cannot capture screenshot, GraphicsDevice is null");
                return;
            }

            try
            {
                var swapchainFB = _graphicsDevice.SwapchainFramebuffer;
                var colorTexture = swapchainFB.ColorTargets[0].Target;

                // Staging texture 생성 (CPU로 읽기 가능)
                var stagingTexture = _graphicsDevice.ResourceFactory.CreateTexture(new TextureDescription(
                    colorTexture.Width,
                    colorTexture.Height,
                    1, 1, 1,
                    colorTexture.Format,
                    TextureUsage.Staging,
                    colorTexture.Type
                ));

                // GPU → CPU 복사
                var commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
                commandList.Begin();
                commandList.CopyTexture(colorTexture, stagingTexture);
                commandList.End();
                _graphicsDevice.SubmitCommands(commandList);
                _graphicsDevice.WaitForIdle();

                // 픽셀 데이터 읽기
                var map = _graphicsDevice.Map(stagingTexture, MapMode.Read);
                var pixelSizeInBytes = 4; // BGRA8 = 4 bytes
                var rowPitch = (int)map.RowPitch;
                var width = (int)colorTexture.Width;
                var height = (int)colorTexture.Height;

                // ImageSharp 이미지 생성
                using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Bgra32>(width, height);

                for (int y = 0; y < height; y++)
                {
                    var rowSpan = image.GetPixelRowSpan(y);
                    unsafe
                    {
                        var sourcePtr = (byte*)map.Data.ToPointer() + (y * rowPitch);
                        fixed (SixLabors.ImageSharp.PixelFormats.Bgra32* destPtr = rowSpan)
                        {
                            Buffer.MemoryCopy(sourcePtr, destPtr, rowPitch, width * pixelSizeInBytes);
                        }
                    }
                }

                _graphicsDevice.Unmap(stagingTexture);

                // 파일 저장
                Directory.CreateDirectory(Path.GetDirectoryName(filename) ?? ".");
                using (var fileStream = File.Create(filename))
                {
                    image.Save(fileStream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
                }
                Console.WriteLine($"[Renderer] Screenshot saved: {filename}");

                // 정리
                stagingTexture.Dispose();
                commandList.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Renderer] ERROR capturing screenshot: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        public void Dispose()
        {
            Console.WriteLine("[Renderer] Disposing graphics resources...");

            try
            {
                if (_graphicsDevice != null)
                {
                    _graphicsDevice.WaitForIdle();
                    Console.WriteLine("[Renderer] GPU idle");
                }

                _commandList?.Dispose();
                Console.WriteLine("[Renderer] CommandList disposed");

                _graphicsDevice?.Dispose();
                Console.WriteLine("[Renderer] GraphicsDevice disposed");

                // 윈도우는 Program.cs가 관리하므로 닫지 않음
                _window = null;
                Console.WriteLine("[Renderer] Window reference cleared (not closed)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Renderer] ERROR during Dispose: {ex.Message}");
            }
        }
    }
}
