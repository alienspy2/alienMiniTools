# IronRose 개발 문서

> **"Iron for Strength, Rose for Beauty"**

IronRose 게임 엔진의 상세 개발 계획 문서입니다.

---

## 📋 Phase별 상세 계획

전체 개발 과정은 12개의 Phase로 구성되어 있습니다.

### 🏗️ 기초 구축 (Phase 0-3)
- **[Phase 0: 프로젝트 구조 및 환경 설정](Phase0_ProjectSetup.md)** *(1-2일)*
  - 솔루션 구조 설계
  - NuGet 패키지 설치
  - Git 저장소 초기화

- **[Phase 1: 최소 실행 가능 엔진](Phase1_MinimalEngine.md)** *(2-3일)*
  - SDL3 윈도우 생성
  - Veldrid 그래픽 초기화
  - 기본 렌더링 루프

- **[Phase 2: Roslyn 핫 리로딩 시스템](Phase2_HotReloading.md)** *(3-4일)*
  - Roslyn 컴파일러 래퍼
  - AssemblyLoadContext 핫 스왑
  - 상태 보존 시스템

- **[Phase 2B: 플러그인 시스템](전략변경.md)** *(진행 중)*
  - ~~Phase 2A: 엔진 핫 리로드~~ → 폐기, 플러그인 방식으로 전환
  - Bootstrapper/Engine 통합 완료
  - 플러그인 인프라, 엔진 API, 핫 리로드

- **[Phase 3: Unity Architecture 구현](Phase3_UnityArchitecture.md)** *(4-5일)*
  - 기본 수학 타입 (Vector3, Quaternion, Color)
  - GameObject & Component 시스템
  - MonoBehaviour 라이프사이클
  - Unity InputSystem (액션 기반 입력: InputAction, 2DVector 컴포짓)

### 🎨 렌더링 및 에셋 (Phase 4-5)
- **[Phase 4: 기본 렌더링 파이프라인](Phase4_BasicRendering.md)** *(5-6일)*
  - 메시 렌더링 시스템
  - 기본 셰이더 (GLSL → SPIR-V)
  - 카메라 시스템
  - 큐브 프리미티브

- **[Phase 5: Unity 에셋 임포터](Phase5_AssetImporter.md)** *(4-5일)*
  - YAML 파서 (Unity Scene/Prefab)
  - FBX 메시 로더
  - PNG 텍스처 로더
  - AssetDatabase (GUID 매핑)

### ⚙️ 물리 및 고급 렌더링 (Phase 6-7)
- **[Phase 6: 물리 엔진 통합](Phase6_PhysicsEngine.md)** *(4-6일, 선택사항)*
  - BepuPhysics v2 (3D 물리)
  - Box2D (2D 물리)
  - Unity API 호환 (Physics, Rigidbody)

- **[Phase 7: Deferred Rendering & PBR](Phase7_DeferredPBR.md)** *(6-8일)*
  - G-Buffer 생성
  - PBR 라이팅 (Cook-Torrance BRDF)
  - Post-Processing (Bloom, Tone Mapping)

### 🤖 AI 통합 및 최적화 (Phase 8-9)
- **[Phase 8: AI 통합](Phase8_AIIntegration.md)** *(4-5일)*
  - LLM API 통합 (Claude API)
  - 명령 인터페이스
  - 코드 검증 및 샌드박싱

- **[Phase 9: 최적화 및 안정화](Phase9_Optimization.md)** *(3-4일)*
  - GPU 리소스 Reference Counting
  - 선택적 성능 최적화
  - 프로파일링 도구
  - 유닛 테스트 & CI/CD

### 📚 문서화 및 공개 (Phase 10-11)
- **[Phase 10: 문서화 및 샘플](Phase10_Documentation.md)** *(5-6일)*
  - API 문서 (DocFX)
  - Unity 마이그레이션 가이드
  - 샘플 프로젝트 4개
  - YouTube 데모 영상

