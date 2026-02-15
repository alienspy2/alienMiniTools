# Phase 10: 문서화 및 샘플

## 목표
사용자가 IronRose를 쉽게 시작하고 학습할 수 있도록 문서와 샘플을 제공합니다.

---

## 작업 항목

### 10.1 API 문서

**XML 주석 작성:**
```csharp
namespace RoseEngine
{
    /// <summary>
    /// 3D 공간의 벡터를 표현합니다.
    /// </summary>
    public struct Vector3
    {
        /// <summary>
        /// X 좌표
        /// </summary>
        public float x;

        /// <summary>
        /// Y 좌표
        /// </summary>
        public float y;

        /// <summary>
        /// Z 좌표
        /// </summary>
        public float z;

        /// <summary>
        /// 새로운 Vector3를 생성합니다.
        /// </summary>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <param name="z">Z 좌표</param>
        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        /// <summary>
        /// 벡터의 크기를 반환합니다.
        /// </summary>
        public float magnitude => MathF.Sqrt(x * x + y * y + z * z);

        /// <summary>
        /// 정규화된 벡터를 반환합니다 (길이가 1인 벡터).
        /// </summary>
        public Vector3 normalized => this / magnitude;
    }
}
```

**DocFX로 문서 생성:**
```json
// docfx.json
{
  "metadata": [
    {
      "src": [
        {
          "files": [ "src/**/*.csproj" ],
          "src": ".."
        }
      ],
      "dest": "api"
    }
  ],
  "build": {
    "content": [
      {
        "files": [ "api/**.yml", "api/index.md" ]
      },
      {
        "files": [ "docs/**.md", "toc.yml", "index.md" ]
      }
    ],
    "dest": "_site"
  }
}
```

**빌드 스크립트:**
```bash
# build-docs.sh
docfx metadata
docfx build
docfx serve _site
```

---

### 10.2 Unity 마이그레이션 가이드

**docs/UnityMigration.md:**
```markdown
# Unity에서 IronRose로 마이그레이션

## 지원되는 기능

| Unity 기능 | IronRose 지원 | 비고 |
|-----------|--------------|------|
| GameObject | ✅ 완전 지원 | |
| MonoBehaviour | ✅ 완전 지원 | Awake, Start, Update, LateUpdate |
| Transform | ✅ 완전 지원 | Position, Rotation, Scale |
| Vector3, Quaternion | ✅ 완전 지원 | |
| Time | ✅ 완전 지원 | deltaTime, time |
| Debug.Log | ✅ 완전 지원 | |
| Rigidbody | ✅ 지원 | BepuPhysics 기반 |
| MeshRenderer | ✅ 지원 | Veldrid 렌더링 |
| Animator | ❌ 미지원 | 향후 추가 예정 |
| UI (Canvas) | ❌ 미지원 | 향후 추가 예정 |

## 코드 변환 예시

### Unity 코드
```csharp
using RoseEngine;

public class PlayerController : MonoBehaviour
{
    public float speed = 5f;

    void Update()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        transform.Translate(new Vector3(h, 0, v) * speed * Time.deltaTime);
    }
}
```

### IronRose 코드 (동일!)
```csharp
using RoseEngine;

public class PlayerController : MonoBehaviour
{
    public float speed = 5f;

    void Update()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        transform.Translate(new Vector3(h, 0, v) * speed * Time.deltaTime);
    }
}
```

**InputSystem 방식도 지원:**
```csharp
using RoseEngine;
using RoseEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    private InputAction moveAction;

    public override void Awake()
    {
        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAction.Enable();
    }

    public override void Update()
    {
        Vector2 move = moveAction.ReadValue<Vector2>();
        transform.Translate(new Vector3(move.x, 0, move.y) * 5f * Time.deltaTime);
    }
}
```

## 알려진 차이점

1. ~~**Input 시스템**: 아직 미구현.~~ → ✅ 레거시 Input + InputSystem 모두 구현 완료
2. **Resources.Load()**: AssetDatabase.Load<T>()로 대체.
3. **Coroutines**: 아직 미지원. async/await 사용 권장.
```

---

### 10.3 성능 베스트 프랙티스

