# IronRose 개발 진행 상황

**최종 업데이트**: 2026-02-14 (Phase 3.5+ Unity InputSystem 구현)

---

## 전체 진행도

- [x] Phase 0: 프로젝트 구조 및 환경 설정 ✅
- [x] Phase 1: 최소 실행 가능 엔진 (Bootstrapper) ✅
- [x] Phase 2: Roslyn 핫 리로딩 시스템 (LiveCode) ✅
- [x] Phase 2A: Engine Core 핫 리로딩 ✅
- [x] Phase 2B-0: Bootstrapper/Engine 통합 ✅
- [x] Phase 3: Unity Architecture (GameObject, Component) ✅
- [x] Phase 3.5: 입력 시스템 (Silk.NET 마이그레이션) ✅
- [x] Phase 3.5+: Unity InputSystem (액션 기반 입력) ✅
- [x] Phase 3.5++: Unity 호환성 확장 (Mathf, Random, Object, Destroy, Coroutine, Transform 계층 등) ✅
- [ ] Phase 4: 고급 렌더링 파이프라인
- [ ] Phase 5: 스크립팅 시스템
- [ ] Phase 6: 에셋 파이프라인
- [ ] Phase 7: 핫 리로드
- [ ] Phase 8: 최적화 및 안정화

---

## Phase 0: 프로젝트 구조 및 환경 설정 ✅

**완료 날짜**: 2026-02-13
**소요 시간**: ~1시간

### 완료된 작업

#### 1. 환경 설정
- [x] .NET 10.0.101 SDK 설치 확인
- [x] 프로젝트 문서 검토

#### 2. 프로젝트 구조 생성
- [x] IronRose.sln 솔루션 생성
- [x] IronRose.Engine 프로젝트 생성
- [x] IronRose.Scripting 프로젝트 생성
- [x] IronRose.AssetPipeline 프로젝트 생성
- [x] IronRose.Rendering 프로젝트 생성
- [x] IronRose.Physics 프로젝트 생성
- [x] IronRose.Bootstrapper 프로젝트 생성

#### 3. 프로젝트 참조 설정
- [x] Bootstrapper → 모든 모듈 참조
- [x] 각 모듈 → Engine 참조

#### 4. NuGet 패키지 설치
- [x] **Rendering**: Veldrid, Veldrid.SPIRV, Veldrid.ImageSharp
- [x] **Windowing/Input**: Silk.NET.Windowing, Silk.NET.Input (GLFW 백엔드)
- [x] **Scripting**: Microsoft.CodeAnalysis.CSharp 5.0.0
- [x] **AssetPipeline**: YamlDotNet, AssimpNet, SixLabors.ImageSharp
- [x] **Engine**: Tomlyn
- [x] **Physics**: BepuPhysics, Aether.Physics2D

#### 5. 개발 환경 설정
- [x] .vscode/launch.json 생성
- [x] .vscode/tasks.json 생성
- [x] .editorconfig 생성 (UTF-8 BOM 강제)
- [x] .gitignore 생성

#### 6. 문서화
- [x] Claude.md 개선 (개발 가이드라인)
- [x] Progress.md 생성 (이 파일)

#### 7. 빌드 및 테스트
- [x] `dotnet build` 성공 (경고 14개, 오류 0개)
- [x] `dotnet run` 실행 확인 ("Hello, World!" 출력)

### 주요 결정 사항
- **아키텍처**: 모든 엔진 코드를 동적 로드 가능하도록 설계
- **부트스트래퍼**: 최소한의 고정 코드만 포함
- **인코딩**: UTF-8 BOM을 강제하여 인코딩 문제 방지
- **자동화**: AI 자동화를 위한 JSON 명령 인터페이스 설계
- **메인 테마 색상**: 금속의 백장미 (Metallic White Rose)
  - RGB: (230, 220, 210) - 은은한 베이지 톤
  - 정규화: (0.902f, 0.863f, 0.824f)
  - Hex: #E6DCD2
  - 용도: 배경, UI 기본 색상, 엔진 로고

### 알려진 이슈
- ~~⚠️ SixLabors.ImageSharp 1.0.4 보안 취약성 경고~~ → ✅ 3.1.12로 해결 (2026-02-14)

---

## Phase 1: 최소 실행 가능 엔진 (Bootstrapper) ✅

