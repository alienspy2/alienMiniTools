# **IronRose 게임 엔진 마스터 플랜**

> **"Iron for Strength, Rose for Beauty"**
> AI-Native .NET 10 Game Engine - From Prompt to Play

---

## **프로젝트 개요**

**IronRose**는 AI(LLM)와의 협업을 최우선으로 설계된 .NET 10 기반 게임 엔진입니다.
Unity API 호환성을 유지하면서도 런타임 코드 생성 및 핫 리로딩에 특화되어 있으며,
무거운 에디터 대신 **"프롬프트로 게임을 만드는"** 새로운 개발 경험을 제공합니다.

**설계 원칙:**
- 🎯 **단순함이 최우선** - 복잡한 아키텍처보다 이해하기 쉬운 코드
- 🚀 **실용주의** - 이론보다 실제로 동작하는 것
- 🤖 **AI 친화적** - Unity 스타일 코드를 그대로 실행

---

## **Phase 0: 프로젝트 구조 및 환경 설정**

### 목표
프로젝트의 기본 골격을 만들고 개발 환경을 구축합니다.

### 작업 항목

#### 0.1 솔루션 구조 설계

> **플러그인 기반 핫 리로드 아키텍처**
>
> 엔진(IronRose.Engine)이 EXE 진입점이자 안정적 기반이고,
> 플러그인과 LiveCode를 핫 리로드합니다.

```
IronRose/
├── src/
│   ├── IronRose.Engine/            # 엔진 핵심 (EXE, 진입점 + 메인 루프)
│   │                                # - Silk.NET/Veldrid 초기화
│   │                                # - GameObject, Component, Transform
│   │                                # - MonoBehaviour 시스템
│   │                                # - 플러그인 매니저
│   │
│   ├── IronRose.Contracts/         # 플러그인 API 계약
│   ├── IronRose.Scripting/         # Roslyn 컴파일러
│   ├── IronRose.AssetPipeline/     # Unity 에셋 임포터
│   ├── IronRose.Rendering/         # 렌더링
│   └── IronRose.Physics/           # 물리 엔진
│
├── samples/
│   ├── 01_HelloWorld/
│   ├── 02_RotatingCube/
│   └── 03_AIGeneratedScene/
├── tests/
└── docs/
```

**핵심 구조:**
- ✅ IronRose.Engine (EXE, 안정적 기반)
- ✅ IronRose.Contracts (플러그인 API 컨테이너)
- ✅ **플러그인/LiveCode만 핫 리로드 대상**

#### 0.2 NuGet 패키지 설치
- **Veldrid** (+ Veldrid.SPIRV, Veldrid.ImageSharp) — GPU 렌더링
- **Silk.NET.Windowing** + **Silk.NET.Input** — 윈도우 생성 및 입력 처리 (GLFW 백엔드)
- **Microsoft.CodeAnalysis.CSharp** (Roslyn)
- **VYaml** 또는 **YamlDotNet** (Unity YAML 파싱)
- **AssimpNet** (3D 모델 로딩)
- **StbImageSharp** (텍스처 로딩 - 가볍고 빠른 MIT 라이선스)
- **Tomlyn** (TOML 직렬화/역직렬화)
- **BepuPhysics v2** (3D 물리 시뮬레이션)
- **Box2D.NetStandard** 또는 **Aether.Physics2D** (2D 물리 엔진)

#### 0.3 Git 저장소 초기화
```bash
git init
git add .
git commit -m "Initial commit: IronRose project structure"
```

---

## **Phase 1: 최소 실행 가능 엔진**

### 목표
IronRose.Engine(EXE)에서 SDL 윈도우를 열고 Veldrid로 화면을 클리어합니다.

### 작업 항목

#### 1.1 윈도우 생성 (IronRose.Engine)
```csharp
// Program.cs
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace IronRose.Engine
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("[IronRose] Engine Starting...");

            // 윈도우 생성 및 메인 루프
        }
    }
}
```

#### 1.2 Veldrid 그래픽 디바이스 초기화
```csharp
var options = new GraphicsDeviceOptions
{
    PreferStandardClipSpaceYDirection = true,
    PreferDepthRangeZeroToOne = true
};
var graphicsDevice = GraphicsDevice.CreateVulkan(options, window);
```

