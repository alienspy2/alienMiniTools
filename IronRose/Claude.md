# IronRose 프로젝트 개발 가이드라인

## 디자인 가이드라인

### 메인 테마 색상

**IronRose Theme Color**: 금속의 백장미 (Metallic White Rose)

```csharp
// RGB: (230, 220, 210) - 은은한 베이지 톤
// 정규화: (0.902, 0.863, 0.824)
// Hex: #E6DCD2

// Veldrid 사용 예시
var ironRoseColor = new RgbaFloat(0.902f, 0.863f, 0.824f, 1.0f);

// Unity 스타일 Color32
var ironRoseColor32 = new Color32(230, 220, 210, 255);
```

**색상 설명**:
- 백장미의 우아하고 은은한 흰색
- 금속의 차가운 광택감
- RGB로 표현 시 따뜻한 베이지 톤
- 배경, UI 기본 색상, 엔진 로고 등에 사용

**보조 색상** (추후 정의):
- 어둡게: 회색 톤 (금속 그림자)
- 밝게: 순백색 (하이라이트)
- 강조: 장미의 붉은색 (액센트)

---

## 코딩 스타일

### 크로스 플랫폼
- **파일 경로**: 항상 `Path.Combine()`을 사용. `"foo/bar"` 또는 `"foo\\bar"` 금지.
- **줄 끝**: LF 기본 (`.editorconfig`, `.gitattributes` 참조)

### 인코딩
- **C# 소스 파일(.cs)**: UTF-8 with BOM 사용
- **.editorconfig**에 명시되어 자동 적용됨

### 네이밍 컨벤션
- Unity와 유사한 C# 표준 컨벤션 사용
- 클래스/메서드: PascalCase
- 필드/변수: camelCase
- 상수: UPPER_CASE

---

## 개발 워크플로우

### 1. 단계별 구현 및 검증
매 단계 구현 후 반드시 다음 순서로 테스트:

```bash
# 1. 빌드
dotnet build

# 2. 실행 파일 테스트
dotnet run --project src/IronRose.Demo

# 또는 직접 실행
./src/IronRose.Demo/bin/Debug/net10.0/IronRose.Demo
```

**중요**: 코드 레벨 유닛 테스트만으로는 부족합니다. 반드시 빌드 후 실제 실행 파일을 실행하여 통합 테스트를 수행해야 합니다.

### 2. 로깅 전략
모든 주요 동작에 대해 상세한 로그를 남겨야 합니다:

```csharp
// 예시
Console.WriteLine($"[Engine] Initializing scene: {sceneName}");
Console.WriteLine($"[Renderer] Creating window: {width}x{height}");
Console.WriteLine($"[Physics] Timestep: {deltaTime:F4}s");
```

**로그 카테고리**:
- `[IronRose]`: 엔진 시작/종료
- `[Engine]`: 게임 오브젝트, 씬, 컴포넌트 생명주기
- `[Renderer]`: 렌더링 파이프라인, 그래픽스 API 호출
- `[Physics]`: 물리 시뮬레이션, 충돌 감지
- `[Scripting]`: 스크립트 컴파일, 핫 리로드
- `[Asset]`: 에셋 로딩, 임포팅

### 3. 진행 상황 추적

매 작업 단계 완료 후 [Progress.md](Progress.md) 파일을 업데이트해야 합니다:

**업데이트 시점**:
- Phase 완료 시
- 주요 기능 구현 완료 시
- 중요한 마일스톤 달성 시

**업데이트 내용**:
```markdown
## Phase X: [제목] ✅

**완료 날짜**: YYYY-MM-DD
**소요 시간**: X시간/일

### 완료된 작업
- [x] 작업 항목 1
- [x] 작업 항목 2

### 주요 결정 사항
- 결정 내용 및 이유

### 알려진 이슈
- 발견된 문제점 및 해결 계획
```

**다음 단계 업데이트**:
```markdown
## 다음 단계: Phase Y

**목표**: [목표 설명]

### 예정된 작업
- [ ] 작업 항목 1
- [ ] 작업 항목 2
```

**전체 진행도 체크박스 업데이트**:
```markdown
- [x] Phase X: 완료된 단계 ✅
- [ ] Phase Y: 현재 작업 중 🚧
```

### 4. 핫 리로드 워크플로우

#### 스크립트 핫 리로드 (Phase 2)
```
1. LiveCode/*.cs 수정
2. Roslyn 런타임 컴파일
3. 즉시 로드 및 실행
```

### 5. 스크립트 편입 (`/digest`)

엔진 실행이 종료된 후, 핫 리로드로 검증이 완료된 LiveCode 스크립트를 `/digest` 커맨드로 `src/IronRose.Demo/` 프로젝트에 편입합니다.
- LiveCode에서 테스트 완료된 `.cs` 파일을 Demo 프로젝트로 이동
- LiveCode 디렉토리는 항상 실험/개발 중인 스크립트만 유지

### 6. AI 자동화를 위한 제어 인터페이스

엔진은 JSON 기반 명령 파일을 통해 자동화 가능해야 합니다:

