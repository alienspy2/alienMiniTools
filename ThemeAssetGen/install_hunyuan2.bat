@echo off
chcp 65001 >nul
echo ========================================
echo  Hunyuan3D-2 Install Script
echo ========================================
echo.

set INSTALL_DIR=%~dp0hunyuan2
set CONDA_ENV=hunyuan2

:: conda init
call conda activate base

:: Check existing directory
if not exist "%INSTALL_DIR%" goto :do_clone

echo [!] %INSTALL_DIR% already exists.
echo [y] Overwrite (re-clone)
echo [n] Skip clone, continue installation
echo [c] Cancel
set /p CHOICE="Choice [y/n/c]: "
if /i "%CHOICE%"=="y" goto :remove_dir
if /i "%CHOICE%"=="n" goto :skip_clone
echo Installation cancelled.
pause
exit /b 1

:remove_dir
rmdir /s /q "%INSTALL_DIR%"

:do_clone
:: 1. Clone repository
echo.
echo [1/8] Cloning Hunyuan3D-2 repository...
git clone https://github.com/Tencent-Hunyuan/Hunyuan3D-2 "%INSTALL_DIR%"
if errorlevel 1 goto :error_clone

:skip_clone
cd /d "%INSTALL_DIR%"

:: 2. Create conda environment (skip if exists)
echo.
echo [2/8] Creating conda environment [%CONDA_ENV%]...
call conda activate %CONDA_ENV% 2>nul
if errorlevel 1 (
    call conda create -n %CONDA_ENV% python=3.10 -y
    if errorlevel 1 goto :error_conda
)

:: 3. Activate environment
echo.
echo [3/8] Activating conda environment...
call conda activate %CONDA_ENV%

:: 4. Install PyTorch [CUDA 12.4]
echo.
echo [4/8] Installing PyTorch 2.6.0 [CUDA 12.4]...
pip install torch==2.6.0 torchvision==0.21.0 torchaudio==2.6.0 --index-url https://download.pytorch.org/whl/cu124
if errorlevel 1 goto :error_pytorch

:: 5. Install dependencies
echo.
echo [5/8] Installing dependencies...
pip install -r requirements.txt
pip install -e .
if errorlevel 1 goto :error_deps

:: 6. Install texture modules (requires CUDA 12.9 + MSVC for PyTorch compatibility)
echo.
echo [6/8] Installing texture modules...

:: Setup Visual Studio Build Tools
set VCVARS=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat
if not exist "%VCVARS%" set VCVARS=C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat
if not exist "%VCVARS%" goto :error_msvc
echo Setting up MSVC environment...
call "%VCVARS%"
set DISTUTILS_USE_SDK=1

:: Setup CUDA 12.9
set CUDA_HOME=C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.9
if not exist "%CUDA_HOME%" goto :error_cuda
set PATH=%CUDA_HOME%\bin;%PATH%
echo Using CUDA: %CUDA_HOME%

:: Uninstall old versions first
pip uninstall custom_rasterizer -y 2>nul
pip uninstall differentiable_renderer -y 2>nul

cd hy3dgen\texgen\custom_rasterizer
pip install . --no-build-isolation
cd ..\..\..

cd hy3dgen\texgen\differentiable_renderer
pip install . --no-build-isolation
cd ..\..\..

:: 7. Additional dependencies
echo.
echo [7/8] Installing additional dependencies...
pip install gradio spaces

:: 8. Install compatible transformers/diffusers versions
echo.
echo [8/8] Fixing transformers/diffusers compatibility...
pip install "transformers>=4.46.0"

echo.
echo ========================================
echo  Installation Complete!
echo ========================================
echo.
echo  Run: run_hunyuan2.bat
echo  Conda env: %CONDA_ENV%
echo  Install path: %INSTALL_DIR%
echo.
pause
exit /b 0

:error_clone
echo [ERROR] git clone failed
pause
exit /b 1

:error_conda
echo [ERROR] conda environment creation failed
pause
exit /b 1

:error_pytorch
echo [ERROR] PyTorch installation failed
pause
exit /b 1

:error_deps
echo [ERROR] dependency installation failed
pause
exit /b 1

:error_msvc
echo.
echo [ERROR] Visual Studio Build Tools not found.
echo Please install "C++ Desktop Development" workload from:
echo   https://visualstudio.microsoft.com/visual-cpp-build-tools/
pause
exit /b 1

:error_cuda
echo.
echo [ERROR] CUDA 12.9 is not installed.
echo CUDA 12.9 Toolkit is required for building texture modules.
echo.
echo Please install CUDA Toolkit 12.9 from:
echo   https://developer.nvidia.com/cuda-12-9-0-download-archive
echo.
echo Install path should be:
echo   C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.9
pause
exit /b 1
