# ThemeAssetGen 구현 계획

## 개요
테마를 입력하면 해당 테마에 맞는 3D 에셋(소품, 가구, 벽, 천장 등)을 자동 생성하고 카탈로그로 관리하는 웹 앱

## 기술 스택
- **백엔드**: FastAPI (Python)
- **프론트엔드**: HTML/CSS/JavaScript + Three.js (3D 뷰어)
- **LLM**: Ollama (gemma3:4b) - 테마 → 에셋 리스트 생성
- **2D 이미지 생성**: ComfyUI (로컬)
- **3D 생성**: Hunyuan3D (로컬)
- **DB**: SQLite + SQLAlchemy
- **3D 출력 포맷**: GLB, OBJ
- **Python 환경**: conda env `themegen`

## 프로젝트 구조
```
ThemeAssetGen/
├── backend/
│   ├── main.py                 # FastAPI 앱 진입점
│   ├── config.py               # 환경설정
│   ├── api/
│   │   └── routes/
│   │       ├── theme.py        # 테마 → 에셋 리스트 API
│   │       ├── asset.py        # 에셋 관리 API
│   │       ├── catalog.py      # 카탈로그 API
│   │       └── generation.py   # 생성 파이프라인 API
│   ├── services/
│   │   ├── ollama_service.py   # Ollama LLM 연동
│   │   ├── comfyui_service.py  # ComfyUI 2D 생성
│   │   ├── hunyuan3d_service.py # Hunyuan3D 3D 생성
│   │   └── pipeline_service.py # 파이프라인 오케스트레이션
│   ├── models/
│   │   ├── schemas.py          # Pydantic 스키마
│   │   ├── database.py         # DB 설정
│   │   └── entities.py         # SQLAlchemy 엔티티
│   ├── workflows/
│   │   └── asset_generation.json # ComfyUI 워크플로우
│   └── utils/
│       └── prompt_templates.py # LLM 프롬프트 템플릿
├── frontend/
│   ├── index.html
│   ├── css/style.css
│   └── js/
│       ├── app.js              # 메인 로직
│       ├── api.js              # API 호출
│       └── 3d-viewer.js        # Three.js 뷰어
├── data/
│   ├── catalogs/               # 카탈로그 저장소
│   └── database.db             # SQLite DB
├── requirements.txt
└── run.py                      # 서버 실행
```

## 핵심 데이터 모델

### Asset 엔티티
```python
class Asset:
    id: str (UUID)
    catalog_id: str
    name: str           # 영문명
    name_kr: str        # 한글명
    category: Enum      # prop, furniture, wall, ceiling, floor, decoration, lighting
    description: str    # 설명
    prompt_2d: str      # 2D 생성 프롬프트
    status: Enum        # pending, generating_2d, generating_3d, completed, failed
    preview_image_path: str
    model_glb_path: str
    model_obj_path: str
```

## API 엔드포인트

| Method | Endpoint | 설명 |
|--------|----------|------|
| POST | `/api/theme/generate` | 테마 → 에셋 리스트 생성 |
| GET | `/api/asset/{id}` | 에셋 조회 |
| PUT | `/api/asset/{id}` | 에셋 편집 |
| POST | `/api/asset/{id}/generate` | 단일 에셋 2D→3D 생성 |
| GET | `/api/catalog/list` | 카탈로그 목록 |
| GET | `/api/catalog/{id}` | 카탈로그 상세 |
| POST | `/api/catalog/{id}/generate-all` | 전체 에셋 배치 생성 |
| GET | `/api/catalog/{id}/export` | ZIP 내보내기 |
| GET | `/api/generation/stream/{id}` | SSE 진행률 스트리밍 |
| GET | `/files/preview/{id}` | 2D 이미지 서빙 |
| GET | `/files/model/{id}/{format}` | 3D 모델 다운로드 |

## 워크플로우

```
테마 입력 → Ollama(에셋 리스트 JSON) → 사용자 편집 →
각 에셋: ComfyUI(2D 이미지) → Hunyuan3D(3D GLB) → 포맷 변환(OBJ) → 카탈로그 저장
```

## 구현 순서

### Phase 1: 기본 인프라
- [x] 프로젝트 디렉토리 생성
- [x] FastAPI 기본 설정 (`backend/main.py`)
- [x] SQLAlchemy 모델 (`backend/models/`)
- [x] Pydantic 스키마 (`backend/models/schemas.py`)
- [x] 설정 파일 (`backend/config.py`, `requirements.txt`)

### Phase 2: Ollama 연동
- [x] OllamaService 구현 (기존 InvokeComfyUI 패턴 참고)
- [x] 에셋 리스트 생성 프롬프트 템플릿
- [x] `/api/theme/generate` API 구현

### Phase 3: ComfyUI 연동
- [x] ComfyUIService 구현 (InvokeComfyUI main.py 로직 활용)
- [x] 에셋 생성용 워크플로우 JSON 작성
- [x] 2D 이미지 생성 API

### Phase 4: Hunyuan3D 연동
- [x] Hunyuan3DService 구현
- [x] 이미지 → 3D 변환 API 호출
- [x] trimesh로 GLB → OBJ 변환

### Phase 5: 파이프라인 통합
- [x] PipelineService 구현
- [x] 배치 생성 + SSE 스트리밍
- [x] 에러 핸들링 및 재시도

### Phase 6: 프론트엔드
- [x] 기본 HTML/CSS 레이아웃
- [x] 테마 입력 + 에셋 리스트 UI
- [x] 진행률 표시 (SSE)
- [x] Three.js 3D 뷰어

### Phase 7: 카탈로그 관리
- [x] 카탈로그 CRUD
- [x] ZIP 내보내기/가져오기
- [x] 라이브러리 UI

## 참고 파일
- `c:\git\alienMiniTools\InvokeComfyUI\main.py` - ComfyUI/Ollama 연동 패턴
- `c:\git\alienMiniTools\InvokeComfyUI\client.py` - FastAPI 웹 UI 패턴

## 서비스 포트
- ThemeAssetGen: 8000
- ComfyUI: 8188
- Hunyuan3D: 7860
- Ollama: 11434

## Hunyuan3D 설치 가이드

### 시스템 요구사항
- NVIDIA GPU (최소 6GB VRAM, 텍스처 포함 시 16GB+ 권장)
- CUDA 12.4+ (PyTorch 2.5.1 + CUDA 12.4 사용)
- Python 3.10
- transformers 4.44.0 (텍스처 생성 호환성)

### 설치 방법 (배치 파일 사용)
```bash
# 설치 (텍스처 생성 포함)
install_hunyuan.bat

# 실행 (포트 7860)
run_hunyuan.bat
```

### API 서버 실행
Hunyuan3D는 기본적으로 Gradio 앱을 제공합니다. REST API로 호출하려면:
1. Gradio 앱 실행 후 `/api/predict` 엔드포인트 사용
2. 또는 커스텀 FastAPI 래퍼 작성 (프로젝트에 포함 예정)

**설치 경로**: `ThemeAssetGen/hunyuan3d/`
**conda 환경**: `hunyuan3d`

## 실행 환경 설정
```bash
# conda 환경 생성 (최초 1회)
conda create -n themegen python=3.11
conda activate themegen
pip install -r requirements.txt
```

## 검증 방법
1. 서버 실행: `conda activate themegen && python run.py`
2. 브라우저에서 `http://localhost:8000` 접속
3. 테마 입력 (예: "중세 판타지 성")
4. 에셋 리스트 생성 확인
5. 개별/전체 에셋 생성 실행
6. 2D 미리보기 + 3D 뷰어 확인
7. 카탈로그 내보내기 테스트
