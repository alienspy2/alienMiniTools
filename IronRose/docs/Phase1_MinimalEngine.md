# Phase 1: 최소 실행 가능 엔진 (Bootstrapper)

## 목표
최소한의 Bootstrapper를 만들어 SDL3 윈도우를 열고 Veldrid로 화면을 클리어합니다.

> **"Bootstrapper는 500줄 미만으로 유지!"**
>
> - SDL/Veldrid 초기화만
> - AssemblyLoadContext 관리 준비
> - 메인 루프
> - 그게 전부!

---

## 작업 항목

### 1.1 SDL3 윈도우 생성 (IronRose.Bootstrapper)

**Program.cs 구현:**
```csharp
using Silk.NET.SDL;
using System;

namespace IronRose.Bootstrapper
{
    class Program
    {
        private static Sdl _sdl = null!;
        private static unsafe Window* _window;
        private static bool _running = true;

        static unsafe void Main(string[] args)
        {
            Console.WriteLine("IronRose Engine Starting...");

            // SDL 초기화
            _sdl = Sdl.GetApi();
            if (_sdl.Init(Sdl.InitVideo) < 0)
            {
                Console.WriteLine($"SDL Init Failed: {_sdl.GetErrorS()}");
                return;
            }

            // 윈도우 생성
            _window = _sdl.CreateWindow(
                "IronRose Engine",
                Sdl.WindowposCentered,
                Sdl.WindowposCentered,
                1280,
                720,
                (uint)(WindowFlags.Shown | WindowFlags.Vulkan)
            );

            if (_window == null)
            {
                Console.WriteLine($"Window Creation Failed: {_sdl.GetErrorS()}");
                return;
            }

            // 메인 루프
            MainLoop();

            // 정리
            _sdl.DestroyWindow(_window);
            _sdl.Quit();
        }

        static unsafe void MainLoop()
        {
            Event evt;

            while (_running)
            {
                // 이벤트 처리
                while (_sdl.PollEvent(&evt) != 0)
                {
                    if (evt.Type == (uint)EventType.Quit)
                    {
                        _running = false;
                    }
                    else if (evt.Type == (uint)EventType.Keydown)
                    {
                        if (evt.Key.Keysym.Sym == (int)KeyCode.Escape)
                        {
                            _running = false;
                        }
                    }
                }

                // TODO: 렌더링 (Phase 1.2에서 구현)

                System.Threading.Thread.Sleep(16); // ~60 FPS
            }
        }
    }
}
```

---

### 1.2 Veldrid 그래픽 디바이스 초기화

**GraphicsManager.cs 생성 (IronRose.Rendering):**
```csharp
using Veldrid;
using Veldrid.Sdl2;

namespace IronRose.Rendering
{
    public class GraphicsManager
    {
        private GraphicsDevice _graphicsDevice = null!;
        private CommandList _commandList = null!;

        public unsafe void Initialize(IntPtr windowHandle)
        {
            var options = new GraphicsDeviceOptions
            {
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true,
                Debug = true
            };

            // Vulkan 디바이스 생성
            _graphicsDevice = GraphicsDevice.CreateVulkan(
                options,
                windowHandle
            );

            _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();

            Console.WriteLine($"Graphics Device Created: {_graphicsDevice.BackendType}");
        }

        public void Render()
        {
            _commandList.Begin();

            // 파란색으로 화면 클리어
            _commandList.SetFramebuffer(_graphicsDevice.SwapchainFramebuffer);
            _commandList.ClearColorTarget(0, new RgbaFloat(0.2f, 0.4f, 0.8f, 1.0f));

            _commandList.End();

            _graphicsDevice.SubmitCommands(_commandList);
            _graphicsDevice.SwapBuffers();
        }

        public void Dispose()
        {
            _commandList?.Dispose();
            _graphicsDevice?.Dispose();
        }
    }
}
```

**Program.cs 업데이트:**
```csharp
private static GraphicsManager _graphics = null!;

static unsafe void Main(string[] args)
{
    // ... SDL 초기화 ...

    // 그래픽 초기화
    _graphics = new GraphicsManager();
    _graphics.Initialize((IntPtr)_window);

    MainLoop();

    // 정리
    _graphics.Dispose();
    _sdl.DestroyWindow(_window);
    _sdl.Quit();
}

static void MainLoop()
{
    while (_running)
    {
        // ... 이벤트 처리 ...

        // 렌더링
        _graphics.Render();
    }
}
```

---

### 1.3 기본 렌더링 루프

**타이밍 시스템 추가:**
```csharp
using System.Diagnostics;

private static Stopwatch _timer = Stopwatch.StartNew();
private static double _lastTime = 0;
private static double _deltaTime = 0;

static void MainLoop()
{
    while (_running)
    {
        // 델타 타임 계산
        double currentTime = _timer.Elapsed.TotalSeconds;
        _deltaTime = currentTime - _lastTime;
        _lastTime = currentTime;

        // 이벤트 처리
        ProcessEvents();

        // 렌더링
        _graphics.Render();

        // 프레임 제한 (60 FPS)
        double frameTime = _timer.Elapsed.TotalSeconds - currentTime;
        if (frameTime < 1.0 / 60.0)
        {
            Thread.Sleep((int)((1.0 / 60.0 - frameTime) * 1000));
        }
    }
}
```

---

## 검증 기준

✅ 1280x720 윈도우가 열림
✅ 파란색 배경 화면이 렌더링됨
✅ ESC 키를 누르면 프로그램이 종료됨
✅ 윈도우 닫기 버튼으로 종료 가능
✅ 약 60 FPS로 동작 (콘솔에 FPS 출력)

---

## 트러블슈팅

### Vulkan 초기화 실패
- Vulkan 드라이버가 설치되어 있는지 확인
- `GraphicsDevice.IsBackendSupported(GraphicsBackend.Vulkan)` 체크
- 대안: Direct3D 11로 대체 (Windows만 지원)

### SDL3 DLL 누락
- NuGet 패키지에서 자동으로 복사되어야 함
- 수동으로 SDL3.dll을 출력 디렉토리에 복사

---

## 예상 소요 시간
**2-3일**

---

## 다음 단계
→ [Phase 2: Roslyn 핫 리로딩 시스템](Phase2_HotReloading.md)
