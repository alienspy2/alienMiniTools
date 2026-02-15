# **IronRose: AI-Native .NET 10 게임 엔진 아키텍처 설계 보고서**

> **"Iron for Strength, Rose for Beauty"**
> 현재 상태: Phase 7 완료 (2026-02-15) - Deferred PBR + IBL + Physics + Hot Reload

## **1\. 프로젝트 비전: The "Prompt-to-Play" Engine**

본 프로젝트는 기존의 게임 엔진(Unity, Unreal)이 가진 무거운 에디터 중심의 워크플로우를 탈피하고, **AI(LLM)가 코드를 생성하고 엔진이 이를 즉시 컴파일하여 실행하는** 새로운 패러다임을 제시합니다. .NET 10의 최신 기술을 활용하여 유니티의 방대한 API 생태계를 흡수하되, 내부적으로는 가볍고 빠른 최신 렌더링/메모리 아키텍처를 지향합니다.

## **2\. 엔진 이름: IronRose**

**IronRose** — "Iron for Strength, Rose for Beauty"

금속(Iron)의 강건한 성능과 장미(Rose)의 아름다운 렌더링을 결합한 이름.
`RoseEngine` 네임스페이스로 Unity API 호환성을 제공합니다.

## ---

**3\. 핵심 아키텍처: 기술 스택 및 구조**

### **3.1 기반 기술 (Foundation) — 전부 구현 완료**

| 레이어 | 기술 | 용도 | 상태 |
|--------|------|------|------|
| **Runtime** | .NET 10.0 | JIT + AOT 가능 런타임 | ✅ |
| **Windowing** | Silk.NET.Windowing (GLFW) | 크로스 플랫폼 윈도우 | ✅ |
| **Input** | Silk.NET.Input | 키보드/마우스/게임패드 | ✅ |
| **Graphics** | Veldrid (Vulkan 백엔드) | 저수준 GPU 추상화 | ✅ |
| **Shader** | Veldrid.SPIRV | GLSL 450 → Vulkan SPIR-V | ✅ |
| **Scripting** | Roslyn (Microsoft.CodeAnalysis) | 런타임 C# 컴파일 | ✅ |
| **Asset Import** | AssimpNet | FBX/GLB/OBJ 3D 모델 로드 | ✅ |
| **Image** | SixLabors.ImageSharp 3.1.12 | PNG/JPG 텍스처 로딩 | ✅ |
| **YAML** | YamlDotNet | Unity Scene/Prefab 파싱 | ✅ |
| **Physics 3D** | BepuPhysics v2.4.0 | 3D 리지드바디 물리 | ✅ |
| **Physics 2D** | Aether.Physics2D v2.2.0 | 2D 리지드바디 물리 | ✅ |
| **Serialization** | Tomlyn | TOML 상태 직렬화 | ✅ |

## ---

**4\. 핵심 기능 구현 계획 (Deep Dive)**

### **4.1 AI 친화적 런타임 코딩 & 핫 리로딩 (The "Heart")**

AI가 생성한 코드를 게임을 끄지 않고 즉시 적용하려면 \*\*AssemblyLoadContext (ALC)\*\*를 활용한 핫 스왑 구조가 필수적입니다.3

**구현 메커니즘: 플러그인 기반 핫 리로드**

> **참고**: 초기 설계는 "Everything is Hot-Reloadable" (엔진 전체 핫 리로드)였으나,
> 복잡도와 안정성 문제로 플러그인 기반 핫 리로드로 전략 변경됨.
> 상세: [전략변경.md](docs/전략변경.md)

1. **IronRose.Engine (EXE, 안정적 기반):** 진입점 + 엔진 코어
   * SDL/Veldrid 초기화, 메인 루프
   * GameObject, Component, Transform
   * 렌더링/물리 시스템
   * 플러그인 매니저