**docs/BestPractices.md:**
```markdown
# IronRose 성능 베스트 프랙티스

## 1. GameObject.GetComponent() 캐싱

❌ **나쁜 예:**
```csharp
void Update()
{
    GetComponent<Rigidbody>().AddForce(Vector3.up);
}
```

✅ **좋은 예:**
```csharp
private Rigidbody _rigidbody;

void Start()
{
    _rigidbody = GetComponent<Rigidbody>();
}

void Update()
{
    _rigidbody.AddForce(Vector3.up);
}
```

## 2. 오브젝트 풀링

대량의 오브젝트를 생성/파괴하지 말고 재사용하세요.

```csharp
public class BulletPool
{
    private Queue<GameObject> _pool = new();

    public GameObject Get()
    {
        if (_pool.Count > 0)
            return _pool.Dequeue();

        return new GameObject("Bullet");
    }

    public void Return(GameObject bullet)
    {
        _pool.Enqueue(bullet);
    }
}
```

## 3. 핫 리로드 최적화

- 자주 변경하지 않는 코드는 별도 어셈블리로 분리
- IHotReloadable 구현으로 상태 보존
```

---

### 10.4 샘플 프로젝트

**samples/01_HelloWorld/HelloWorld.cs:**
```csharp
using RoseEngine;

public class HelloWorld : MonoBehaviour
{
    void Start()
    {
        Debug.Log("Hello, IronRose!");
    }

    void Update()
    {
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"Running for {Time.time:F1} seconds");
        }
    }
}
```

**samples/02_RotatingCube/RotatingCube.cs:**
```csharp
using RoseEngine;

public class RotatingCube : MonoBehaviour
{
    public float rotationSpeed = 45f;

    void Start()
    {
        // 큐브 생성
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = new Vector3(0, 0, 5);

        var renderer = cube.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material.color = new Color(1.0f, 0.5f, 0.2f);
        }
    }

    void Update()
    {
        transform.Rotate(0, rotationSpeed * Time.deltaTime, 0);
    }
}
```

**samples/03_AIPlayground/README.md:**
```markdown
# AI Playground

AI로 게임 오브젝트를 실시간으로 생성하는 샘플입니다.

## 사용 방법

1. 환경 변수 설정:
   ```
   set ANTHROPIC_API_KEY=your_api_key_here
   ```

2. 엔진 실행

3. 콘솔에 명령 입력:
   ```
   /ai 빨간색 구를 만들고 위아래로 움직이게 해줘
   /ai 카메라를 천천히 회전시켜줘
   ```

## 예시 프롬프트

- "초록색 큐브 10개를 랜덤한 위치에 생성해줘"
- "태양처럼 빛나는 구를 만들어줘"
- "플레이어 컨트롤러를 만들어줘"
```

**samples/04_UnitySceneImport/README.md:**
```markdown
# Unity Scene Import

Unity에서 만든 씬을 IronRose로 가져오는 예제입니다.

## 단계

1. Unity에서 씬 생성
2. 씬 파일 (.unity) 복사
3. IronRose에서 로드:
   ```csharp
   var scene = AssetDatabase.Load<Scene>("MyScene.unity");
   ```
```

---

### 10.5 비디오 데모

**YouTube 데모 영상 촬영 계획:**

**시나리오:**
1. **인트로 (30초)**
   - "IronRose: AI-Native Game Engine"
   - 프로젝트 소개

2. **기본 기능 (1분)**
   - 윈도우 열기
   - 큐브 렌더링
   - 핫 리로딩 데모

3. **AI 통합 (2분)**
   - 프롬프트 입력
   - 실시간 코드 생성
   - 즉시 실행

4. **Unity 호환성 (1분)**
   - Unity 스크립트 실행
   - Prefab 로드

5. **아웃트로 (30초)**
   - GitHub 링크
   - Discord 커뮤니티

**촬영 도구:**
- OBS Studio (화면 녹화)
- DaVinci Resolve (편집)

---

## 검증 기준

✅ API 문서가 DocFX로 생성됨
✅ 4개의 샘플 프로젝트가 모두 동작함
✅ Unity 마이그레이션 가이드 완성
✅ YouTube 데모 영상 업로드 (5분)

---

## 예상 소요 시간
**5-6일**

---

## 다음 단계
→ [Phase 11: 커뮤니티 & 오픈소스](Phase11_Community.md)