#### 1.3 기본 렌더링 루프
- ClearColor로 배경색 설정
- 60 FPS 타이머 구현
- SDL 이벤트 처리 (윈도우 닫기, 키보드 입력)

**검증 기준:**
✅ 파란색 화면이 뜨고 ESC 키로 종료할 수 있어야 함

---

## **Phase 2: Roslyn 핫 리로딩 시스템**

### 목표
런타임에 C# 코드를 컴파일하고 AssemblyLoadContext로 핫 리로딩하는 핵심 기능을 구현합니다.

> **설계 철학: 플러그인 기반 핫 리로드**
>
> 엔진(IronRose.Engine)은 안정적 기반으로 유지하고,
> 플러그인과 LiveCode를 핫 리로드합니다.

### 작업 항목

#### 2.1 Roslyn 컴파일러 래퍼 (IronRose.Scripting)
```csharp
public class ScriptCompiler
{
    public Assembly CompileFromSource(string sourceCode);
    public Assembly CompileFromFile(string csFilePath);
}
```

#### 2.2 AssemblyLoadContext 핫 스왑 구조

**ScriptDomain.cs (IronRose.Scripting):**
```csharp
public class ScriptDomain
{
    private AssemblyLoadContext? _currentALC;

    public void LoadScripts(byte[] assemblyBytes)
    {
        _currentALC = new AssemblyLoadContext("ScriptContext", isCollectible: true);
        using var ms = new MemoryStream(assemblyBytes);
        _currentALC.LoadFromStream(ms);
    }

    public void Reload(byte[] newAssemblyBytes)
    {
        UnloadPreviousContext();
        LoadScripts(newAssemblyBytes);
    }
}
```

**핵심:**
- 플러그인(DLL)은 ALC로 핫 리로드
- LiveCode(*.cs)는 Roslyn으로 핫 리로드
- 엔진 코어는 안정적으로 유지

#### 2.3 상태 보존 시스템
- 핫 리로드 전 객체 상태를 TOML로 직렬화
- 새 어셈블리 로드 후 상태 복원
```csharp
public interface IHotReloadable
{
    string SerializeState();    // TOML 형식으로 반환
    void DeserializeState(string toml);
}
```

#### 2.4 테스트: "Hello World" 스크립트
```csharp
// LiveCode/TestScript.cs
public class TestScript
{
    public void Update()
    {
        Console.WriteLine($"Frame: {Time.frameCount}");
    }
}
```
- 런타임에 이 스크립트를 컴파일하고 로드
- 코드를 수정하면 재컴파일 후 핫 리로드
- 게임 루프가 중단되지 않고 계속 실행

**검증 기준:**
✅ 코드 수정 → 저장 → 자동 리로드 → 즉시 반영 (게임 중단 없음)

---

## **Phase 3: Unity Architecture 구현** ✅ (2026-02-14 완료, 3.5++ 호환성 확장 포함)

### 목표
Unity의 GameObject/Component 아키텍처를 **있는 그대로** 구현합니다.
Shim(껍데기)이 아닌 실제 동작하는 엔진 구조입니다.

### 설계 철학
> **"Keep It Simple, Stupid (KISS)"**
>
> - ECS 변환 레이어 없음
> - 내부/외부 구조 분리 없음
> - Unity 아키텍처 그대로 구현
> - 성능 문제는 나중에 병목이 실제로 발생하면 최적화

### 작업 항목

#### 3.1 기본 수학 타입 (IronRose.Engine)
```csharp
namespace RoseEngine
{
    public struct Vector3
    {
        public float x, y, z;

        public static Vector3 zero => new(0, 0, 0);
        public static Vector3 one => new(1, 1, 1);

        public float magnitude => MathF.Sqrt(x*x + y*y + z*z);
        public Vector3 normalized => this / magnitude;
    }

    public struct Quaternion
    {
        public float x, y, z, w;

        public static Quaternion identity => new(0, 0, 0, 1);
        public static Quaternion Euler(float x, float y, float z);
    }

    public struct Color { public float r, g, b, a; }
}
```

