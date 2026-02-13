# Phase 2A: Engine Core 핫 리로딩

## 목표
**"Everything is Hot-Reloadable"의 진정한 구현**

Bootstrapper만 고정하고, IronRose.Engine.dll을 포함한 모든 엔진 코드를 핫 리로드 가능하게 만듭니다.

---

## 현재 상태 분석

### 현재 아키텍처 (Phase 2)
```
[Bootstrapper.exe] (정적 빌드)
  │
  ├─ using IronRose.Rendering;    ← 정적 참조 (핫 리로드 불가)
  ├─ using IronRose.Scripting;    ← 정적 참조 (핫 리로드 불가)
  │
  └─ [Scripts/*.cs] ────────────→ [Roslyn] → [ALC] (핫 리로드 가능)
```

**문제점**:
- ❌ Engine.dll 수정 → 엔진 재시작 필요
- ❌ Rendering.dll 수정 → 엔진 재시작 필요
- ❌ Bootstrapper가 엔진 네임스페이스를 직접 사용 (`using IronRose.Rendering;`)

---

## 목표 아키텍처

### Phase 2A 목표
```
[Bootstrapper.exe] (최소 코드, ~200줄)
  │
  ├─ [EngineContext ALC] ──→ Engine.dll (핫 리로드 가능)
  │                          Rendering.dll (핫 리로드 가능)
  │                          Scripting.dll (핫 리로드 가능)
  │
  └─ [ScriptContext ALC] ──→ Scripts/*.cs (핫 리로드 가능)
```

**핵심 원칙**:
- ✅ Bootstrapper는 어떤 엔진 타입도 직접 참조하지 않음
- ✅ 리플렉션 or 인터페이스로 엔진과 통신
- ✅ src 폴더 변경 감지 → 빌드 → DLL 핫 리로드

---

## 구현 계획

### 2A.1 Bootstrapper 최소화

**현재 문제**:
```csharp
// Bootstrapper/Program.cs
using IronRose.Rendering;  // ← 정적 참조!

private static GraphicsManager? _graphics;  // ← 타입 참조!
_graphics = new GraphicsManager();  // ← 직접 생성!
```

**해결 방안**:
```csharp
// Bootstrapper/Program.cs (정적 참조 제거)
using System.Reflection;

// 엔진 인터페이스만 참조 (별도 어셈블리)
using IronRose.Contracts;

private static IEngineCore? _engine;
private static AssemblyLoadContext? _engineALC;

static void LoadEngine()
{
    // Engine.dll 동적 로드
    _engineALC = new AssemblyLoadContext("EngineContext", isCollectible: true);

    var engineAssembly = _engineALC.LoadFromAssemblyPath(
        Path.GetFullPath("src/IronRose.Engine/bin/Debug/net10.0/IronRose.Engine.dll")
    );

    var renderingAssembly = _engineALC.LoadFromAssemblyPath(
        Path.GetFullPath("src/IronRose.Rendering/bin/Debug/net10.0/IronRose.Rendering.dll")
    );

    // 리플렉션으로 엔진 생성
    var engineType = engineAssembly.GetType("IronRose.Engine.EngineCore");
    _engine = (IEngineCore)Activator.CreateInstance(engineType)!;

    _engine.Initialize();
}
```

---

### 2A.2 계약 인터페이스 분리

**새 프로젝트: IronRose.Contracts**
```bash
dotnet new classlib -n IronRose.Contracts -f net10.0 -o src/IronRose.Contracts
dotnet sln add src/IronRose.Contracts
```

**IEngineCore.cs:**
```csharp
namespace IronRose.Contracts
{
    public interface IEngineCore
    {
        void Initialize();
        bool ProcessEvents();
        void Update(double deltaTime);
        void Render();
        void Shutdown();
    }
}
```

**프로젝트 참조 구조**:
```
Bootstrapper → Contracts (정적, 변경 없음)
Engine → Contracts (인터페이스 구현)
Rendering → Contracts
```

---

### 2A.3 Engine.dll 핫 리로드 구조

**EngineLoader.cs (Bootstrapper):**
```csharp
public class EngineLoader
{
    private AssemblyLoadContext? _currentALC;
    private IEngineCore? _currentEngine;

    public IEngineCore LoadEngine()
    {
        _currentALC = new AssemblyLoadContext("EngineContext", isCollectible: true);

        // Engine, Rendering, Physics 등 모두 로드
        var assemblies = new[]
        {
            "IronRose.Engine.dll",
            "IronRose.Rendering.dll",
            "IronRose.Scripting.dll"
        };

        foreach (var asmName in assemblies)
        {
            var path = Path.Combine("src", asmName.Replace(".dll", ""),
                                     "bin/Debug/net10.0", asmName);
            _currentALC.LoadFromAssemblyPath(Path.GetFullPath(path));
        }

        // EngineCore 인스턴스 생성
        var engineAssembly = _currentALC.Assemblies
            .First(a => a.GetName().Name == "IronRose.Engine");

        var engineType = engineAssembly.GetType("IronRose.Engine.EngineCore");
        _currentEngine = (IEngineCore)Activator.CreateInstance(engineType)!;

        return _currentEngine;
    }

    public void HotReloadEngine()
    {
        Console.WriteLine("[EngineLoader] Hot reloading engine...");

        // 상태 저장 (TODO)

        // 기존 엔진 언로드
        _currentEngine?.Shutdown();
        _currentEngine = null;

        var weakRef = new WeakReference(_currentALC);
        _currentALC?.Unload();
        _currentALC = null;

        // GC
        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // 새 엔진 로드
        _currentEngine = LoadEngine();

        // 상태 복원 (TODO)

        Console.WriteLine("[EngineLoader] Engine hot reload completed!");
    }
}
```

