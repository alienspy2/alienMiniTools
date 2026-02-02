# ThemeAssetGen 구현 내역

## 프로젝트 구조

```
ThemeAssetGen/
├── backend/
│   ├── __init__.py
│   ├── main.py                 # FastAPI 앱 진입점
│   ├── config.py               # 환경설정
│   ├── api/
│   │   ├── __init__.py
│   │   └── routes/
│   │       ├── __init__.py     # 라우터 통합
│   │       ├── theme.py        # POST /api/theme/generate
│   │       ├── asset.py        # GET/PUT/DELETE /api/asset/{id}
│   │       ├── catalog.py      # /api/catalog/*
│   │       └── generation.py   # SSE 스트리밍
│   ├── services/
│   │   ├── __init__.py
│   │   ├── ollama_service.py   # Ollama LLM 연동
│   │   ├── comfyui_service.py  # ComfyUI 2D 이미지 생성
│   │   ├── hunyuan3d_service.py # Hunyuan3D 3D 모델 생성
│   │   └── pipeline_service.py # 전체 파이프라인 오케스트레이션
│   ├── models/
│   │   ├── __init__.py
│   │   ├── database.py         # SQLAlchemy 설정
│   │   ├── entities.py         # Theme, Catalog, Asset 엔티티
│   │   └── schemas.py          # Pydantic 스키마
│   ├── workflows/
│   │   └── asset_generation.json # ComfyUI 워크플로우
│   └── utils/
│       ├── __init__.py
│       └── prompt_templates.py # LLM 프롬프트 템플릿
├── frontend/
│   ├── index.html              # 메인 HTML
│   ├── css/
│   │   └── style.css           # 다크 테마 스타일
│   └── js/
│       ├── api.js              # API 호출 모듈
│       ├── 3d-viewer.js        # Three.js 3D 뷰어
│       └── app.js              # 메인 앱 로직
├── data/
│   ├── catalogs/               # 생성된 에셋 저장
│   └── database.db             # SQLite DB (자동 생성)
├── requirements.txt
├── run.py                      # 서버 실행 스크립트
├── run_server.bat              # Windows 서버 실행
├── install_hunyuan2.bat        # Hunyuan3D-2 설치 스크립트
├── run_hunyuan2.bat            # Hunyuan3D-2 Gradio UI (포트 7860)
├── run_hunyuan2_api.bat        # Hunyuan3D-2 API 서버 (포트 8080)
└── fix_hunyuan2_transformers.bat  # transformers/diffusers 호환성 수정
```

## 백엔드 구현

### 1. 데이터 모델 (entities.py)

```python
class AssetCategory(Enum):
    PROP, FURNITURE, WALL, CEILING, FLOOR, DECORATION, LIGHTING, OTHER

class GenerationStatus(Enum):
    PENDING, GENERATING_2D, GENERATING_3D, COMPLETED, FAILED

class Theme:      # 테마 정보
class Catalog:    # 카탈로그 (테마별 에셋 모음)
class Asset:      # 개별 에셋
```

### 2. API 엔드포인트

| Method | Endpoint | 설명 |
|--------|----------|------|
| POST | `/api/theme/generate` | 테마 → 에셋 리스트 생성 |
| GET | `/api/asset/{id}` | 에셋 조회 |
| PUT | `/api/asset/{id}` | 에셋 편집 |
| DELETE | `/api/asset/{id}` | 에셋 삭제 |
| POST | `/api/asset/{id}/generate` | 단일 에셋 2D→3D 생성 |
| GET | `/api/catalog/list` | 카탈로그 목록 |
| GET | `/api/catalog/{id}` | 카탈로그 상세 |
| DELETE | `/api/catalog/{id}` | 카탈로그 삭제 |
| POST | `/api/catalog/{id}/generate-all` | 전체 에셋 배치 생성 |
| GET | `/api/catalog/{id}/export` | ZIP 내보내기 |
| GET | `/api/generation/stream/{id}` | SSE 진행률 스트리밍 |
| GET | `/files/preview/{id}` | 2D 이미지 서빙 |
| GET | `/files/model/{id}/{format}` | 3D 모델 다운로드 (glb/obj) |

### 3. 서비스 구현

#### OllamaService
- `generate_asset_list(theme)`: 테마 → 에셋 리스트 JSON 생성
- `refine_prompt(prompt)`: 프롬프트 최적화

#### ComfyUIService
- `generate_image(prompt, output_path)`: 2D 이미지 생성
- `load_workflow()`: 워크플로우 JSON 로드
- `set_positive_prompt()`: 프롬프트 설정

#### Hunyuan3DService
- `generate_3d_from_image(image_path, output_dir, asset_id)`: 2D→3D 변환
- `_convert_to_obj(glb_path)`: GLB→OBJ 변환 (trimesh)