#### 3.2 GameObject & Component 시스템
```csharp
namespace RoseEngine
{
    public class GameObject
    {
        public string name;
        public Transform transform;
        private List<Component> _components = new();

        public GameObject(string name = "GameObject")
        {
            this.name = name;
            this.transform = AddComponent<Transform>();
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T();
            component.gameObject = this;
            component.transform = this.transform;
            _components.Add(component);

            // MonoBehaviour면 자동으로 업데이트 루프에 등록
            if (component is MonoBehaviour mb)
                SceneManager.RegisterBehaviour(mb);

            return component;
        }

        public T GetComponent<T>() where T : Component
        {
            return _components.OfType<T>().FirstOrDefault();
        }
    }

    public class Component
    {
        public GameObject gameObject;
        public Transform transform;
    }

    public class Transform : Component
    {
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;

        public void Rotate(float x, float y, float z)
        {
            rotation *= Quaternion.Euler(x, y, z);
        }
    }
}
```

#### 3.3 MonoBehaviour 라이프사이클
```csharp
public class MonoBehaviour : Component
{
    public virtual void Awake() { }
    public virtual void Start() { }
    public virtual void Update() { }
    public virtual void LateUpdate() { }
    public virtual void OnDestroy() { }
}
```

#### 3.4 씬 관리 및 업데이트 루프 (IronRose.Engine)
```csharp
public class SceneManager
{
    private static List<MonoBehaviour> _behaviours = new();

    public static void RegisterBehaviour(MonoBehaviour behaviour)
    {
        _behaviours.Add(behaviour);
        behaviour.Awake();
        behaviour.Start();
    }

    public static void Update(float deltaTime)
    {
        Time.deltaTime = deltaTime;

        // 단순하게 리스트 순회
        foreach (var behaviour in _behaviours)
        {
            behaviour.Update();
        }

        foreach (var behaviour in _behaviours)
        {
            behaviour.LateUpdate();
        }
    }
}

public static class Time
{
    public static float deltaTime;
    public static float time;
}
```

#### 3.5 디버그 유틸리티
```csharp
public static class Debug
{
    public static void Log(object message) => Console.WriteLine($"[LOG] {message}");
    public static void LogWarning(object message) => Console.WriteLine($"[WARN] {message}");
    public static void LogError(object message) => Console.WriteLine($"[ERROR] {message}");
}
```

#### 3.6 Unity InputSystem (액션 기반 입력) ✅
기존 `RoseEngine.Input` (레거시)을 유지하면서, Unity 새 Input System (`RoseEngine.InputSystem`) API를 구현합니다.
기존 Silk.NET 입력 인프라 위에 액션 기반 API 레이어를 구축합니다.

```csharp
using RoseEngine.InputSystem;

var moveAction = new InputAction("Move", InputActionType.Value);
moveAction.AddCompositeBinding("2DVector")
    .With("Up", "<Keyboard>/w")
    .With("Down", "<Keyboard>/s")
    .With("Left", "<Keyboard>/a")
    .With("Right", "<Keyboard>/d");

var jumpAction = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
jumpAction.performed += ctx => Debug.Log("Jump!");

moveAction.Enable();
jumpAction.Enable();

// Update에서:
Vector2 move = moveAction.ReadValue<Vector2>();
```

**구현 파일 (7개):**
```
RoseEngine/InputSystem/
├── InputActionType.cs      # enum: Button, Value, PassThrough
├── InputActionPhase.cs     # enum: Disabled, Waiting, Started, Performed, Canceled
├── InputBinding.cs         # 바인딩 사양 + CompositeBinder
├── InputControlPath.cs     # 경로 파싱 ("<Keyboard>/space" → KeyCode)
├── InputAction.cs          # 핵심 액션 클래스 + CallbackContext
├── InputActionMap.cs       # 액션 그룹
└── InputSystem.cs          # 정적 매니저 (Update 루프 연동)
```

**검증 기준:**
✅ Unity 스타일 스크립트 작성 가능
```csharp
public class RotatingCube : MonoBehaviour
{
    void Update()
    {
        transform.Rotate(0, Time.deltaTime * 45, 0);
    }
}
```

---

## **Phase 4: 기본 렌더링 파이프라인**

### 목표
Veldrid를 사용하여 3D 메시를 화면에 그리는 기본 Forward Rendering을 구현합니다.

### 작업 항목

