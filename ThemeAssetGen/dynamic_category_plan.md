# 동적 카테고리 및 설정 시스템 개편 계획 (Dynamic Category Plan)

이 문서는 ThemeAssetGen 시스템에 동적 카테고리 생성 기능, 설정 파일 시스템 개편, UI 개선 및 다운로드 구조 변경을 위한 구현 계획을 기술합니다.

## 1. 개요 (Overview)

현재 고정된 카테고리(`wall_texture`, `stair` 등)와 `config.py`에 하드코딩된 설정을 탈피하고, 사용자의 테마 입력에 따라 유연하게 자산을 구성할 수 있도록 시스템을 개선합니다. 또한 사용자 경험(UX)을 명확하게 하기 위해 "제안(Suggestion)"과 "생성(Generation)" 단계를 분리합니다.

## 2. 주요 변경 사항 (Core Changes)

### 2.1 설정 시스템 개편 (Config System Refactoring)
*   **목표**: `backend/config.py`의 정적 설정을 `config.json` 기반의 동적 설정으로 변경.
*   **구현**:
    *   `config.json`: 실제 설정값 저장 (Git 제외).
    *   `config_loader.py`: 앱 시작 시 `config.json`이 없으면 기본값으로 생성하고, 있으면 로드하는 로직 구현.
    *   `.gitignore`: `config.json` 추가.
    *   기존 `config.py`: `config_loader.py`를 통해 값을 가져오도록 래퍼(Wrapper) 역할로 유지하거나, 사용처를 전면 수정. (하위 호환성을 위해 래퍼 유지 후 점진적 교체 추천)

### 2.2 동적 카테고리 생성 워크플로우 (Dynamic Category Workflow)
*   **기존**: 테마 입력 -> (즉시) 고정된 카테고리별 자산 프롬프트 생성 -> 이미지 생성
*   **변경**:
    1.  **테마 입력**: 사용자가 테마(예: "Cyberpunk Alley") 입력.
    2.  **카테고리 제안 (Suggest)**: LLM(Ollama)이 테마를 분석하여 적합한 자산 카테고리(예: `neon_sign`, `trash_can`, `wall_panel`)와 추천 수량을 JSON으로 반환.
    3.  **사용자 검토 및 수정**: 프론트엔드에서 제안된 카테고리 목록과 수량을 표시. 사용자가 직접 추가/삭제/수량 변경 가능.
    4.  **생성 시작 (Generate)**: 사용자가 "프롬프트 생성" 버튼 클릭 시, 확정된 카테고리 구성으로 작업 큐 등록.

### 2.3 UI 개선 (Frontend Improvements)
*   **카테고리별 아코디언 (Accordion UI)**: 결과 화면에서 자산 목록이 너무 길어지지 않도록 카테고리별로 접고 펼칠 수 있는 UI 적용.
*   **단계별 입력 폼**: 
    *   Step 1: 테마 입력
    *   Step 2: 카테고리 구성 확인 (동적 폼)
    *   Step 3: 진행 상황 모니터링

### 2.4 ZIP 다운로드 구조 개선 (ZIP Structure)
*   **변경 전**: 모든 이미지가 루트 혹은 단순 나열.
*   **변경 후**: 카테고리명으로 폴더 생성 후 해당 자산 이미지 배치.
    *   예: `Theme_Asset/neon_sign/asset_01.png`, `Theme_Asset/wall_panel/asset_02.png`

---

## 3. 상세 구현 단계 (Implementation Steps)

### Phase 1: 설정 시스템 변경 (Config)

1.  **`backend/default_config.py` 생성**: 기존 `config.py`의 값들을 기본값 딕셔너리로 정의.
2.  **`backend/config_manager.py` 구현**:
    *   `load_config()`: `config.json` 로드 또는 기본값 생성.
    *   `save_config()`: 변경사항 저장.
    *   `get_config(key)`: 값 조회.
3.  **`backend/config.py` 수정**: `ConfigManager` 인스턴스를 사용하여 전역 변수처럼 접근 가능하도록 변경 (기존 코드 영향 최소화).
4.  **`.gitignore` 업데이트**: `config.json` 추가.
5.  **`edit_config.py` 수정**: `ConfigManager`를 사용하여 `config.json`을 읽고 쓰도록 GUI 에디터 로직 전면 수정.

### Phase 2: 백엔드 로직 변경 (Backend Logic)

1.  **Ollama 프롬프트 템플릿 추가 및 파싱 로직 강화**:
    *   테마를 입력받아 적합한 Asset Category List(Name, Recommended Count, Description)를 JSON으로 출력하도록 템플릿 작성.
    *   **방어 로직 구현**: LLM이 유효하지 않은 JSON을 반환할 경우를 대비해, 재시도(Retry) 로직과 부분 파싱(Regex extraction) 기능을 `OllamaService`에 추가.
    *   **중복 방지 로직 (De-duplication)**: 자산 목록 제안 또는 프롬프트 생성 시, `existing_assets` 리스트를 컨텍스트로 제공하여 중복된 아이템이 생성되지 않도록 프롬프트 강화.
2.  **API 엔드포인트 추가/수정 (`backend/api/routes/generation.py`)**:
    *   `POST /api/generation/suggest`: 테마 -> 카테고리 제안 JSON 반환.
    *   `POST /api/generation/start`: 테마 + 확정된 카테고리 리스트 -> 작업 큐 등록.
3.  **`PipelineService` 수정**:
    *   고정된 `ASSET_GENERATION_COUNTS` 대신, 요청받은 카테고리 구조를 순회하며 프롬프트 생성 및 이미지 생성 작업 스케줄링.

### Phase 3: 프론트엔드 구현 (Frontend)

1.  **입력 폼 컴포넌트 분리**:
    *   `ThemeInput`: 테마 텍스트 입력.
    *   `CategoryEditor`: 카테고리 리스트(이름, 수량) 추가/삭제/수정 가능한 동적 리스트 UI.
2.  **상태 관리 로직 수정**:
    *   `SUGGESTING` -> `REVIEWING` -> `GENERATING` 상태 흐름 도입.
3.  **결과 목록 컴포넌트 (`AssetList`)**:
    *   HTML `<details>` 태그 또는 JS 기반 아코디언 컴포넌트로 카테고리 그룹화.

### Phase 4: 유틸리티 및 다운로드 (Utils)

1.  **ZIP 생성 로직 수정 (`backend/utils/file_utils.py` 또는 해당 위치)**:
    *   파일을 압축할 때 `arcname` 인자를 사용하여 `category_name/filename` 형태로 경로 지정.

---

## 4. 예상되는 파일 변경 (Affected Files)

*   `backend/config.py` (Refactor)
*   `backend/config_manager.py` (New)
*   `backend/default_config.py` (New)
*   `backend/services/ollama_service.py` (Update: Suggestion feature)
*   `backend/services/pipeline_service.py` (Update: Dynamic loop)
*   `backend/api/routes/generation.py` (Update: APIs)
*   `frontend/index.html` (or separate js files): UI Logic Update
*   `.gitignore`

## 5. 완료 기준 (Definition of Done)

*   `config.json`이 없을 때 자동 생성되고, GUI 에디터로 수정 사항이 즉시 `config.json`에 반영되어야 한다.
*   사용자가 테마를 입력하면 LLM이 적절한 카테고리를 제안하고, UI에서 이를 수정할 수 있어야 한다.
*   자산 생성이 완료되면 결과 화면이 카테고리별로 그룹화되어야 한다.
*   ZIP 다운로드 시 폴더 구조가 정리되어 있어야 한다.