#### 명령 파일 예시 (`.claude/test_commands.json`)
```json
{
  "commands": [
    {"type": "scene.load", "scene": "TestScene"},
    {"type": "input.key_press", "key": "Space"},
    {"type": "wait", "duration": 1.0},
    {"type": "screenshot", "path": "test_output.png"},
    {"type": "quit"}
  ]
}
```

#### 구현 요구사항
- 엔진은 시작 시 명령 파일 존재 여부를 확인
- 명령 파일이 있으면 자동으로 실행하고 결과를 로그로 출력
- 각 명령 실행 후 성공/실패 상태를 명확히 기록
- 스크린샷, 로그 파일 등을 지정된 경로에 저장

**목적**: AI가 빌드 → 명령 파일 생성 → 실행 → 결과 확인의 전체 프로세스를 자동화할 수 있도록 함.

---

## 프로젝트 디렉토리 구조

```
IronRose/
├── .claude/
│   ├── commands/                    # Claude 커스텀 커맨드 (digest.md 등)
│   └── test_outputs/                # 테스트 결과물 (스크린샷 등)
├── Assets/                          # 게임 에셋
│   └── Textures/                    # IBL 큐브맵, 텍스처 + .rose 메타데이터
├── Shaders/                         # GLSL 셰이더 (14파일)
│   ├── vertex.glsl, fragment.glsl   # Forward 렌더링
│   ├── deferred_geometry.*          # G-Buffer Geometry Pass
│   ├── deferred_lighting.*          # PBR Lighting Pass
│   ├── skybox.*                     # 스카이박스/IBL
│   ├── bloom_*.frag, gaussian_blur.frag  # Post-Processing
│   ├── tonemap*.frag               # ACES Tone Mapping
│   └── fullscreen.vert              # Fullscreen triangle
├── src/
│   ├── IronRose.Engine/             # 엔진 코어 (EXE 진입점)
│   │   ├── EngineCore.cs            # 엔진 업데이트/렌더 오케스트레이션
│   │   ├── RenderSystem.cs          # Forward/Deferred 하이브리드 렌더링
│   │   ├── RoseEngine/              # Unity 호환 API (59파일, ~5500줄)
│   │   │   ├── 수학: Vector3, Vector2, Vector4, Quaternion, Color, Matrix4x4, Mathf
│   │   │   ├── 코어: GameObject, Component, Transform, MonoBehaviour, SceneManager, Object
│   │   │   ├── 렌더링: Camera, Light, Mesh, MeshFilter, MeshRenderer, Material, Texture2D, Shader
│   │   │   ├── 입력: Input(레거시), InputSystem/(액션 기반, 7파일)
│   │   │   ├── 물리: Rigidbody, Rigidbody2D, Collider(3D+2D), Collision, ForceMode
│   │   │   ├── 2D: Sprite, SpriteRenderer, Font, TextRenderer, Rect
│   │   │   ├── IBL: Cubemap, RenderSettings
│   │   │   └── 유틸: Random, Debug, Time, Screen, Application, Resources, Attributes, Coroutine
│   │   ├── AssetPipeline/           # 에셋 임포트 (7파일)
│   │   │   ├── AssetDatabase.cs, MeshImporter.cs, TextureImporter.cs
│   │   │   ├── PrefabImporter.cs, GlbTextureExtractor.cs
│   │   │   └── RoseMetadata.cs, UnityYamlParser.cs
│   │   └── Physics/
│   │       └── PhysicsManager.cs    # PhysicsWorld3D/2D 통합
│   ├── IronRose.Demo/              # 데모 실행 파일
│   │   ├── FrozenCode/              # 안정된 데모 씬 (6개)
│   │   └── LiveCode/                # 핫 리로드 실험 스크립트
│   ├── IronRose.Rendering/         # 렌더링 파이프라인
│   │   ├── GBuffer.cs, GraphicsManager.cs, ShaderCompiler.cs
│   │   └── PostProcessing/          # BloomEffect, TonemapEffect, PostProcessStack
│   ├── IronRose.Physics/           # 물리 래퍼 (Engine 미참조)
│   │   ├── PhysicsWorld3D.cs        # BepuPhysics v2.4.0
│   │   └── PhysicsWorld2D.cs        # Aether.Physics2D v2.2.0
│   ├── IronRose.Scripting/         # Roslyn 런타임 컴파일
│   └── IronRose.Contracts/         # 플러그인 API 계약
├── Screenshots/                     # 테스트 스크린샷
├── reference/                       # 참고 구현 (Unity IBL 등)
└── docs/                            # 문서 (18개 마크다운 파일)
```

---

## 체크리스트

### 매 작업 단계 완료 시
- [ ] [Progress.md](Progress.md) 업데이트
  - [ ] 완료된 작업 체크
  - [ ] 완료 날짜 기록
  - [ ] 주요 결정 사항 문서화
  - [ ] 다음 단계 정의
- [ ] 빌드 성공 (`dotnet build`)
- [ ] 실행 파일 테스트 완료
- [ ] 주요 동작에 로그 추가됨

### 매 커밋/PR 전 확인
- [ ] UTF-8 BOM 인코딩 확인 (C# 파일)
- [ ] 명명 규칙 준수
- [ ] 불필요한 파일 제외 (.gitignore 확인)
- [ ] 코드 리뷰 준비 완료