**시작 날짜**: 2026-02-13
**완료 날짜**: 2026-02-13
**소요 시간**: ~2시간

### 완료된 작업

#### 1. Bootstrapper 기본 구조
- [x] Program.cs 생성 (간소화된 메인 루프)
- [x] unsafe 코드 허용 설정

#### 2. GraphicsManager 구현
- [x] Veldrid.StartupUtilities를 사용한 윈도우 생성
- [x] Vulkan GraphicsDevice 초기화
- [x] CommandList 생성 및 렌더링 루프
- [x] IronRose 테마 색상 (금속의 백장미) 적용

#### 3. 빌드 및 실행 테스트
- [x] `dotnet build` 성공 (경고 14개, 오류 0개)
- [x] `dotnet run` 실행 확인
- [x] 1280x720 윈도우 생성 확인
- [x] Vulkan 초기화 성공
- [x] 메인 루프 진입 확인

### 주요 결정 사항
- **윈도우 생성**: Phase 3.5에서 Silk.NET.Windowing으로 전환 완료
  - 기존 Veldrid.Sdl2 → Silk.NET.Windowing (GLFW 백엔드) + 네이티브 핸들 Veldrid 연동
  - Bootstrapper를 더 간소화 (~50줄)
- **렌더링**: Vulkan 백엔드 사용
  - 성공적으로 초기화됨
  - IronRose 테마 색상으로 화면 클리어

#### 4. 타이밍 시스템
- [x] Stopwatch 기반 델타타임 계산
- [x] FPS 카운터 (매초 출력)
- [x] 프레임타임 로그
- [x] 60 FPS 타겟 프레임 제한

### 테스트 결과
- ✅ 윈도우 정상 생성 (1280x720)
- ✅ Vulkan 초기화 성공
- ✅ 금속 백장미 색상 렌더링
- ✅ **FPS: 59-60** (안정적)
- ✅ **프레임타임: 16-17ms** (목표: 16.67ms)

---

## Phase 2: Roslyn 핫 리로딩 시스템 ✅

**시작 날짜**: 2026-02-13
**완료 날짜**: 2026-02-13
**소요 시간**: ~1시간

### 완료된 작업

#### 1. Roslyn 컴파일러 래퍼
- [x] ScriptCompiler.cs 구현 (~125줄)
- [x] System.Runtime 참조 추가
- [x] 컴파일 성공/실패 로깅

#### 2. AssemblyLoadContext 핫 스왑
- [x] ScriptDomain.cs 구현 (~135줄)
- [x] 동적 어셈블리 로드/언로드
- [x] GC 기반 메모리 정리
- [x] 스크립트 인스턴스 자동 생성

#### 3. 상태 보존 시스템
- [x] IHotReloadable 인터페이스
- [x] StateManager 구현 (TOML 기반)

#### 4. 스크립팅 통합
- [x] Program.cs 업데이트 (스크립팅 시스템 통합)
- [x] FileSystemWatcher 설정
- [x] LiveCode 디렉토리 구조

#### 5. 테스트
- [x] TestScript.cs 컴파일 성공
- [x] 스크립트 로드 및 Update() 호출 확인
- [x] 코드 변경 시 새 버전 로드 (재시작 시)

### 주요 결정 사항
- **Roslyn 기반 컴파일**: Microsoft.CodeAnalysis.CSharp 사용
- **AssemblyLoadContext**: isCollectible: true로 메모리 관리
- **Update() 패턴**: MonoBehaviour 없이도 스크립트 실행 가능

### 테스트 결과
- ✅ TestScript.cs 컴파일 (3072 bytes)
- ✅ Update() 매 프레임 호출
- ✅ "Frame: 60, 120, 180..." 출력 확인
- ✅ 스크립트 수정 후 재시작 시 새 버전 로드

### 알려진 이슈
- ⚠️ FileSystemWatcher 실시간 감지 불안정
  - 엔진 실행 중 파일 수정 시 자동 리로드 안 됨
  - 엔진 재시작 시에는 정상 작동
  - NotifyFilters 설정 개선 필요

---

## Phase 2A: Engine Core 핫 리로딩 ✅

**시작 날짜**: 2026-02-13
**완료 날짜**: 2026-02-13
**소요 시간**: ~3시간

### 완료된 작업

