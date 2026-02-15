# Phase 9: 최적화 및 안정화

## 목표
프로덕션 수준의 성능과 안정성을 확보합니다.

---

## 최적화 철학

> **"측정 먼저, 최적화는 나중에"**
>
> - 병목이 **실제로 발생한 부분**만 최적화합니다.
> - 프로파일러로 측정 없이는 최적화하지 않습니다.
> - 단순한 코드 > 복잡한 최적화 코드

---

## 작업 항목

### 9.1 메모리 관리 (필수)

**GPU 리소스 Reference Counting:**
```csharp
using System;

namespace IronRose.Engine
{
    public class RefCounted<T> where T : IDisposable
    {
        private T _resource;
        private int _refCount = 1;

        public RefCounted(T resource)
        {
            _resource = resource;
        }

        public T Resource => _resource;

        public void Retain()
        {
            _refCount++;
        }

        public void Release()
        {
            _refCount--;
            if (_refCount <= 0)
            {
                _resource.Dispose();
                Console.WriteLine($"[RefCount] Disposed: {typeof(T).Name}");
            }
        }

        public int RefCount => _refCount;
    }
}
```

**Mesh/Texture 래핑:**
```csharp
public class Mesh
{
    private RefCounted<DeviceBuffer>? _vertexBuffer;
    private RefCounted<DeviceBuffer>? _indexBuffer;

    public void Dispose()
    {
        _vertexBuffer?.Release();
        _indexBuffer?.Release();
    }
}
```

**사용하지 않는 어셈블리 자동 언로드:**
```csharp
public class ScriptDomain
{
    private WeakReference _previousALC = null!;

    public void Reload(byte[] newAssemblyBytes)
    {
        // ... 기존 코드 ...

        // 이전 ALC가 완전히 언로드되었는지 확인
        if (_previousALC?.IsAlive == true)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (_previousALC.IsAlive)
            {
                Console.WriteLine("[WARNING] Previous ALC not fully unloaded!");
            }
        }
    }
}
```

**메모리 릭 탐지:**
```csharp
public class MemoryMonitor
{
    private long _lastGCMemory = 0;

    public void CheckForLeaks()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long currentMemory = GC.GetTotalMemory(false);
        long diff = currentMemory - _lastGCMemory;

        if (diff > 10_000_000) // 10MB 증가
        {
            Console.WriteLine($"[MEMORY] Possible leak detected: +{diff / 1_000_000}MB");
        }

        _lastGCMemory = currentMemory;
    }
}
```

---

### 9.2 선택적 성능 최적화 (병목 발생 시에만)

**⚠️ 다음은 성능 문제가 실제로 측정되었을 때만 적용!**

**GC 압력 최소화:**
```csharp
using System.Buffers;

public class MeshPool
{
    private static ArrayPool<Vertex> _vertexPool = ArrayPool<Vertex>.Shared;

    public Vertex[] RentVertexArray(int size)
    {
        return _vertexPool.Rent(size);
    }

    public void ReturnVertexArray(Vertex[] array)
    {
        _vertexPool.Return(array);
    }
}
```

**Component 캐싱:**
```csharp
public class GameObject
{
    private Dictionary<Type, Component> _componentCache = new();

    public T? GetComponent<T>() where T : Component
    {
        var type = typeof(T);

        if (_componentCache.TryGetValue(type, out var cached))
        {
            return (T)cached;
        }

        var component = _components.OfType<T>().FirstOrDefault();
        if (component != null)
        {
            _componentCache[type] = component;
        }

        return component;
    }
}
```

**Transform 행렬 캐싱:**
```csharp
public class Transform : Component
{
    private Vector3 _position;
    private Quaternion _rotation;
    private Vector3 _localScale;

    private Matrix4x4? _cachedMatrix = null;
    private bool _isDirty = true;

    public Vector3 position
    {
        get => _position;
        set
        {
            _position = value;
            _isDirty = true;
        }
    }

    public Matrix4x4 GetWorldMatrix()
    {
        if (_isDirty || _cachedMatrix == null)
        {
            _cachedMatrix = CalculateMatrix();
            _isDirty = false;
        }
        return _cachedMatrix.Value;
    }

    private Matrix4x4 CalculateMatrix()
    {
        // TRS 행렬 계산
        return Matrix4x4.Identity; // TODO: 실제 구현
    }
}
```

---

### 9.3 멀티스레딩 (고급, 선택사항)

**⚠️ 복잡도가 크게 증가하므로 정말 필요할 때만!**

**대규모 씬에서만 필요:**

**에셋 로딩 비동기 처리:**
```csharp
public class AssetDatabase
{
    public async Task<Mesh> LoadMeshAsync(string path)
    {
        return await Task.Run(() =>
        {
            var importer = new MeshImporter();
            return importer.Import(path);
        });
    }
}
```

