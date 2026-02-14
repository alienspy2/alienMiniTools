# **AI-Native.NET 10 게임 엔진 아키텍처 설계 보고서**

## **1\. 프로젝트 비전: The "Prompt-to-Play" Engine**

본 프로젝트는 기존의 게임 엔진(Unity, Unreal)이 가진 무거운 에디터 중심의 워크플로우를 탈피하고, **AI(LLM)가 코드를 생성하고 엔진이 이를 즉시 컴파일하여 실행하는** 새로운 패러다임을 제시합니다..NET 10의 최신 기술을 활용하여 유니티의 방대한 API 생태계를 흡수하되, 내부적으로는 가볍고 빠른 최신 렌더링/메모리 아키텍처를 지향합니다.

## **2\. 엔진 네이밍 추천**

AI와의 협업, 실시간 코드 주입, 그리고 유니티를 대체한다는 의미를 담아 다음과 같은 이름을 제안합니다.

| 이름 | 의미 및 브랜딩 |
| :---- | :---- |
| **Synapse (시냅스)** | 뇌의 신호 전달 경로처럼, AI의 코드(생각)가 엔진(몸)으로 즉시 전달되어 실행됨을 의미합니다. |
| **LiveWire (라이브와이어)** | 코드가 멈추지 않고(Live) 계속해서 연결(Wire)되고 수정되는 역동적인 핫 리로딩 환경을 강조합니다. |
| **Mimic (미믹)** | 유니티의 API와 생태계를 완벽하게 모방(Mimic)하면서도 더 가볍고 빠르다는 의미를 가집니다. |
| **Flux 3D** | 데이터와 코드가 고정되지 않고 끊임없이 흐르는(Flux) 유동적인 개발 환경을 상징합니다. |
| **Runtime 10** | 에디터보다 **런타임** 자체에 집중하며,.NET **10** 기반의 차세대 엔진임을 직관적으로 보여줍니다. |

## ---

**3\. 핵심 아키텍처: 기술 스택 및 구조**

### **3.1 기반 기술 (Foundation)**

* **Runtime:**.NET 10 (Preview/RC) \- AOT 컴파일과 JIT의 이점을 혼합하여 사용.  
* **Windowing/Input:** **Silk.NET (SDL3)** \- 가장 가볍고 호환성이 좋은 SDL3를 통해 윈도우 생성 및 입력을 처리합니다.\[13\]  
* **Rendering:** **Veldrid** (Vulkan Backend 주력) \- 저수준 그래픽 API를 직접 다루지 않고도 Vulkan의 성능을 활용합니다.1  
* **Scripting:** **Roslyn (Microsoft.CodeAnalysis)** \- 런타임에 C\# 코드를 파싱하고 컴파일하여 메모리에 로드합니다.

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

AI(LLM)는 인터넷상의 방대한 유니티 코드로 학습되어 있습니다. 따라서 **"using UnityEngine;"** 스타일의 코드를 그대로 실행할 수 있게 하는 것이 핵심입니다.

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

**5\. 렌더링 파이프라인: Deferred Rendering & PBR**

성능과 확장성을 위해 지연 렌더링(Deferred Rendering)만을 지원하며, 물리 기반 렌더링(PBR)을 기본으로 합니다.

### **5.1 G-Buffer 설계 (Veldrid Framebuffer)**

R8G8B8A8\_UNorm 같은 포맷을 사용하여 최소 3\~4개의 RenderTarget을 구성합니다.\[15\]

| Render Target | 채널 (RGBA) 데이터 |
| :---- | :---- |
| **RT0 (Albedo)** | RGB: Base Color, A: Transmission/Alpha |
| **RT1 (Normal)** | RGB: World Normal (Octahedron encoding 권장), A: Smoothness |
| **RT2 (Material)** | R: Metallic, G: Occlusion, B: Emission, A: Unused |
| **Depth** | D32\_Float\_S8\_UInt (Hardware Depth) |

### **5.2 렌더링 패스 (Passes)**

1. **Geometry Pass:** 모든 메시를 그려 G-Buffer를 채웁니다. Veldrid.SPIRV를 통해 GLSL/HLSL 셰이더를 Vulkan SPIR-V로 변환하여 사용합니다.11  
2. **Lighting Pass:** 화면 전체 Quad를 그리며 G-Buffer를 샘플링하여 조명을 계산합니다. 수천 개의 동적 광원을 처리하기 위해 **Tiled Deferred** 또는 **Clustered Lighting** 기법을 적용할 수 있습니다.  
3. **Post-Processing:** Bloom, ToneMapping, TAA 등을 Compute Shader나 Fragment Shader로 처리합니다.

## ---

**6\. 리소스 관리: Reference Counting**

C\#의 GC에만 의존하면 GPU 메모리 해제 시점이 불명확하므로, 명시적인 참조 카운팅을 도입합니다.

**RefCounted 패턴:**

* 모든 GPU 리소스(Texture, Mesh)는 RefCounted\<T\> 래퍼로 감쌉니다.  
* **Unity 호환성:** 유니티의 Resources.Load()나 Destroy() 동작을 흉내 낼 때, 내부적으로는 Retain()과 Release()를 호출합니다.  
* 참조 카운트가 0이 되면 즉시 Veldrid.Resource.Dispose()를 호출하여 VRAM을 확보합니다.\[16\]

## ---

**7\. 향후 확장 로드맵**

1. **Phase 1 (Skeleton):** SDL3 윈도우 \+ Veldrid Clear 화면 \+ Roslyn으로 "Hello World" 스크립트 핫 리로딩 성공.
2. **Phase 2 (Unity Architecture):** GameObject, Component, MonoBehaviour 클래스를 Unity와 동일하게 구현. 유니티 큐브 프리팹 로드.
3. **Phase 3 (Rendering):** Deferred G-Buffer 구현 및 PBR 라이팅 적용.
4. **Phase 4 (AI Integration):** LLM API를 연동하여 런타임에 "빨간색 큐브를 만들어줘"라고 입력하면 코드가 생성되어 실행되는 데모 완성.

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