2. **Plugin DLLs (ALC 핫 리로드):** 게임 로직 및 확장 기능
   * ALC(AssemblyLoadContext)로 격리/핫 리로드
   * 엔진 API(IEngine, EnginePlugin)를 통해 확장

3. **LiveCode (Roslyn 핫 리로드):** 빠른 프로토타입
   * *.cs 파일을 Roslyn으로 런타임 컴파일
   * 플러그인 API 사용 가능

4. **AI Digest:** 검증된 플러그인 코드를 엔진에 통합
   * Claude Code가 플러그인 코드를 분석/변환
   * 엔진 코드로 병합 + 테스트 작성

**장점:**
* 엔진 코어는 항상 안정적
* 플러그인 예외 시 해당 플러그인만 해제
* 빠른 반복 개발 (작은 DLL 핫 리로드)

**안전성:**
* 엔진은 재시작 없이 안정 유지
* 플러그인 크래시 시 try-catch로 격리
* AI Digest로 검증된 코드만 엔진에 통합

### **4.2 Unity 아키텍처 구현 (Direct Implementation)**

AI(LLM)는 인터넷상의 방대한 유니티 코드로 학습되어 있습니다. 따라서 **"using RoseEngine;"** 스타일의 코드를 그대로 실행할 수 있게 하는 것이 핵심입니다.

**Unity 아키텍처 직접 구현:**

* **단순성 우선:** Shim(껍데기) 레이어나 ECS 변환 없이 Unity의 GameObject/Component 패턴을 직접 구현합니다.
* **직관적 구조:**
  * GameObject는 실제 게임 오브젝트를 표현하는 클래스입니다.
  * Component는 GameObject에 첨부되는 기능 단위입니다.
  * MonoBehaviour.Update()는 매 프레임 SceneManager가 순회하며 직접 호출합니다.7
* **장점:**
  * 구현이 간단하고 이해하기 쉽습니다.
  * 디버깅이 직관적입니다.
  * AI가 생성한 Unity 코드가 그대로 동작합니다.
* **성능:** 초기에는 순수 OOP로 구현하며, 병목이 실제로 발생하면 해당 부분만 선택적으로 최적화합니다.

### **4.3 유니티 에셋 호환성 (Import Pipeline)**

유니티의 .unity (Scene), .prefab, .meta 파일은 YAML 포맷입니다. 이를 파싱하여 엔진의 네이티브 객체로 변환합니다.

* **YAML 파서:** **VYaml** 또는 **YamlDotNet**을 사용하여 유니티 특유의 YAML 태그(\!u\!)를 처리합니다.9  
* **GUID 매핑:** 유니티의 .meta 파일에 있는 GUID를 읽어, 엔진 내부의 AssetID와 매핑 테이블을 구축합니다. 이를 통해 스크립트나 씬에서 깨진 참조 없이 에셋을 로드할 수 있습니다.\[14\]  
* **Mesh/Texture:** .fbx나 .png는 **AssimpNet**과 **StbImageSharp**을 통해 Veldrid 리소스로 변환합니다.

## ---

**5\. 렌더링 파이프라인: Forward/Deferred 하이브리드 + PBR** ✅ 구현 완료

Forward(Sprite, Text, 투명)와 Deferred(불투명 3D 메시)를 결합한 하이브리드 렌더링 파이프라인.

### **5.1 G-Buffer 설계 (구현 완료)**

| Render Target | 포맷 | 채널 데이터 |
| :---- | :---- | :---- |
| **RT0 (Albedo)** | R8G8B8A8_UNorm | RGB: Base Color, A: Alpha |
| **RT1 (Normal)** | R16G16B16A16_Float | RGB: World Normal [-1,1], A: Roughness |
| **RT2 (Material)** | R8G8B8A8_UNorm | R: Metallic, G: Occlusion, B: Emission intensity |
| **RT3 (WorldPos)** | R16G16B16A16_Float | RGB: World Position, A: 1.0 (geometry marker) |
| **Depth** | D32_Float_S8_UInt | Hardware Depth |