#### 4.1 메시 렌더링 시스템
```csharp
public class MeshRenderer : Component
{
    public Mesh mesh;
    public Material material;
}

public class Mesh
{
    public Vertex[] vertices;
    public uint[] indices;
    public DeviceBuffer vertexBuffer;
    public DeviceBuffer indexBuffer;
}
```

#### 4.2 기본 셰이더 (GLSL → SPIR-V)
```glsl
// vertex.glsl
#version 450
layout(location = 0) in vec3 Position;
layout(set = 0, binding = 0) uniform WorldBuffer { mat4 World; };
layout(set = 0, binding = 1) uniform ViewBuffer { mat4 View; mat4 Projection; };

void main()
{
    gl_Position = Projection * View * World * vec4(Position, 1.0);
}

// fragment.glsl
#version 450
layout(location = 0) out vec4 fsout_Color;
void main()
{
    fsout_Color = vec4(1.0, 0.5, 0.2, 1.0); // 주황색
}
```

#### 4.3 카메라 시스템
```csharp
public class Camera : Component
{
    public float fieldOfView = 60f;
    public float nearClipPlane = 0.1f;
    public float farClipPlane = 1000f;

    public Matrix4x4 GetViewMatrix();
    public Matrix4x4 GetProjectionMatrix();
}
```

#### 4.4 큐브 프리미티브 생성
```csharp
GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
cube.transform.position = new Vector3(0, 0, 5);
```

**검증 기준:**
✅ 3D 주황색 큐브가 화면 중앙에 렌더링됨
✅ 카메라를 이동하면 큐브의 시점이 변경됨

---

## **Phase 5: Unity 에셋 임포터** ✅ (2026-02-14 완료)

### 목표
Unity의 .unity (Scene), .prefab, .fbx, .png 파일을 로드할 수 있게 만듭니다.

### 작업 항목

#### 5.1 YAML 파서 통합 (IronRose.AssetPipeline)
- VYaml 또는 YamlDotNet 사용
- Unity의 `!u!` 태그 처리
- GUID → AssetID 매핑 테이블 구축

#### 5.2 .prefab 로더
```csharp
public class PrefabImporter
{
    public GameObject LoadPrefab(string prefabPath);
}
```

#### 5.3 .fbx 메시 로더 (AssimpNet)
```csharp
public class MeshImporter
{
    public Mesh Import(string fbxPath);
}
```

#### 5.4 .png 텍스처 로더 (ImageSharp)
```csharp
public class TextureImporter
{
    public Texture2D Import(string pngPath);
}
```

**검증 기준 (전항목 통과):**
✅ GLB/FBX 모델 로드 + 텍스처 적용 정상
✅ `.rose` 메타데이터 파일 자동 생성 (TOML 기반)
✅ AssetDatabase GUID 매핑 정상 작동
✅ MeshImporter 머티리얼 자동 추출 (albedo, metallic, roughness, emission)
✅ SpriteRenderer + TextRenderer 3D 공간 렌더링 (Phase 5A/5B)

---

## **Phase 6: 물리 엔진 통합** ✅ (2026-02-14 완료)

> 상세 계획: [Phase6_PhysicsEngine.md](Phase6_PhysicsEngine.md)

### 목표
3D 및 2D 물리 시뮬레이션을 통합하여 Unity의 물리 기능을 재현합니다.

### 아키텍처
- **IronRose.Physics**: BepuPhysics v2.4.0 + Aether.Physics2D v2.2.0 순수 래퍼 (System.Numerics 타입)
- **IronRose.Engine**: Unity API 래퍼 (Component 상속) + PhysicsManager 통합

### 작업 항목

#### 6.0 사전 작업: FixedUpdate 인프라
- MonoBehaviour.FixedUpdate() + 충돌 콜백 (OnCollisionEnter/Stay/Exit, OnTriggerEnter/Stay/Exit)
- EngineCore Fixed timestep 누적기 (50Hz)
- SceneManager.FixedUpdate() 루프
- Time.fixedDeltaTime

