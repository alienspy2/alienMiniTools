# Hunyuan3D-2.1 설치 가이드

## 개요
- GitHub: https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1
- 모델: `tencent/Hunyuan3D-2.1`
- Conda 환경: `hunyuan21`
- 설치 폴더: `hunyuan21/`

## 요구사항
- Python 3.10
- PyTorch 2.5.1 (CUDA 12.4)
- **CUDA Toolkit 12.4** (시스템 설치 필요)

## 자동 설치
```batch
install_hunyuan21.bat
```

## 수동 설치 (문제 발생 시)

### 1. 저장소 클론
```batch
git clone https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1 hunyuan21
cd hunyuan21
```

### 2. Conda 환경 생성
```batch
conda create -n hunyuan21 python=3.10 -y
conda activate hunyuan21
```

### 3. PyTorch 설치 (CUDA 12.4)
```batch
pip install torch==2.5.1 torchvision==0.20.1 torchaudio==2.5.1 --index-url https://download.pytorch.org/whl/cu124
```

### 4. 의존성 설치 (bpy 제외)
```batch
findstr /v /i "^bpy" requirements.txt > requirements_no_bpy.txt
pip install -r requirements_no_bpy.txt
del requirements_no_bpy.txt
```
> **참고**: `bpy`는 Blender Python으로 pip 설치 불가. Blender 기능 필요시 별도 설치.

### 5. Custom Rasterizer 설치 (CUDA 12.4 필요)
```batch
set CUDA_HOME=C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.4
set PATH=%CUDA_HOME%\bin;%PATH%

cd hy3dpaint\custom_rasterizer
pip install -e . --no-build-isolation
cd ..\..
```

### 6. Differentiable Renderer 설치
```batch
cd hy3dpaint\DifferentiableRenderer
compile_mesh_painter.bat
cd ..\..
```

### 7. RealESRGAN 모델 다운로드
```batch
mkdir hy3dpaint\ckpt
curl -L -o hy3dpaint\ckpt\RealESRGAN_x4plus.pth https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth
```

### 8. 추가 의존성
```batch
pip install gradio spaces
```

## 실행

### Gradio 앱 (포트 7860)
```batch
run_hunyuan21.bat
```
또는:
```batch
conda activate hunyuan21
cd hunyuan21
python gradio_app.py --model_path tencent/Hunyuan3D-2.1 --subfolder hunyuan3d-dit-v2-1 --texgen_model_path tencent/Hunyuan3D-2.1 --low_vram_mode
```

### API 서버 (포트 8081)
```batch
run_hunyuan21_api.bat
```

## 알려진 문제

### 1. bpy 설치 실패
```
ERROR: No matching distribution found for bpy==4.0
```
**해결**: bpy는 pip로 설치 불가. 설치 스크립트에서 자동으로 제외됨.

### 2. Custom Rasterizer CUDA 버전 불일치
```
ninja: build stopped: subcommand failed.
```
**원인**: 시스템 CUDA 버전(12.9)과 PyTorch CUDA 버전(12.4) 불일치

**해결**: CUDA 12.4 설치 후 환경변수 설정
- 다운로드: https://developer.nvidia.com/cuda-12-4-0-download-archive
- 설치 시 Custom → CUDA만 선택 (드라이버 제외)
- 환경변수 설정 후 재설치

### 3. torch 모듈 없음 에러
```
ModuleNotFoundError: No module named 'torch'
```
**해결**: `--no-build-isolation` 옵션 추가
```batch
pip install -e . --no-build-isolation
```

## 버전 비교

| 항목 | Hunyuan3D-2.0 | Hunyuan3D-2.1 |
|------|---------------|---------------|
| 폴더 | hunyuan2/ | hunyuan21/ |
| Conda | hunyuan2 | hunyuan21 |
| API 포트 | 8080 | 8081 |
| 텍스처 모듈 | hy3dgen/texgen | hy3dpaint |