> RT1은 R16G16B16A16_Float로 [-1,1] 노멀 정밀도 보존 (R8 인코딩의 banding 방지).
> RT3에 World Position 직접 기록 (depth 복원 대신 — 정밀도 + 안정성 우수).

### **5.2 렌더링 패스 (구현 완료)**

```
1. Geometry Pass    → G-Buffer에 불투명 3D 메시 기록 (4 MRT + depth)
2. Lighting Pass    → G-Buffer → HDR 텍스처 (Cook-Torrance PBR + IBL)
3. Skybox Pass      → 큐브맵 기반 스카이박스 렌더링
4. Forward Pass     → HDR 텍스처에 Sprite/Text/Wireframe 추가
5. Post-Processing  → Bloom (threshold + Gaussian blur) + ACES Tone Mapping → Swapchain
```

**PBR BRDF**: Cook-Torrance (GGX Distribution + Schlick Fresnel + Smith Geometry)
**IBL**: 큐브맵 기반 Split-sum approximation + 디퓨즈 irradiance

## ---

**6\. 리소스 관리: Reference Counting**

C\#의 GC에만 의존하면 GPU 메모리 해제 시점이 불명확하므로, 명시적인 참조 카운팅을 도입합니다.

**RefCounted 패턴:**

* 모든 GPU 리소스(Texture, Mesh)는 RefCounted\<T\> 래퍼로 감쌉니다.  
* **Unity 호환성:** 유니티의 Resources.Load()나 Destroy() 동작을 흉내 낼 때, 내부적으로는 Retain()과 Release()를 호출합니다.  
* 참조 카운트가 0이 되면 즉시 Veldrid.Resource.Dispose()를 호출하여 VRAM을 확보합니다.\[16\]

## ---

**7\. 개발 이력 및 향후 로드맵**

### 완료된 단계 (2026-02-13 ~ 2026-02-15)
1. ✅ **Phase 0-2**: 프로젝트 구조 + Vulkan 윈도우 + Roslyn 핫 리로딩 + Engine 핫 리로드
2. ✅ **Phase 3**: Unity Architecture (GameObject, Component, MonoBehaviour, InputSystem) + 호환성 확장 (59개 컴포넌트)
3. ✅ **Phase 4**: 3D Forward Rendering (Mesh, Camera, Light, Texture2D, Primitives)
4. ✅ **Phase 5**: 에셋 임포터 (AssimpNet, ImageSharp, YAML, SpriteRenderer, TextRenderer)
5. ✅ **Phase 6**: 물리 엔진 (BepuPhysics 3D + Aether.Physics2D, FixedUpdate 50Hz)
6. ✅ **Phase 7**: Deferred PBR (G-Buffer, Cook-Torrance, IBL, Bloom, ACES Tone Mapping)

### 다음 단계
7. 🔲 **Phase 8 (AI Integration):** LLM API 연동, 런타임 코드 생성, 샌드박싱
8. 🔲 **Phase 9 (Optimization):** GPU 리소스 관리, 프로파일링, GC 압력 최적화
9. 🔲 **Phase 10 (Documentation):** API 문서, 샘플 프로젝트, 비디오 데모
10. 🔲 **Phase 11 (Community):** GitHub 공개, NuGet, Discord

### 코드 통계
- **~11,255줄** C# 소스 + **~921줄** GLSL 셰이더
- **59개** RoseEngine 컴포넌트 (Unity API ~80% 호환)
- **14개** 셰이더 파일 (Forward + Deferred + Post-Processing)
- **7개** 데모 씬 (FrozenCode)

#### **참고 자료**