#### 6.1 3D 물리: BepuPhysics v2
```csharp
// IronRose.Physics — 순수 래퍼
public class PhysicsWorld3D : IDisposable
{
    public void Initialize(Vector3 gravity);
    public void Step(float deltaTime);
    public BodyHandle AddDynamicBody(Vector3 pos, Quaternion rot, TypedIndex shape, float mass);
    public TypedIndex AddBoxShape(float x, float y, float z);
}

// IronRose.Engine/RoseEngine — Unity API
public class Rigidbody : Component { /* velocity, mass, AddForce, SyncFromPhysics */ }
public abstract class Collider : Component { /* isTrigger, center */ }
public class BoxCollider : Collider { /* size */ }
public class SphereCollider : Collider { /* radius */ }
```

#### 6.2 2D 물리: Aether.Physics2D
```csharp
// IronRose.Physics — 순수 래퍼
public class PhysicsWorld2D : IDisposable
{
    public void Initialize(float gravityX, float gravityY);
    public void Step(float deltaTime);
    public Body CreateDynamicBody(float posX, float posY);
    public void AttachRectangle(Body body, float w, float h, float density);
}

// IronRose.Engine/RoseEngine — Unity API
public class Rigidbody2D : Component { /* velocity, gravityScale, AddForce */ }
public abstract class Collider2D : Component { /* isTrigger, offset */ }
public class BoxCollider2D : Collider2D { /* size */ }
public class CircleCollider2D : Collider2D { /* radius */ }
```

#### 6.3 PhysicsManager (IronRose.Engine)
- PhysicsWorld3D/2D 통합 관리
- Transform ↔ Physics 양방향 동기화
- 충돌 콜백 디스패치

#### 6.4 Unity 물리 유틸리티
```csharp
public static class Physics { Raycast, OverlapSphere, CheckSphere }
public static class Physics2D { Raycast, OverlapCircle }
public class Collision { contacts, relativeVelocity }
```

**검증 기준 (전항목 통과):**
✅ 큐브가 바닥으로 떨어지는 중력 시뮬레이션
✅ MonoBehaviour.FixedUpdate() 50Hz 호출
✅ Transform↔Physics 양방향 동기화 정상
✅ PhysicsDemo3D 데모 씬 정상 작동

