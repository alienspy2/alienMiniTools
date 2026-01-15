# ThemeAssetGen

테마를 입력하면 해당 테마에 맞는 3D 에셋을 자동 생성하는 웹 앱

## 빠른 시작 (Quick Start)

모든 서비스를 순서대로 실행해야 합니다.

| 순서 | 실행 방법 | 설명 | 비고 |
|:----:|-----------|------|------|
| 1 | Ollama | LLM 서버 (에셋 리스트 생성) | 백그라운드 자동 실행 |
| 2 | ComfyUI 실행 | 2D 이미지 생성 서버 | 포트 8188 |
| 3 | `run_hunyuan2.bat` | 3D 모델 생성 서버 (Gradio) | 초기 로딩 1~2분 소요 |
| 4 | `run_server.bat` | ThemeAssetGen 메인 서버 | 포트 8000 |
| 5 | http://localhost:8000 접속 | 웹 브라우저에서 열기 | |

## 기능

- 테마 입력 → AI가 에셋 리스트 자동 생성
- 에셋별 2D 이미지 생성 (ComfyUI)
- 2D → 3D 모델 변환 (Hunyuan3D)
- 카탈로그 관리 및 ZIP 내보내기
- Three.js 기반 3D 미리보기

## 요구 사항

- Windows 10/11
- NVIDIA GPU (최소 8GB VRAM, 16GB+ 권장)
- CUDA 11.8+
- Conda (Miniconda 또는 Anaconda)

## 설치

### 1. ThemeAssetGen 설치

```bash
# conda 환경 생성
conda create -n themegen python=3.11
conda activate themegen

# 의존성 설치
cd ThemeAssetGen
pip install -r requirements.txt
```

### 2. Ollama 설치 (LLM)

[Ollama 다운로드](https://ollama.ai/download)에서 설치 후:

```bash
# 모델 다운로드
ollama pull gemma3:4b

# 서버 실행 (보통 자동 실행됨)
ollama serve
```

**포트:** 11434

### 3. ComfyUI 설치 (2D 이미지 생성)

1. **ComfyUI 다운로드**
   - [ComfyUI 릴리즈 페이지](https://github.com/comfyanonymous/ComfyUI/releases)에서 최신 버전 다운로드
   - 압축 해제 후 실행

2. **Developer 모드 활성화**
   - ComfyUI 실행 후 우측 상단 설정(⚙️) 클릭
   - `Enable Dev mode Options` 활성화

3. **포트 설정**
   - Settings → `Server` 섹션
   - `Port` 를 `8188` 로 변경
   - ComfyUI 재시작

4. **모델 설치**
   - `models/checkpoints/` 폴더에 모델 배치
   - 사용 모델: `dreamshaper_8LCM.safetensors` (SD 1.5 기반 LCM 모델)
   - [다운로드 링크](https://civitai.com/models/4384/dreamshaper)

**포트:** 8188

### 4. Hunyuan3D 설치 (3D 생성)

```bash
# 자동 설치 스크립트 실행
install_hunyuan.bat
```

또는 수동 설치:

```bash
git clone https://github.com/Tencent-Hunyuan/Hunyuan3D-2 hunyuan3d
cd hunyuan3d

conda create -n hunyuan3d python=3.10
conda activate hunyuan3d

pip install torch torchvision --index-url https://download.pytorch.org/whl/cu118
pip install -r requirements.txt
pip install -e .

# 텍스처 생성 모듈
cd hy3dgen/texgen/custom_rasterizer && pip install . && cd ../../..
cd hy3dgen/texgen/differentiable_renderer && pip install . && cd ../../..
```

**포트:** 7860

## 실행

### 1. 외부 서비스 실행

```bash
# 터미널 1: Ollama (보통 자동 실행)
ollama serve

# 터미널 2: ComfyUI
cd ComfyUI
python main.py --listen 0.0.0.0 --port 8188

# 터미널 3: Hunyuan3D
run_hunyuan.bat
# 또는: cd hunyuan3d && conda activate hunyuan3d && python gradio_app.py --port 7860 --enable_texture
```

### 2. ThemeAssetGen 서버 실행

```bash
# 방법 1: 배치 파일
run_server.bat

# 방법 2: 직접 실행
conda activate themegen
python run.py
```

### 3. 브라우저 접속

http://localhost:8000

## 사용법

1. **테마 입력**: "중세 판타지 성", "현대 오피스", "우주 정거장" 등
2. **에셋 리스트 확인**: AI가 생성한 에셋 목록 확인 및 편집
3. **생성 실행**: 개별 에셋 또는 전체 생성 버튼 클릭
4. **3D 미리보기**: 완료된 에셋의 "3D 보기" 버튼
5. **내보내기**: ZIP으로 전체 에셋 다운로드

## 서비스 포트 요약

| 서비스 | 포트 | 설명 |
|--------|------|------|
| ThemeAssetGen | 8000 | 메인 웹 앱 |
| Ollama | 11434 | LLM (에셋 리스트 생성) |
| ComfyUI | 8188 | 2D 이미지 생성 |
| Hunyuan3D | 7860 | 3D 모델 생성 |

## 환경 변수

`config.py`에서 기본값 설정, 환경 변수로 덮어쓰기 가능:

```bash
set OLLAMA_URL=http://127.0.0.1:11434
set OLLAMA_MODEL=gemma3:4b
set COMFYUI_URL=http://127.0.0.1:8188
set HUNYUAN3D_URL=http://127.0.0.1:7860
set SERVER_PORT=8000
```

## 출력 포맷

- **2D 미리보기**: PNG (1024x1024)
- **3D 모델**: GLB (텍스처 포함), OBJ

## 폴더 구조

```
data/
└── catalogs/
    └── {catalog_id}/
        └── assets/
            └── {asset_id}/
                ├── preview.png    # 2D 이미지
                ├── model.glb      # 3D 모델
                └── model.obj      # OBJ 포맷
```

## 문제 해결

### Ollama 연결 실패
```bash
# Ollama 상태 확인
curl http://localhost:11434/api/tags
```

### ComfyUI 연결 실패
- ComfyUI가 `--listen 0.0.0.0` 옵션으로 실행되었는지 확인
- 체크포인트 모델이 설치되었는지 확인

### Hunyuan3D 연결 실패
- GPU 메모리 부족 시 다른 프로세스 종료
- `--enable_texture` 옵션 확인

### 3D 변환 실패
```bash
# trimesh 설치 확인
pip install trimesh
```
