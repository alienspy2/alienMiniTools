@echo off
chcp 65001 >nul
echo ========================================
echo  Hunyuan3D-2 Install Script
echo ========================================
echo.

set INSTALL_DIR=%~dp0hunyuan2
set CONDA_ENV=hunyuan2

:: Find conda installation (miniforge3 / miniconda3 / anaconda3)
set CONDA_ROOT=
for %%P in (miniforge3 miniconda3 anaconda3) do (
    if exist "%USERPROFILE%\%%P\condabin\conda.bat" set CONDA_ROOT=%USERPROFILE%\%%P
)
if "%CONDA_ROOT%"=="" (
    echo [ERROR] Conda not found. Install miniforge3 or miniconda3.
    pause
    exit /b 1
)
echo Using conda: %CONDA_ROOT%
call "%CONDA_ROOT%\condabin\conda.bat" activate base

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
call "%CONDA_ROOT%\condabin\conda.bat" activate %CONDA_ENV% 2>nul
if errorlevel 1 (
    call "%CONDA_ROOT%\condabin\conda.bat" create -n %CONDA_ENV% python=3.10 -y
    if errorlevel 1 goto :error_conda
)

:: 3. Activate environment
echo.
echo [3/8] Activating conda environment...
call "%CONDA_ROOT%\condabin\conda.bat" activate %CONDA_ENV%

:: 4. Install dependencies
echo.
echo [4/8] Installing dependencies...
set TORCH_USE_CUDA_DSA=1
pip install -r requirements.txt
pip install -e .
if errorlevel 1 goto :error_deps

:: 5. Install PyTorch [CUDA 12.8]
echo.
echo [5/8] Installing PyTorch Nightly [CUDA 12.8]...
pip install --pre torch --index-url https://download.pytorch.org/whl/nightly/cu128 --force-reinstall
if errorlevel 1 goto :error_pytorch
pip install --pre torchvision --index-url https://download.pytorch.org/whl/nightly/cu128 --force-reinstall --no-deps

:: 6. Install texture modules (requires CUDA 12.0 + MSVC for PyTorch compatibility)
echo.
echo [6/8] Installing texture modules...

:: Setup Visual Studio Build Tools
:: PATH 최소화 (Windows 필수 + 활성화된 conda 환경만 유지, vcvars64 오류 방지)
set PATH=C:\Windows\system32;C:\Windows;C:\Windows\System32\Wbem;%CONDA_PREFIX%;%CONDA_PREFIX%\Scripts;%CONDA_PREFIX%\Library\bin
set VCVARS=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat
if not exist "%VCVARS%" set VCVARS=C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat
if not exist "%VCVARS%" goto :error_msvc
:: Force English output from MSVC (must be set BEFORE vcvars call)
set VSLANG=1033
echo Setting up MSVC environment...
call "%VCVARS%"
set DISTUTILS_USE_SDK=1
set TORCH_USE_CUDA_DSA=1
set PYTHONIOENCODING=utf-8
set PYTHONLEGACYWINDOWSSTDIO=0

:: Find CUDA installation (v12.8+ required for latest MSVC)
set CUDA_HOME=
for %%V in (v12.8 v12.9 v12.6 v12.4) do (
    if exist "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\%%V\bin\nvcc.exe" set CUDA_HOME=C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\%%V
)
if "%CUDA_HOME%"=="" goto :error_cuda
set PATH=%CUDA_HOME%\bin;%PATH%
:: Allow newer MSVC versions (CUDA 12.0 doesn't recognize VS 2022 17.10+)
set NVCC_PREPEND_FLAGS=-allow-unsupported-compiler
set TORCH_CUDA_ARCH_LIST=8.6
set "CUDA_HOME=%CUDA_HOME%"
set "CUDA_PATH=%CUDA_HOME%"
echo Using CUDA: %CUDA_HOME%

:: Uninstall old versions first
pip uninstall custom_rasterizer -y 2>nul
pip uninstall differentiable_renderer -y 2>nul

cd hy3dgen\texgen\custom_rasterizer
echo Installing custom_rasterizer...
pip install . --no-build-isolation > custom_rasterizer_buildlog.txt 2>&1
if errorlevel 1 (
    type custom_rasterizer_buildlog.txt
    goto :error_texture
)
cd ..\..\..

cd hy3dgen\texgen\differentiable_renderer
echo Installing differentiable_renderer...
pip install . --no-build-isolation > differentiable_renderer_buildlog.txt 2>&1
if errorlevel 1 (
    type differentiable_renderer_buildlog.txt
    goto :error_texture
)
cd ..\..\..

:: 7. Additional dependencies
echo.
echo [7/8] Installing additional dependencies...
pip install gradio spaces

:: 8. Install compatible transformers/diffusers versions
echo.
echo [8/8] Fixing transformers/diffusers compatibility...
pip install "transformers>=4.48.0" diffusers accelerate --no-deps

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

:error_texture
echo.
echo [ERROR] Texture module installation failed.
echo Check the build output above for actual errors.
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
echo [ERROR] CUDA Toolkit not found.
echo CUDA 12.x is required for building texture modules.
echo.
echo Please install one of: CUDA 12.8 / 12.9 / 12.6 / 12.4 from:
echo   https://developer.nvidia.com/cuda-toolkit-archive
echo.
echo Install path should be:
echo   C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.x
pause
exit /b 1