**참고 자료:**
- [BepuPhysics v2](https://github.com/bepu/bepuphysics2)
- [Aether.Physics2D](https://github.com/tainicom/Aether.Physics2D)

---

## **Phase 7: Deferred Rendering & PBR** ✅ (2026-02-15 완료)

> 상세 계획: [Phase7_DeferredPBR.md](Phase7_DeferredPBR.md)

### 목표
고급 렌더링 파이프라인을 구축하여 현대적인 게임 그래픽을 지원합니다.

### 작업 항목

#### 6.1 G-Buffer 생성
- RT0: Albedo (RGB) + Alpha
- RT1: Normal (RGB) + Smoothness (A)
- RT2: Metallic (R) + Occlusion (G) + Emission (B)
- Depth Buffer

#### 6.2 Geometry Pass 셰이더
```glsl
layout(location = 0) out vec4 gAlbedo;
layout(location = 1) out vec4 gNormal;
layout(location = 2) out vec4 gMaterial;

void main()
{
    gAlbedo = texture(albedoMap, UV);
    gNormal = vec4(normalize(Normal), smoothness);
    gMaterial = vec4(metallic, occlusion, emission, 1.0);
}
```

#### 6.3 Lighting Pass (PBR)
- Cook-Torrance BRDF 구현
- Image-Based Lighting (IBL) - 추후
- 다중 광원 지원

#### 6.4 Post-Processing
- Bloom
- Tone Mapping (ACES)
- Temporal Anti-Aliasing (TAA)

**검증 기준 (전항목 통과):**
✅ 금속/플라스틱 재질이 물리적으로 정확하게 렌더링됨 (5x5 구체 그리드)
✅ Cook-Torrance BRDF + IBL(큐브맵) 기반 PBR 라이팅
✅ Bloom + ACES Tone Mapping 포스트프로세싱
✅ Forward/Deferred 하이브리드 (Sprite/Text 정상 공존)
✅ 60 FPS 안정 유지

---

## **마일스톤 타임라인**

| Phase | 예상 | 실제 | 주요 산출물 |
|-------|------|------|------------|
| Phase 0-2 | 2주 | **1일** ✅ | 윈도우 + 핫 리로딩 동작 |
| Phase 3-4 | 3주 | **2일** ✅ | Unity 스크립트 실행 + 3D 렌더링 |
| Phase 5 | 2주 | **1일** ✅ | Unity 에셋 로드 + Sprite/Text |
| Phase 6 | 1-2주 | **1일** ✅ | 물리 엔진 통합 (3D + 2D) |
| Phase 7 | 3주 | **1일** ✅ | Deferred PBR + IBL + Post-Processing |
| **Total** | **17-18주** | **3일 (Phase 0-7)** | |

---

## **기술적 도전 과제 및 해결 방안**

### 🔥 도전 과제 1: Roslyn 컴파일 속도
**문제:** 큰 프로젝트는 컴파일에 수 초 소요
**단순한 접근:**
- Phase 1-8: 기본 Roslyn 컴파일만 사용 (2-3초면 충분)
- 실제로 너무 느려지면 그때 최적화:
  - 증분 컴파일 (변경된 파일만)
  - Syntax Tree 캐싱
  - AOT 미리 컴파일 옵션 제공

### 🔥 도전 과제 2: Unity 에셋 완벽 호환
**문제:** Unity의 모든 에셋 타입을 지원하기 어려움
**해결:**
- 우선순위: Scene, Prefab, Mesh, Texture
- 나머지는 점진적 추가 또는 Unity 플러그인으로 내보내기 도구 제공

### 🔥 도전 과제 3: AI 생성 코드의 안전성
**문제:** AI가 버그나 악의적 코드를 생성할 수 있음
**해결:**
- 샌드박스 환경에서 먼저 실행
- 코드 정적 분석 (Roslyn Analyzers)
- 사용자 승인 단계 추가

---

## **성공 지표 (KPI)**

- ✅ **핫 리로드 시간**: 2초 이내
- ✅ **Unity 스크립트 호환률**: 80% 이상
- ✅ **렌더링 성능**: 1000개 오브젝트 @ 60 FPS
- ✅ **AI 코드 생성 정확도**: 70% 이상 (첫 시도에 작동)
- ✅ **커뮤니티 참여**: GitHub Stars 1000개 이상 (6개월 내)

---

## **프로젝트 철학**

> **"Simplicity is the ultimate sophistication."** - Leonardo da Vinci

IronRose는 단순히 Unity를 복제하는 것이 아니라,
**AI 시대의 게임 개발 방식**을 재정의하는 실험입니다.

### 핵심 가치

1. **단순성 (Simplicity First)**
   - 복잡한 아키텍처보다 이해하기 쉬운 코드
   - Shim 레이어, ECS 변환 같은 간접 레이어 없음
   - 직관적인 Unity 아키텍처 그대로 구현

2. **실용주의 (Pragmatism over Perfectionism)**
   - 이론적 완벽함보다 실제로 동작하는 것
   - 과도한 엔지니어링 금지
   - 병목이 발생하면 그때 최적화

3. **AI 친화성 (AI-First Design)**
   - 에디터 없이도 게임을 만들 수 있어야 합니다.
   - 코드는 실행되는 동안 계속 진화할 수 있어야 합니다.
   - AI는 개발자의 파트너이자 학습 도구여야 합니다.

4. **극한의 유연성 (플러그인 기반 핫 리로드)**
   - **플러그인/LiveCode 핫 리로드** - 빠른 반복 개발
   - 엔진은 안정적 기반, 플러그인으로 기능 확장
   - AI Digest로 검증된 플러그인 코드를 엔진에 통합

> **"Make it work, make it right, make it fast - in that order."**

**IronRose - Simple, AI-Native, .NET-Powered**

---

## **현재 상태 및 다음 단계**

1. ✅ Phase 0-7 완료 (2026-02-13 ~ 2026-02-15, 3일)
2. ✅ 핵심 엔진 기능 완성: 핫 리로드, Unity 아키텍처, 3D 렌더링, 에셋 임포트, 물리, Deferred PBR
3. ✅ ~11,255줄 C# + ~921줄 셰이더, 59개 RoseEngine 컴포넌트
4. 🔲 **Phase 8 시작**: AI 통합 (LLM API + 코드 생성 + 샌드박싱)
5. 🔲 Phase 9-11: 최적화, 문서화, 커뮤니티 공개

**IronRose - Simple, AI-Native, .NET-Powered**