---

### 2A.4 파일 변경 감지 및 자동 빌드

**EngineWatcher.cs (Bootstrapper):**
```csharp
public class EngineWatcher
{
    private FileSystemWatcher _watcher;
    private System.Timers.Timer _rebuildTimer;

    public event Action? OnEngineRebuilt;

    public EngineWatcher()
    {
        // src 폴더의 모든 .cs 파일 감시
        _watcher = new FileSystemWatcher("src", "*.cs")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite
        };

        _watcher.Changed += OnFileChanged;

        // 디바운싱 타이머 (1초 내 여러 변경 → 한 번만 빌드)
        _rebuildTimer = new System.Timers.Timer(1000);
        _rebuildTimer.AutoReset = false;
        _rebuildTimer.Elapsed += (s, e) => RebuildAndReload();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine($"[EngineWatcher] Detected change: {e.Name}");

        // 타이머 리셋 (디바운싱)
        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    private void RebuildAndReload()
    {
        Console.WriteLine("[EngineWatcher] Rebuilding solution...");

        // dotnet build 실행
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build IronRose.sln",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            Console.WriteLine("[EngineWatcher] Build SUCCESS");
            OnEngineRebuilt?.Invoke();
        }
        else
        {
            Console.WriteLine("[EngineWatcher] Build FAILED");
            Console.WriteLine(output);
        }
    }

    public void Enable()
    {
        _watcher.EnableRaisingEvents = true;
        Console.WriteLine("[EngineWatcher] Watching src/**/*.cs for changes");
    }
}
```

---

### 2A.5 EngineCore 추상화 (IronRose.Engine)

**EngineCore.cs (새 파일):**
```csharp
using IronRose.Contracts;
using IronRose.Rendering;
using System;

namespace IronRose.Engine
{
    public class EngineCore : IEngineCore
    {
        private GraphicsManager? _graphics;

        public void Initialize()
        {
            Console.WriteLine("[Engine] Initializing...");
            _graphics = new GraphicsManager();
            _graphics.Initialize();
        }

        public bool ProcessEvents()
        {
            return _graphics?.ProcessEvents() ?? false;
        }

        public void Update(double deltaTime)
        {
            // GameObject/Component 업데이트 (Phase 3에서 구현)
        }

        public void Render()
        {
            _graphics?.Render();
        }

        public void Shutdown()
        {
            Console.WriteLine("[Engine] Shutting down...");
            _graphics?.Dispose();
        }
    }
}
```

---

## 구현 단계

### Step 1: IronRose.Contracts 프로젝트 생성
- [ ] 새 프로젝트 생성
- [ ] IEngineCore 인터페이스 정의
- [ ] 솔루션에 추가

### Step 2: EngineCore 구현
- [ ] IronRose.Engine에 EngineCore.cs 생성
- [ ] IEngineCore 인터페이스 구현
- [ ] GraphicsManager를 EngineCore로 이동

### Step 3: Bootstrapper 리팩토링
- [ ] 정적 참조 제거 (IronRose.Rendering 등)
- [ ] EngineLoader 구현
- [ ] 리플렉션 기반 엔진 로드

### Step 4: 핫 리로드 구현
- [ ] EngineWatcher 구현 (src 폴더 감시)
- [ ] 자동 빌드 + 리로드
- [ ] 디바운싱 타이머

### Step 5: 테스트
- [ ] Engine.dll 수정 → 자동 빌드 → 핫 리로드 확인
- [ ] GameObject 추가 → 즉시 반영 확인

---

## 예상 문제점 및 해결

### 문제 1: 의존성 로드
**문제**: Engine.dll이 Rendering.dll을 참조하는데, ALC에서 어떻게 해결?

**해결**:
```csharp
_engineALC.Resolving += (context, name) => {
    // 같은 ALC 내에서 Rendering.dll 찾기
    return context.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
};
```

### 문제 2: 핫 리로드 시 상태 손실
**문제**: GameObject들이 사라짐

**해결** (Phase 3 이후):
```csharp
// 리로드 전
var scene = SerializeScene();  // GameObject → TOML

// 리로드 후
DeserializeScene(scene);  // TOML → GameObject
```

### 문제 3: 빌드 시간
**문제**: src 폴더 수정마다 전체 빌드 → 느림

**해결**:
- 증분 빌드 사용 (`dotnet build`)
- 변경된 프로젝트만 빌드
- 병렬 빌드 활성화

### 문제 4: FileSystemWatcher 불안정
**문제**: 이전 테스트에서 파일 변경 감지 실패

