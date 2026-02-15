# IronRose 개발 문서

> **"Iron for Strength, Rose for Beauty"**
> 현재 상태: Phase 7 완료 (2026-02-15)

IronRose 게임 엔진의 상세 개발 계획 문서입니다.

---

## Phase별 상세 계획

### 기초 구축 (Phase 0-3) ✅ 완료
- **[Phase 0: 프로젝트 구조 및 환경 설정](Phase0_ProjectSetup.md)** ✅
- **[Phase 1: 최소 실행 가능 엔진](Phase1_MinimalEngine.md)** ✅
- **[Phase 2: Roslyn 핫 리로딩 시스템](Phase2_HotReloading.md)** ✅
- **[Phase 2A: Engine Core 핫 리로딩](Phase2A_HotReloading_EngineCore.md)** ✅
- **[Phase 2B: 플러그인 기반 아키텍처](Phase2B_PluginBasedArchitecture.md)** ✅
- **[Phase 3: Unity Architecture 구현](Phase3_UnityArchitecture.md)** ✅

### 렌더링 및 에셋 (Phase 4-5) ✅ 완료
- **[Phase 4: 기본 렌더링 파이프라인](Phase4_BasicRendering.md)** ✅
- **[Phase 5: Unity 에셋 임포터](Phase5_AssetImporter.md)** ✅
- **[Phase 5A: SpriteRenderer](Phase5A_SpriteRenderer.md)** ✅
- **[Phase 5B: TextRenderer](Phase5B_TextRenderer.md)** ✅

### 물리 및 고급 렌더링 (Phase 6-7) ✅ 완료
- **[Phase 6: 물리 엔진 통합](Phase6_PhysicsEngine.md)** ✅
- **[Phase 7: Deferred Rendering & PBR](Phase7_DeferredPBR.md)** ✅

---

## 타임라인

| Phase | 예상 | 실제 | 주요 산출물 |
|-------|------|------|------------|
| Phase 0-2 | 2주 | **1일** ✅ | 윈도우 + 핫 리로딩 동작 |
| Phase 3-4 | 3주 | **2일** ✅ | Unity 스크립트 실행 + 3D 렌더링 |
| Phase 5 | 2주 | **1일** ✅ | Unity 에셋 로드 + Sprite/Text |
| Phase 6 | 1-2주 | **1일** ✅ | 물리 엔진 통합 (3D + 2D) |
| Phase 7 | 3주 | **1일** ✅ | Deferred PBR + IBL + Post-Processing |
| **Total** | **17-18주** | **3일 (Phase 0-7)** | |

---

## 코드 통계 (Phase 7 기준)

- **~11,255줄** C# 소스 + **~921줄** GLSL 셰이더
- **59개** RoseEngine 컴포넌트 (Unity API ~80% 호환)
- **14개** 셰이더 파일 (Forward + Deferred + Post-Processing)
- **7개** 데모 씬 (FrozenCode)
- **18개** NuGet 패키지

---

## 참고 문서

- [MasterPlan.md](MasterPlan.md) - 전체 프로젝트 로드맵
- [Progress.md](Progress.md) - 개발 진행 상황 추적
- [AI-Native Game Engine: Architecture & Roadmap](AI-Native%20Game%20Engine_%20Architecture%20&%20Roadmap.md) - 아키텍처 설계

---

**IronRose - Simple, AI-Native, .NET-Powered**