#### 1. 아키텍처 재구성
- [x] IronRose.Contracts 프로젝트 생성 (인터페이스 분리)
- [x] IEngineCore 인터페이스 정의
- [x] Bootstrapper → Contracts만 참조 (정적 참조 제거)

#### 2. Engine 동적 로딩
- [x] EngineCore.cs 구현 (IEngineCore)
- [x] EngineLoader.cs (~170줄)
  - AssemblyLoadContext (isCollectible)
  - Shadow Copy (LoadFromStream - 파일 잠금 없음)
  - 26개 의존성 자동 로드

#### 3. 파일 감지 및 자동 빌드
- [x] EngineWatcher.cs (~120줄)
  - FileSystemWatcher (src/**/*.cs)
  - 디바운싱 타이머 (1초)
  - bin-hot 폴더 전략

#### 4. 핫 리로드 완성
- [x] bin-hot/{timestamp}/ 폴더로 빌드
- [x] 파일 잠금 없이 빌드 성공
- [x] 기존 엔진 언로드 (ALC + GC)
- [x] 새 엔진 로드 from bin-hot
- [x] **코드 변경 즉시 반영!**

### 주요 결정 사항
- **bin-hot 전략**: 파일 잠금 없이 빌드
  - 런타임: bin-hot/{timestamp}/*.dll 사용
  - 다음 실행: 변경된 소스가 bin/*.dll에 반영됨
- **Shadow Copy**: LoadFromStream으로 파일 잠금 방지
- **Contracts 분리**: 기본 ALC에만 로드 (타입 격리)
- **Window.Close() 생략**: SDL2 블록 문제 우회

### 테스트 결과
```
[EngineWatcher] Build SUCCESS ✅
[EngineLoader] Unloading engine... ✅
[EngineLoader] Engine ALC unloaded successfully ✅
[EngineLoader] HOT RELOAD: Loading from bin-hot/... ✅
╔════════════════════════════════════════════════════╗
║ ✨✨✨ FULL HOT RELOAD WORKING!!! ✨✨✨        ║
╚════════════════════════════════════════════════════╝
```

#### 추가 완료 사항 (2026-02-13)
- [x] **ALC 타입 격리 해결**
  - Veldrid, Silk.NET 라이브러리를 기본 ALC에서만 로드
  - Bootstrapper의 Sdl2Window와 Engine의 Sdl2Window 타입 일치
  - **윈도우 보존 성공!** (핫 리로드 시 재생성 없음)

- [x] **스크린샷 기능 구현**
  - logs/*.png로 자동 캡처 (프레임 1, 60, 매 300프레임)
  - AI가 화면을 읽고 분석 가능
  - SwapBuffers() 전 캡처로 실제 렌더링 결과 저장

- [x] **핫 리로드 검증**
  - 색상 변경 테스트 (베이지 → 파란 → 베이지)
  - 스크린샷으로 변경사항 확인
  - **완전히 작동하는 핫 리로드 확인!**

### 해결된 이슈
- ✅ ~~핫 리로드 후 윈도우 재생성~~ → Bootstrapper가 윈도우 관리
- ✅ ~~ALC 타입 격리~~ → Veldrid를 기본 ALC에서만 로드
- ✅ ~~ALC not fully unloaded~~ → 정상 언로드 확인

---

## Phase 2B-0: Bootstrapper/Engine 통합 ✅

**완료 날짜**: 2026-02-13

### 완료된 작업
- [x] Engine.csproj → EXE 전환 (OutputType, AllowUnsafeBlocks, Veldrid.StartupUtilities, Rendering 참조)
- [x] Program.cs를 Engine으로 이동 (EngineLoader/EngineWatcher/IEngineCore 제거)
- [x] EngineCore.cs 리플렉션 제거 (직접 참조로 전환)
- [x] IEngineCore.cs 삭제 (Contracts 프로젝트는 유지)
- [x] Bootstrapper 디렉토리 전체 삭제
- [x] IronRose.sln에서 Bootstrapper 제거
- [x] 부수 파일 업데이트 (launch.json, Claude.md, GraphicsManager 주석)

### 주요 결정 사항
- **전략 변경**: Phase 2A(엔진 전체 핫 리로드) → Phase 2B(플러그인 기반 핫 리로드)
- **Bootstrapper 제거**: 엔진을 동적 로드할 필요가 없으므로 통합
- **리플렉션 제거**: IronRose.Rendering을 직접 참조하여 타입 안전성 확보
- **Contracts 유지**: 향후 플러그인 API 컨테이너로 활용 예정

---

## Phase 3: Unity Architecture ✅

**시작 날짜**: 2026-02-14
**완료 날짜**: 2026-02-14
**소요 시간**: ~2시간

### 완료된 작업

#### Wave 1: 수학 타입 + 유틸리티 (신규 6파일)
- [x] `UnityEngine/Vector3.cs` - x,y,z struct, 연산자, Dot, Cross, Lerp, Distance
- [x] `UnityEngine/Vector2.cs` - x,y struct, 기본 연산자, Vector3 implicit 변환
- [x] `UnityEngine/Quaternion.cs` - x,y,z,w struct, Euler(), AngleAxis(), operator*
- [x] `UnityEngine/Color.cs` - r,g,b,a struct, 프리셋 색상 (white, red, blue 등)
- [x] `UnityEngine/Time.cs` - static deltaTime, time, frameCount (internal set)
- [x] `UnityEngine/Debug.cs` - static Log, LogWarning, LogError

#### Wave 2: Component 계층 (신규 5파일)
- [x] `UnityEngine/Component.cs` - 기본 클래스 (gameObject, transform, GetComponent<T>)
- [x] `UnityEngine/Transform.cs` - position, rotation, localScale, Translate(), Rotate()
- [x] `UnityEngine/MonoBehaviour.cs` - virtual Awake/Start/Update/LateUpdate/OnDestroy
- [x] `UnityEngine/SceneManager.cs` - RegisterBehaviour, Update(deltaTime), Clear()
- [x] `UnityEngine/GameObject.cs` - AddComponent<T>, AddComponent(Type), GetComponent<T>

#### Wave 3: 기존 파일 수정 (2파일)
- [x] **ScriptDomain.cs 수정**
  - `GetLoadedTypes()` 메서드 추가 (로드된 타입 노출)
  - `SetTypeFilter(Func<Type, bool>)` 추가 (MonoBehaviour 필터링)
  - ALC `Resolving` 이벤트 핸들러 추가 (default ALC fallback)
- [x] **EngineCore.cs 수정**
  - 엔진 어셈블리 참조 추가 (UnityEngine 타입 컴파일용)
  - TypeFilter 설정 (MonoBehaviour를 legacy 인스턴스화에서 제외)
  - `RegisterMonoBehaviours()` 신규 메서드 (GameObject 생성 + AddComponent + Awake)
  - `CompileAndLoadScripts()`: SceneManager.Clear() → Reload → RegisterMonoBehaviours 흐름
  - `Update()`: SceneManager.Update() 호출 추가
  - `Shutdown()`: SceneManager.Clear() 호출 추가

#### Wave 4: LiveCode 스크립트 전환
- [x] `LiveCode/TestScript.cs` → MonoBehaviour 패턴으로 전환
  - Screen.SetClearColor + transform.Rotate + Time/Debug 사용
- [x] `LiveCode/AnotherScript.cs` → MonoBehaviour 패턴으로 전환

### 주요 설계 결정

1. **UnityEngine 네임스페이스 위치**: `src/IronRose.Engine/UnityEngine/`
   - IronRose.Engine.dll (default ALC)에 위치
   - LiveCode ALC가 IronRose.Engine 참조 시 default ALC로 fallback → 타입 동일성 보장
   - `typeof(MonoBehaviour).IsAssignableFrom(liveCodeType)` 정상 동작

2. **Transform 자기참조 부트스트랩**
   - GameObject 생성자에서 Transform을 직접 생성하여 수동 와이어링
   - AddComponent를 거치지 않아 null 참조 문제 해결

3. **MonoBehaviour vs Legacy 이중 처리**
   - MonoBehaviour: EngineCore → GameObject 생성 + AddComponent → SceneManager 관리
   - Legacy (Update()만 있는 클래스): 기존 ScriptDomain 리플렉션 방식 유지
   - ScriptDomain에 TypeFilter로 MonoBehaviour 제외

4. **SceneManager 라이프사이클**
   - RegisterBehaviour: Awake() 즉시 호출, pendingStart 큐에 추가
   - Update: pending Start() 처리 → Update() 전체 → LateUpdate() 전체 → frameCount++
   - Clear: OnDestroy() 호출 후 전체 정리

5. **핫 리로드 흐름**
   ```
   파일 변경 감지 → SceneManager.Clear() (OnDestroy 호출)
   → ScriptDomain.Reload() (ALC 언로드/재로드)
   → EngineCore.RegisterMonoBehaviours() (GameObject 생성, Awake 호출)
   → 다음 프레임 SceneManager.Update()에서 Start() 호출
   ```

### 테스트 결과
- ✅ `dotnet build` 성공 (오류 0개)
- ✅ 총 11개 신규 파일 생성 + 4개 기존 파일 수정
- ✅ Unity 스타일 스크립팅 패턴 동작 확인

### 신규 파일 목록 (11개)
```
src/IronRose.Engine/UnityEngine/
├── Vector3.cs        (~65줄)
├── Vector2.cs        (~60줄)
├── Quaternion.cs     (~100줄)
├── Color.cs          (~55줄)
├── Time.cs           (~10줄)
├── Debug.cs          (~20줄)
├── Component.cs      (~12줄)
├── Transform.cs      (~40줄)
├── MonoBehaviour.cs   (~15줄)
├── SceneManager.cs    (~90줄)
└── GameObject.cs      (~60줄)
```

---

## Phase 3.5+: Unity InputSystem (액션 기반 입력) ✅

**완료 날짜**: 2026-02-14

### 완료된 작업

#### 신규 파일 (7개)
- [x] `UnityEngine/InputSystem/InputActionType.cs` - enum: Button, Value, PassThrough
- [x] `UnityEngine/InputSystem/InputActionPhase.cs` - enum: Disabled, Waiting, Started, Performed, Canceled
- [x] `UnityEngine/InputSystem/InputBinding.cs` - 바인딩 사양 + CompositeBinder (fluent `.With()` API)
- [x] `UnityEngine/InputSystem/InputControlPath.cs` - 경로 파싱 (`<Keyboard>/space` → `KeyCode.Space`) + 레거시 Input 재활용
- [x] `UnityEngine/InputSystem/InputAction.cs` - 핵심 액션 클래스 + CallbackContext + phase 전이 + `ReadValue<T>()`
- [x] `UnityEngine/InputSystem/InputActionMap.cs` - 액션 그룹 (AddAction, FindAction, Enable/Disable)
- [x] `UnityEngine/InputSystem/InputSystem.cs` - 정적 매니저 (활성 액션 추적, Update 루프)

#### 수정 파일 (2개)
- [x] `Program.cs` — `InputSystem.Update()` 호출 추가 (Input.Update() 직후)
- [x] `LiveCode/TestScript.cs` — InputSystem 사용 데모 (WASD 2DVector 컴포짓 + Space 버튼)

### 주요 설계 결정

1. **레거시 Input 재활용**: InputSystem이 기존 `UnityEngine.Input` 정적 상태를 읽음 (Silk.NET 이벤트 중복 등록 없음)
2. **Phase 전이 모델**: `Waiting → Started → Performed → Canceled`
   - Button: 누를 때 Started+Performed, 뗄 때 Canceled
   - Value: 매 프레임 값이 있으면 Performed 재호출
   - PassThrough: 입력이 있는 모든 프레임에 Performed
3. **컴포짓 바인딩**: `2DVector` (Up/Down/Left/Right → Vector2), `1DAxis` (Positive/Negative → float)
4. **경로 파싱**: `<Keyboard>/a-z`, `<Mouse>/leftButton` 등 Unity InputSystem 경로 형식 지원

### 프레임 업데이트 흐름
```
Program.OnUpdate()
  → Input.Update()           // 레거시 (기존)
  → InputSystem.Update()     // 신규: 모든 활성 InputAction 평가
  → EngineCore.Update()      // 게임 로직
```

### 사용 예시
```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class TestScript : MonoBehaviour
{
    private InputAction moveAction;
    private InputAction jumpAction;

    public override void Awake()
    {
        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        jumpAction = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
        jumpAction.performed += ctx => Debug.Log("[InputSystem] Jump!");

        moveAction.Enable();
        jumpAction.Enable();
    }

    public override void Update()
    {
        Vector2 move = moveAction.ReadValue<Vector2>();
        if (move.x != 0 || move.y != 0)
            Debug.Log($"[InputSystem] Move: {move}");
    }
}
```

### 테스트 결과
- ✅ `dotnet build` 성공 (경고 0개, 오류 0개)
- ✅ 레거시 `UnityEngine.Input`과 `UnityEngine.InputSystem` 공존

### 신규 파일 목록 (7개)
```
src/IronRose.Engine/UnityEngine/InputSystem/
├── InputActionType.cs     (~10줄)
├── InputActionPhase.cs    (~10줄)
├── InputBinding.cs        (~30줄)
├── InputControlPath.cs    (~170줄)
├── InputAction.cs         (~210줄)
├── InputActionMap.cs      (~45줄)
└── InputSystem.cs         (~25줄)
```

---

## Phase 3.5++: Unity 호환성 확장 ✅

**완료 날짜**: 2026-02-14

### 완료된 작업

#### 신규 파일 (6개)
- [x] `UnityEngine/Mathf.cs` — Sin, Cos, Lerp, Clamp, Clamp01, SmoothDamp, PingPong, Repeat, Approximately 등 ~40개 메서드
- [x] `UnityEngine/Random.cs` — Range(float/int), insideUnitSphere, onUnitSphere, insideUnitCircle, rotation, ColorHSV
- [x] `UnityEngine/Object.cs` — 기반 클래스: Destroy(deferred), DestroyImmediate, Instantiate(deep clone), FindObjectOfType/s, implicit bool
- [x] `UnityEngine/Attributes.cs` — SerializeField, HideInInspector, Header, Range, Tooltip, Space, RequireComponent 등
- [x] `UnityEngine/YieldInstruction.cs` — WaitForSeconds, WaitForEndOfFrame, WaitUntil, WaitWhile, CustomYieldInstruction
- [x] `UnityEngine/Coroutine.cs` — Coroutine 핸들 (중첩 코루틴 지원)

#### 기존 수정 (8개)
- [x] `Component.cs` — Object 상속, name→gameObject 위임, tag, GetComponentInChildren/InParent, GetComponentsInChildren/InParent
- [x] `GameObject.cs` — Object 상속, SetActive/activeSelf/activeInHierarchy, tag/layer/CompareTag, Find/FindWithTag/FindGameObjectsWithTag, AddComponent 자동 MonoBehaviour 등록
- [x] `MonoBehaviour.cs` — OnEnable/OnDisable, enabled setter 연동, StartCoroutine(IEnumerator/string)/StopCoroutine/StopAllCoroutines, Invoke/InvokeRepeating/CancelInvoke/IsInvoking
- [x] `Transform.cs` — parent/children 계층, SetParent(worldPositionStays), localPosition/localRotation/localScale, 월드↔로컬 좌표 자동 변환, lossyScale, LookAt, RotateAround, TransformPoint/InverseTransformPoint, Space enum
- [x] `SceneManager.cs` — GameObject 레지스트리, 코루틴 스케줄러(WaitForSeconds/중첩/CustomYield), Invoke 타이머, Deferred Destroy 큐, activeInHierarchy 체크, 중복 등록 방지
- [x] `Quaternion.cs` — Inverse, Normalize, normalized, Lerp/Slerp/SlerpUnclamped, RotateTowards, Angle, LookRotation, FromToRotation
- [x] `Vector3.cs` — MoveTowards, SmoothDamp, Angle/SignedAngle, Scale, Project/ProjectOnPlane, Reflect, ClampMagnitude, Min/Max, RotateTowards, LerpUnclamped, Normalize(), Set(), indexer
- [x] `Color.cs` — HSVToRGB, RGBToHSV

### 주요 설계 결정

1. **UnityEngine.Object 베이스**: Component와 GameObject가 Object를 상속. `implicit operator bool`로 Destroy된 오브젝트 null 체크 패턴 지원
2. **Deferred Destroy**: `Object.Destroy(go, delay)` → SceneManager 큐 → 프레임 끝 처리. 자식 재귀 파괴 + OnDisable/OnDestroy 호출 + 레지스트리 정리
3. **Transform 로컬/월드 분리**: 내부에 localPosition/localRotation 저장, position/rotation은 부모 체인 계산. SetParent(worldPositionStays) 지원
4. **코루틴 스케줄러**: SceneManager에서 중앙 관리. WaitForSeconds 타이머, 중첩 Coroutine/IEnumerator 자동 감지, CustomYieldInstruction 지원
5. **AddComponent 자동 등록**: MonoBehaviour를 AddComponent하면 SceneManager.RegisterBehaviour 자동 호출 (중복 방지 guard 포함)
6. **라이프사이클 확장**: Awake → OnEnable → Start → Update → Coroutines → LateUpdate → Destroy 처리 → frameCount++

### 테스트 결과
- ✅ `dotnet build` 성공 (경고 0개, 오류 0개)
- ✅ Demo 프로젝트 정상 실행 (60 FPS 안정)
- ✅ 기존 모든 기능 정상 동작 (렌더링, Input, InputSystem, 핫 리로드)

---

## 다음 단계: Phase 4

**목표**: 고급 렌더링 파이프라인

---

## 개발 메트릭

### 코드 통계
- **프로젝트 수**: 7개 (Engine, Demo, Contracts, Scripting, Rendering, AssetPipeline, Physics)
- **총 라인 수**: ~5400줄 (Phase 3.5++)
  - Engine/Program.cs: ~120줄
  - Engine/EngineCore.cs: ~250줄
  - Engine/RenderSystem.cs: ~180줄
  - Engine/UnityEngine/*.cs: ~3300줄 (27파일)
  - Engine/UnityEngine/InputSystem/*.cs: ~500줄 (7파일)
  - Rendering/GraphicsManager.cs: ~85줄
  - Rendering/ShaderCompiler.cs: ~50줄
  - Scripting/ScriptCompiler.cs: ~145줄
  - Scripting/ScriptDomain.cs: ~165줄
  - Scripting/StateManager.cs: ~50줄
  - Demo/*.cs: ~215줄
- **NuGet 패키지**: 18개

### 빌드 통계
- **마지막 빌드 시간**: ~1.8초
- **빌드 결과**: 성공 (경고 0개, 오류 0개)

### 실행 통계
- **윈도우**: 1280x720
- **그래픽 백엔드**: Vulkan
- **FPS**: 59-60 (안정적, Stopwatch 기반)
- **프레임타임**: 16-17ms (목표: 16.67ms)

---

## 변경 이력

### 2026-02-14 (Phase 3.5++ Unity 호환성 확장)
- **Phase 3.5++ 완료** ✅ (Unity 호환성 대폭 확장)
  - **신규 6파일**:
    - `Mathf.cs`: Sin, Cos, Lerp, Clamp, SmoothDamp, PingPong, Repeat, Approximately 등 ~40개 메서드
    - `Random.cs`: Range, insideUnitSphere, onUnitSphere, insideUnitCircle, rotation, ColorHSV
    - `Object.cs`: 기반 클래스 — Destroy(deferred), DestroyImmediate, Instantiate(deep clone), FindObjectOfType, implicit bool
    - `Attributes.cs`: SerializeField, Header, Range, Tooltip, RequireComponent 등 Unity 어트리뷰트
    - `YieldInstruction.cs`: WaitForSeconds, WaitForEndOfFrame, WaitUntil, WaitWhile, CustomYieldInstruction
    - `Coroutine.cs`: 코루틴 핸들 클래스
  - **기존 수정 8파일**:
    - `Component.cs`: Object 상속, GetComponentInChildren/InParent
    - `GameObject.cs`: Object 상속, SetActive/activeSelf/activeInHierarchy, Find/FindWithTag, tag/layer
    - `MonoBehaviour.cs`: OnEnable/OnDisable, StartCoroutine/StopCoroutine, Invoke/InvokeRepeating/CancelInvoke
    - `Transform.cs`: parent/children 계층, localPosition/localRotation, 월드↔로컬 변환, LookAt, RotateAround
    - `SceneManager.cs`: GO 레지스트리, 코루틴 스케줄러, Invoke 타이머, Deferred Destroy 큐
    - `Quaternion.cs`: Inverse, Lerp/Slerp, LookRotation, RotateTowards, FromToRotation, Angle
    - `Vector3.cs`: MoveTowards, SmoothDamp, Angle/SignedAngle, Scale, Project, Reflect, ClampMagnitude
    - `Color.cs`: HSVToRGB, RGBToHSV
  - UnityEngine 전체: 27파일 ~3300줄 + InputSystem 7파일 ~500줄
  - Demo 프로젝트 정상 실행 (60 FPS)
- **Debug 토글 & ImageSharp 취약성 수정**
  - `Debug.Enabled` 프로퍼티 추가 (로그 출력 ON/OFF 토글)
  - `ScreenCaptureEnabled` ON/OFF 토글 테스트 통과
  - SixLabors.ImageSharp 1.0.4 → 3.1.12 업그레이드 (보안 취약성 해결)
  - ImageSharp 3.x API 마이그레이션: `GetPixelRowSpan` → `ProcessPixelRows`
  - NU 취약성 경고 28개 → 0개
- **Phase 3 완료** ✅ (Unity Architecture)
  - UnityEngine 네임스페이스 구현 (Vector3, Vector2, Quaternion, Color, Time, Debug)
  - GameObject/Component/Transform/MonoBehaviour 아키텍처
  - SceneManager 라이프사이클 (Awake → Start → Update → LateUpdate → OnDestroy)
  - ScriptDomain TypeFilter + ALC Resolving fallback
  - EngineCore에 RegisterMonoBehaviours() 통합
  - LiveCode 스크립트 MonoBehaviour 패턴으로 전환
  - "그래픽스 프레임워크"에서 "게임 엔진"으로 전환 달성
- **Phase 3.5 완료** ✅ (입력 시스템 + Silk.NET 마이그레이션)
  - **Veldrid.Sdl2 → Silk.NET.Windowing + Silk.NET.Input 전면 교체**
    - Program.cs: Silk.NET 이벤트 루프 (Load/Update/Render/Closing 콜백)
    - GraphicsManager: 네이티브 핸들(X11/Wayland/Win32) → Veldrid SwapchainSource
    - EngineCore: IWindow 인터페이스로 전환
    - Veldrid.StartupUtilities, Silk.NET.SDL 패키지 제거
  - **Unity 스타일 Input 정적 클래스** (UnityEngine.Input)
    - GetKey/GetKeyDown/GetKeyUp, GetMouseButton/Down/Up
    - mousePosition, mouseScrollDelta
    - GetAxis("Horizontal"/"Vertical"/"Mouse X"/"Mouse Y")
    - anyKey, anyKeyDown
  - **KeyCode enum** + Silk.NET.Input.Key 매핑 (A-Z, 0-9, F1-F12, 화살표, 수정자, 키패드)
  - LiveCode TestScript에 WASD/마우스 입력 데모 추가
- **Phase 3.5+ 완료** ✅ (Unity InputSystem - 액션 기반 입력)
  - **UnityEngine.InputSystem 네임스페이스** (7개 신규 파일)
    - InputAction: 콜백 기반 입력 (started/performed/canceled)
    - InputActionType: Button, Value, PassThrough
    - InputActionPhase: Disabled → Waiting → Started → Performed → Canceled
    - InputBinding + CompositeBinder: 2DVector/1DAxis 컴포짓 바인딩
    - InputControlPath: `<Keyboard>/space` 경로 파싱 → 레거시 Input 재활용
    - InputActionMap: 액션 그룹 관리
    - InputSystem: 정적 매니저 (Update 루프 연동)
  - Program.cs에 `InputSystem.Update()` 추가 (Input.Update() 직후)
  - TestScript에 InputSystem 데모 추가 (WASD 2DVector + Space 버튼 콜백)

### 2026-02-13
- **Phase 0 완료** ✅
  - 프로젝트 초기 설정 완료
  - Progress.md, Claude.md, .gitignore 생성
  - IronRose 테마 색상 정의
- **Phase 1 완료** ✅
  - Bootstrapper 기본 구조 완성 (~85줄)
  - GraphicsManager 구현 (Veldrid.Sdl2 사용)
  - Vulkan 초기화 성공
  - 첫 윈도우 오픈 및 렌더링 성공!
  - Stopwatch 기반 타이밍 시스템 완성
  - FPS 카운터 (59-60 FPS 안정적 유지)
- **Phase 2 완료** ✅
  - Roslyn 컴파일러 래퍼 구현
  - AssemblyLoadContext 핫 스왑 시스템
  - ScriptDomain (동적 로드/언로드/GC)
  - TestScript.cs 컴파일 및 실행 성공!
  - 코드 변경 시 새 버전 자동 로드
- **Phase 2A 완료** ✅ ("Everything is Hot-Reloadable")
  - IronRose.Contracts 인터페이스 분리
  - EngineCore + EngineLoader 구현
  - Shadow Copy (파일 잠금 방지)
  - EngineWatcher (자동 감지 + 빌드)
  - **bin-hot 전략 성공!**
  - **Engine.dll 런타임 핫 리로드 작동!**