**해결**:
```csharp
_watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
_watcher.InternalBufferSize = 64 * 1024;  // 기본: 8KB → 64KB
```

---

## 검증 결과 ✅

### 구현 완료 (2026-02-13)

**기본 동작**:
- ✅ Bootstrapper 시작 → Engine.dll 동적 로드 (26개 어셈블리)
- ✅ Shadow Copy (LoadFromStream) - 파일 잠금 없음
- ✅ 리플렉션 기반 엔진 통신
- ✅ 윈도우 생성 및 렌더링 (Vulkan)

**핫 리로드 메커니즘**:
- ✅ src/**/*.cs 파일 변경 감지
- ✅ bin-hot/{timestamp}/ 폴더로 빌드
- ✅ 빌드 성공 (파일 잠금 없음!)
- ✅ 기존 엔진 언로드 (ALC + GC)
- ✅ 새 엔진 로드
- ✅ **코드 변경 즉시 반영!**

**실행 로그**:
```
[EngineWatcher] Build SUCCESS
[EngineLoader] Unloading engine...
[EngineLoader] Engine ALC unloaded successfully
[EngineLoader] HOT RELOAD: Loading from bin-hot/20260213_190432
╔════════════════════════════════════════════════════╗
║ ✨✨✨ FULL HOT RELOAD WORKING!!! ✨✨✨        ║
║ Everything is Hot-Reloadable - ACHIEVED! 🚀   ║
╚════════════════════════════════════════════════════╝
```

### 핫 리로드 시나리오 1: 엔진 코드 수정 ✅
1. Engine/GameObject.cs에 메서드 추가:
   ```csharp
   public void SayHello() { Debug.Log("Hello from hot-reloaded engine!"); }
   ```
2. 파일 저장
3. 콘솔에 "[EngineWatcher] Rebuilding..." 출력
4. 빌드 성공 → "[EngineLoader] Engine hot reload completed!"
5. 새 메서드 호출 가능 확인

### 핫 리로드 시나리오 2: 렌더링 코드 수정
1. GraphicsManager.cs의 클리어 색상 변경:
   ```csharp
   _commandList.ClearColorTarget(0, new RgbaFloat(1.0f, 0.0f, 0.0f, 1.0f));  // 빨간색
   ```
2. 파일 저장
3. 자동 빌드 → 리로드
4. 화면 색상이 즉시 빨간색으로 변경 확인

### 핫 리로드 시나리오 3: 다중 DLL 리로드
1. Engine.dll + Rendering.dll 동시 수정
2. 한 번의 빌드로 두 DLL 업데이트
3. 동시 핫 리로드 성공

---

## 트레이드오프

### 장점
- ✅ **진정한 "Everything is Hot-Reloadable"**
- ✅ AI가 엔진 기능도 런타임에 추가/수정 가능
- ✅ GameObject 구조 변경 → 즉시 반영
- ✅ 렌더링 파이프라인 수정 → 즉시 반영

### 단점
- ❌ **복잡도 증가** (리플렉션, ALC 관리)
- ❌ **성능 오버헤드** (리플렉션 호출)
- ❌ **디버깅 어려움** (스택 트레이스가 복잡)
- ❌ **초기 구현 시간** (~2-3일)

---

## 대안: 단계적 접근

### 옵션 1: 완전 핫 리로드 (Phase 2A)
- Engine.dll 핫 리로드 구현
- Bootstrapper 리플렉션 기반으로 전환
- 2-3일 소요

### 옵션 2: 하이브리드 (현재 + 개선)
- **Engine.dll**: 정적 빌드 (재시작 필요)
- **Scripts 폴더**: 핫 리로드 유지
- **Scripts → Engine 이동**: AI Agent가 자동화
- 1일 소요

### 옵션 3: Phase 3 우선
- GameObject/Component 먼저 구현
- 기능이 안정화된 후 핫 리로드 개선
- Phase 7에서 재검토

---

## 권장 사항

**Phase 3 먼저 진행 → Phase 7에서 엔진 핫 리로드**

**이유**:
1. GameObject/Component 없이는 엔진 핫 리로드를 테스트하기 어려움
2. 핫 리로드는 **개발 경험 개선**이지 **핵심 기능**은 아님
3. 복잡도가 높아 디버깅 시간이 길어질 수 있음
4. AI Agent 자동화로 Scripts → Engine 이동이 가능

**하지만 도전하고 싶다면**:
- "Everything is Hot-Reloadable"은 매우 강력한 기능
- 한 번 구현하면 이후 개발 속도가 극적으로 향상
- IronRose의 핵심 차별점

---

## 의사 결정

**A. 지금 구현** (Phase 2A, 2-3일)
→ 완전한 핫 리로드 시스템
→ 복잡하지만 강력함

**B. Phase 3 우선** (GameObject, 2-3일)
→ 엔진 구조 먼저 완성
→ Phase 7에서 핫 리로드 개선

**C. 하이브리드** (Scripts만 핫 리로드, 1일)
→ 현재 상태 유지
→ AI Agent 자동화로 보완

어떻게 하시겠습니까?