- **[Phase 11: 커뮤니티 & 오픈소스](Phase11_Community.md)** *(3-4일 + 지속)*
  - GitHub 공개 (MIT 라이선스)
  - Discord/Reddit 커뮤니티
  - NuGet 패키지 배포
  - 플러그인 생태계

---

## ⏱️ 전체 타임라인

| Phase | 기간 | 누적 기간 |
|-------|------|----------|
| Phase 0-2 | 6-9일 | 2주 |
| Phase 3-4 | 9-11일 | 3주 |
| Phase 5 | 4-5일 | 5주 |
| Phase 6 (선택) | 4-6일 | 6-7주 |
| Phase 7 | 6-8일 | 8-9주 |
| Phase 8 | 4-5일 | 10주 |
| Phase 9 | 3-4일 | 11주 |
| Phase 10-11 | 8-10일 | 13-14주 |
| **Total** | **17-18주 (약 4-5개월)** | **1.0 릴리스** |

---

## 🎯 마일스톤

### Milestone 1: 기본 엔진 (Phase 0-2)
✅ 윈도우가 열리고 화면이 렌더링됨
✅ 핫 리로딩 동작

### Milestone 2: Unity 호환 (Phase 3-4)
✅ Unity 스타일 스크립트 실행
✅ 3D 메시 렌더링

### Milestone 3: 에셋 지원 (Phase 5)
✅ Unity Prefab 로드
✅ FBX/PNG 임포트

### Milestone 4: 고급 기능 (Phase 6-7)
✅ 물리 시뮬레이션 (선택)
✅ PBR 렌더링

### Milestone 5: AI 통합 (Phase 8)
✅ 프롬프트로 게임 오브젝트 생성
✅ 실시간 코드 생성 및 실행

### Milestone 6: 릴리스 준비 (Phase 9-11)
✅ 성능 최적화
✅ 문서 및 샘플
✅ 오픈소스 공개

---

## 📖 참고 문서

### 메인 문서
- [MasterPlan.md](../MasterPlan.md) - 전체 프로젝트 개요
- [AI-Native Game Engine: Architecture & Roadmap](../AI-Native%20Game%20Engine_%20Architecture%20&%20Roadmap.md) - 아키텍처 설계

### 개발 가이드
- Unity 마이그레이션 가이드
- 성능 베스트 프랙티스
- API 레퍼런스

---

## 🚀 빠른 시작 가이드

### 1. 현재 Phase 확인
자신이 어느 단계에 있는지 확인하세요.

### 2. 해당 Phase 문서 읽기
각 Phase 문서에는 다음이 포함됩니다:
- 목표
- 작업 항목 (코드 예제 포함)
- 검증 기준
- 예상 소요 시간

### 3. 작업 진행
문서의 순서대로 작업을 진행하세요.

### 4. 검증
각 Phase의 검증 기준을 모두 통과했는지 확인하세요.

### 5. 다음 Phase로
다음 Phase 문서로 이동하세요.

---

## 💡 개발 철학

### 1. 단순성 우선 (Simplicity First)
- 복잡한 아키텍처보다 이해하기 쉬운 코드
- 과도한 엔지니어링 금지

### 2. 실용주의 (Pragmatism)
- 이론적 완벽함보다 실제로 동작하는 것
- 병목이 발생하면 그때 최적화

### 3. AI 친화성 (AI-First)
- Unity 스타일 코드를 그대로 실행
- 런타임 코드 생성 및 핫 리로딩

---

## 📞 문의 및 지원

- **Issues**: [GitHub Issues](https://github.com/yourusername/IronRose/issues)
- **Discord**: [IronRose Community](https://discord.gg/ironrose)
- **Email**: contact@ironrose.dev

---

## 📜 라이선스

MIT License - 자유롭게 사용, 수정, 배포 가능

---

**Let's build the future of game development! 🚀**

**Iron for Strength, Rose for Beauty** 🌹