1. Vulkan Backend \- Veldrid, 2월 13, 2026에 액세스, [https://veldrid.dev/articles/implementation/vulkan.html](https://veldrid.dev/articles/implementation/vulkan.html)  
2. Veldrid (3D Graphics Library) Implementation Overview : r/csharp \- Reddit, 2월 13, 2026에 액세스, [https://www.reddit.com/r/csharp/comments/7tb1i2/veldrid\_3d\_graphics\_library\_implementation/](https://www.reddit.com/r/csharp/comments/7tb1i2/veldrid_3d_graphics_library_implementation/)  
3. C\# Scripting Engine Part 7 – Hot Reloading • Kah Wei, Tng, 2월 13, 2026에 액세스, [https://kahwei.dev/2023/08/07/c-scripting-engine-part-7-hot-reloading/](https://kahwei.dev/2023/08/07/c-scripting-engine-part-7-hot-reloading/)  
4. API proposal: ReferenceCountedDisposable  
5. How Rider Hot Reload Works Under the Hood | The .NET Tools Blog, 2월 13, 2026에 액세스, [https://blog.jetbrains.com/dotnet/2021/12/02/how-rider-hot-reload-works-under-the-hood/](https://blog.jetbrains.com/dotnet/2021/12/02/how-rider-hot-reload-works-under-the-hood/)  
6. Self-compiled Roslyn build performance: Not as fast as originally shipped Roslyn version, 2월 13, 2026에 액세스, [https://stackoverflow.com/questions/34853273/self-compiled-roslyn-build-performance-not-as-fast-as-originally-shipped-roslyn](https://stackoverflow.com/questions/34853273/self-compiled-roslyn-build-performance-not-as-fast-as-originally-shipped-roslyn)  
7. Scripting API: MonoBehaviour \- Unity \- Manual, 2월 13, 2026에 액세스, [https://docs.unity3d.com/6000.3/Documentation/ScriptReference/MonoBehaviour.html](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/MonoBehaviour.html)  
8. MonoBehaviour \- Unity \- Manual, 2월 13, 2026에 액세스, [https://docs.unity3d.com/6000.3/Documentation/Manual/class-MonoBehaviour.html](https://docs.unity3d.com/6000.3/Documentation/Manual/class-MonoBehaviour.html)  
9. hadashiA/VYaml: The extra fast, low memory footprint ... \- GitHub, 2월 13, 2026에 액세스, [https://github.com/hadashiA/VYaml](https://github.com/hadashiA/VYaml)  
10. socialpoint-labs/unity-yaml-parser: Python3 library to manipulate Unity serialized files from outside the Unity Editor. \- GitHub, 2월 13, 2026에 액세스, [https://github.com/socialpoint-labs/unity-yaml-parser](https://github.com/socialpoint-labs/unity-yaml-parser)  
11. UnityYAML \- Unity \- Manual, 2월 13, 2026에 액세스, [https://docs.unity3d.com/6000.3/Documentation/Manual/UnityYAML.html](https://docs.unity3d.com/6000.3/Documentation/Manual/UnityYAML.html)  
12. Shaders and Resources \- Veldrid, 2월 13, 2026에 액세스, [https://veldrid.dev/articles/shaders.html](https://veldrid.dev/articles/shaders.html)  
13. CanTalat-Yakan/3DEngine: 3D Game Engine \- Vulkan ... \- GitHub, 2월 13, 2026에 액세스, [https://github.com/CanTalat-Yakan/3DEngine](https://github.com/CanTalat-Yakan/3DEngine)  
14. What is Unity GUID — How to Get & Change GUID — 2026 \- Makaka Games, 2월 13, 2026에 액세스, [https://makaka.org/unity-tutorials/guid](https://makaka.org/unity-tutorials/guid)  
15. Part 2 \- Veldrid, 2월 13, 2026에 액세스, [https://veldrid.dev/articles/getting-started/getting-started-part2.html](https://veldrid.dev/articles/getting-started/getting-started-part2.html)  
16. Messing with Unity's GUIDs \- BorisTheBrave.Com, 2월 13, 2026에 액세스, [https://www.boristhebrave.com/2020/02/05/messing-with-unitys-guids/](https://www.boristhebrave.com/2020/02/05/messing-with-unitys-guids/)