**렌더링 커맨드 생성 병렬화:**
```csharp
using System.Threading.Tasks;
using System.Collections.Concurrent;

public class RenderSystem
{
    public void RenderParallel(List<MeshRenderer> renderers)
    {
        var commandLists = new ConcurrentBag<CommandList>();

        Parallel.ForEach(renderers, renderer =>
        {
            var cl = _device.ResourceFactory.CreateCommandList();
            cl.Begin();
            // ... 렌더링 커맨드 ...
            cl.End();

            commandLists.Add(cl);
        });

        foreach (var cl in commandLists)
        {
            _device.SubmitCommands(cl);
        }
    }
}
```

---

### 9.4 프로파일링 도구 (필수)

**"측정할 수 없으면 최적화할 수 없다"**

**PerformanceMonitor.cs:**
```csharp
using System;
using System.Diagnostics;

namespace IronRose.Engine
{
    public class PerformanceMonitor
    {
        private Stopwatch _frameTimer = Stopwatch.StartNew();
        private double _frameTime = 0;
        private int _frameCount = 0;
        private double _fpsUpdateInterval = 1.0;
        private double _fpsAccumulator = 0;
        private double _currentFPS = 0;

        public void BeginFrame()
        {
            _frameTimer.Restart();
        }

        public void EndFrame()
        {
            _frameTime = _frameTimer.Elapsed.TotalMilliseconds;
            _frameCount++;

            _fpsAccumulator += _frameTimer.Elapsed.TotalSeconds;
            if (_fpsAccumulator >= _fpsUpdateInterval)
            {
                _currentFPS = _frameCount / _fpsAccumulator;
                _frameCount = 0;
                _fpsAccumulator = 0;
            }
        }

        public void DrawOverlay()
        {
            Console.Title = $"IronRose | FPS: {_currentFPS:F1} | Frame: {_frameTime:F2}ms";
        }

        public double FrameTime => _frameTime;
        public double FPS => _currentFPS;
    }
}
```

**GPU 메모리 사용량 모니터링:**
```csharp
public class GPUMemoryMonitor
{
    public void LogMemoryUsage(GraphicsDevice device)
    {
        // Veldrid는 직접적인 VRAM 쿼리를 지원하지 않음
        // Vulkan API를 직접 사용하거나 간접적으로 추정
        Console.WriteLine("[GPU Memory] Tracking not available in Veldrid");

        // 대신 로드된 리소스 수를 추적
        int textureCount = 0;
        int bufferCount = 0;
        // ... 카운팅 로직 ...

        Console.WriteLine($"[GPU] Textures: {textureCount}, Buffers: {bufferCount}");
    }
}
```

**핫 리로드 시간 측정:**
```csharp
public class ScriptDomain
{
    public void Reload(byte[] newAssemblyBytes)
    {
        var sw = Stopwatch.StartNew();

        // ... 기존 리로드 로직 ...

        sw.Stop();
        Console.WriteLine($"[HotReload] Completed in {sw.ElapsedMilliseconds}ms");
    }
}
```

---

### 9.5 유닛 테스트 & CI/CD

**xUnit 테스트 작성:**
```csharp
using Xunit;
using RoseEngine;

namespace IronRose.Tests
{
    public class Vector3Tests
    {
        [Fact]
        public void Vector3_Add_ReturnsCorrectResult()
        {
            var a = new Vector3(1, 2, 3);
            var b = new Vector3(4, 5, 6);

            var result = a + b;

            Assert.Equal(5, result.x);
            Assert.Equal(7, result.y);
            Assert.Equal(9, result.z);
        }

        [Fact]
        public void Vector3_Magnitude_ReturnsCorrectLength()
        {
            var v = new Vector3(3, 4, 0);
            Assert.Equal(5.0f, v.magnitude, precision: 3);
        }
    }
}
```

**GitHub Actions 자동 빌드:**
```yaml
# .github/workflows/build.yml
name: Build IronRose

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

jobs:
  build:
    runs-on: windows-latest

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET 10
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '10.0.x'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore

    - name: Test
      run: dotnet test --no-build --verbosity normal
```

---

## 검증 기준

✅ 메모리 릭 없음 (장시간 실행 시 메모리 증가 없음)
✅ GPU 리소스 자동 해제
✅ 60 FPS 안정적 유지 (1000개 오브젝트)
✅ 핫 리로드 2초 이내
✅ 모든 단위 테스트 통과

---

## 성능 벤치마크 목표

| 항목 | 목표 | 측정 방법 |
|------|------|-----------|
| 프레임 레이트 | 60 FPS @ 1000 오브젝트 | PerformanceMonitor |
| 핫 리로드 시간 | < 2초 | Stopwatch |
| 메모리 사용량 | < 500MB | GC.GetTotalMemory |
| 컴파일 시간 | < 1초 (10KB 코드) | Stopwatch |

---

## 예상 소요 시간
**3-4일** (필수 항목만)
**+2-3일** (선택적 최적화 포함)

---

## 다음 단계
→ [Phase 10: 문서화 및 샘플](Phase10_Documentation.md)
