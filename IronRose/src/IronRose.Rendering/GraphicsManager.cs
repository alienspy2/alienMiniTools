using Veldrid;
using Silk.NET.Windowing;
using Veldrid.ImageSharp;
using System;
using System.IO;

namespace IronRose.Rendering
{
    public class GraphicsManager
    {
        private GraphicsDevice? _graphicsDevice;
        private CommandList? _commandList;
        private IWindow? _window;
        private string? _pendingScreenshot;
        private RgbaFloat _clearColor = new RgbaFloat(0.902f, 0.863f, 0.824f, 1.0f);

        public void SetClearColor(float r, float g, float b)
        {
            _clearColor = new RgbaFloat(r, g, b, 1.0f);
        }

        public void Initialize(IWindow window)
        {
            Console.WriteLine("[Renderer] Initializing graphics device...");

            _window = window;
            Console.WriteLine($"[Renderer] Using window: {_window.Size.X}x{_window.Size.Y}");

            // Silk.NET 네이티브 핸들 → Veldrid SwapchainSource
            var swapchainSource = GetSwapchainSource(_window);

            GraphicsDeviceOptions options = new GraphicsDeviceOptions
            {
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true,
                Debug = false
            };

            var scDesc = new SwapchainDescription(
                swapchainSource,
                (uint)_window.Size.X,
                (uint)_window.Size.Y,
                null,   // depthFormat
                false,  // vsync
                false   // srgb
            );

            _graphicsDevice = GraphicsDevice.CreateVulkan(options, scDesc);
            Console.WriteLine($"[Renderer] Graphics Device Created: {_graphicsDevice.BackendType}");

            _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
            Console.WriteLine("[Renderer] Command list created");

            // 윈도우 리사이즈 처리
            _window.Resize += size =>
            {
                if (size.X > 0 && size.Y > 0)
                    _graphicsDevice.ResizeMainWindow((uint)size.X, (uint)size.Y);
            };
        }

        private static SwapchainSource GetSwapchainSource(IWindow window)
        {
            var native = window.Native
                ?? throw new PlatformNotSupportedException("Cannot get native window handle");

            if (native.Win32.HasValue)
            {
                var w = native.Win32.Value;
                return SwapchainSource.CreateWin32(w.Hwnd, w.HInstance);
            }

            if (native.X11.HasValue)
            {
                var x = native.X11.Value;
                return SwapchainSource.CreateXlib(x.Display, (nint)x.Window);
            }

            if (native.Wayland.HasValue)
            {
                var w = native.Wayland.Value;
                return SwapchainSource.CreateWayland(w.Display, w.Surface);
            }

            throw new PlatformNotSupportedException("Unsupported windowing platform");
        }

        public void Render()
        {
            if (_graphicsDevice == null || _commandList == null)
            {
                Console.WriteLine("[Renderer] ERROR: GraphicsDevice or CommandList is null");
                return;
            }

            _commandList.Begin();

            _commandList.SetFramebuffer(_graphicsDevice.SwapchainFramebuffer);
            _commandList.ClearColorTarget(0, _clearColor);

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

                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < height; y++)
                    {
                        var rowSpan = accessor.GetRowSpan(y);
                        unsafe
                        {
                            var sourcePtr = (byte*)map.Data.ToPointer() + (y * rowPitch);
                            fixed (SixLabors.ImageSharp.PixelFormats.Bgra32* destPtr = rowSpan)
                            {
                                Buffer.MemoryCopy(sourcePtr, destPtr, rowPitch, width * pixelSizeInBytes);
                            }
                        }
                    }
                });

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

                _window = null;
                Console.WriteLine("[Renderer] Window reference cleared");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Renderer] ERROR during Dispose: {ex.Message}");
            }
        }
    }
}