#### PipelineService
- `generate_theme_assets(theme_name)`: 테마 → 에셋 리스트 전체 파이프라인
- `generate_single_asset(asset_id)`: 단일 에셋 생성 (2D→3D)
- `generate_batch(catalog_id)`: 배치 생성 + SSE 상태 업데이트

## 프론트엔드 구현

### 1. index.html
- 테마 입력 폼
- 카탈로그 선택 드롭다운
- 에셋 리스트 (편집/생성/삭제/3D보기)
- 생성 진행률 표시
- 3D 뷰어 (Three.js)
- 에셋 편집 모달

### 2. style.css
- 다크 테마 UI
- CSS 변수 기반 색상 시스템
- 반응형 레이아웃

### 3. JavaScript 모듈

**api.js**
- 모든 API 호출 함수
- SSE 스트리밍 연결

**3d-viewer.js**
- Three.js 기반 3D 뷰어 클래스
- GLTFLoader로 GLB 모델 로드
- OrbitControls로 마우스 조작

**app.js**
- 이벤트 리스너 설정
- UI 상태 관리
- 에셋 렌더링

## 외부 서비스 연동

### Ollama (LLM)
- URL: `http://127.0.0.1:11434`
- 모델: `gemma3:4b`
- API: `/api/generate`

### ComfyUI (2D 이미지)
- URL: `http://127.0.0.1:23000`
- API: `/prompt`, `/history/{id}`, `/view`
- 워크플로우: `backend/workflows/asset_generation.json`

### Hunyuan3D-2 (3D 모델)
- URL: `http://127.0.0.1:7860` (Gradio)
- API: gradio_client
- 모델: `tencent/Hunyuan3D-2`
- 텍스처 생성 지원 (Hunyuan3D-Paint-v2-0)

## 데이터 흐름

```
1. 사용자가 테마 입력 ("중세 판타지 성")
                ↓
2. Ollama가 에셋 리스트 JSON 생성
   - 왕좌, 촛대, 석벽, 깃발 등 10~20개
   - 각 에셋에 2D 프롬프트 포함
                ↓
3. DB에 Theme, Catalog, Asset 저장
                ↓
4. 사용자가 에셋 리스트 확인/편집
                ↓
5. "생성" 버튼 클릭
                ↓
6. ComfyUI로 2D 이미지 생성
   - prompt_2d → preview.png
                ↓
7. Hunyuan3D로 3D 모델 생성
   - preview.png → model.glb
                ↓
8. trimesh로 포맷 변환
   - model.glb → model.obj
                ↓
9. 카탈로그에 저장
   - data/catalogs/{catalog_id}/assets/{asset_id}/
```

## 설정 옵션 (config.py)

| 환경변수 | 기본값 | 설명 |
|----------|--------|------|
| `OLLAMA_URL` | `http://127.0.0.1:11434` | Ollama 서버 |
| `OLLAMA_MODEL` | `gemma3:4b` | 사용할 모델 |
| `COMFYUI_URL` | `http://127.0.0.1:23000` | ComfyUI 서버 |
| `HUNYUAN3D_URL` | `http://127.0.0.1:7860` | Hunyuan3D Gradio 서버 |
| `SERVER_HOST` | `0.0.0.0` | 서버 호스트 |
| `SERVER_PORT` | `8000` | 서버 포트 |

## Hunyuan3D-2 설치 요구사항

### 필수 소프트웨어
- **CUDA Toolkit 12.9**: 텍스처 모듈 빌드에 필요
  - 다운로드: https://developer.nvidia.com/cuda-12-9-0-download-archive
  - 설치 경로: `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.9`
- **Visual Studio Build Tools 2022**: C++ 컴파일러
  - "C++를 사용한 데스크톱 개발" 워크로드 필요
  - 다운로드: https://visualstudio.microsoft.com/visual-cpp-build-tools/
- **Python 3.10**: conda 환경 (hunyuan2)
- **PyTorch 2.6.0 + CUDA 12.4**
- **transformers >= 4.46.0**

### 설치 순서
```bash
# 1. Hunyuan3D-2 설치
install_hunyuan2.bat

# 2. 설치 스크립트가 자동으로 수행하는 작업:
#    - Git clone Hunyuan3D-2
#    - conda 환경 생성 (hunyuan2, Python 3.10)
#    - PyTorch 2.6.0 + CUDA 12.4 설치
#    - 의존성 설치
#    - MSVC 환경 설정 (vcvars64.bat)
#    - CUDA 12.9 환경 설정
#    - custom_rasterizer 빌드
#    - differentiable_renderer 빌드
#    - transformers>=4.46.0 설치

# 기존 설치 후 transformers 문제 발생 시:
fix_hunyuan2_transformers.bat
```

### 실행 순서
```bash
# 1. ComfyUI 실행 (포트 23000)

# 2. Hunyuan3D-2 Gradio 실행 (포트 7860)
run_hunyuan2.bat

# 3. ThemeAssetGen 서버 실행 (포트 8000)
run_server.bat
```
