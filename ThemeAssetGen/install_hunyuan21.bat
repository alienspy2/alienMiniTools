@echo off
chcp 65001 >nul
echo ========================================
echo  Hunyuan3D-2.1 Install Script
echo ========================================
echo.

set INSTALL_DIR=%~dp0hunyuan21
set CONDA_ENV=hunyuan21

:: conda init
call conda activate base

:: Check existing directory
if not exist "%INSTALL_DIR%" goto :do_clone

echo [!] %INSTALL_DIR% already exists.
set /p OVERWRITE="Overwrite? [y/n]: "
if /i "%OVERWRITE%"=="y" goto :remove_dir
echo Installation cancelled.
pause
exit /b 1

:remove_dir
rmdir /s /q "%INSTALL_DIR%"

:do_clone
:: 1. Clone repository
echo.
echo [1/9] Cloning Hunyuan3D-2.1 repository...
git clone https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1 "%INSTALL_DIR%"
if errorlevel 1 goto :error_clone

cd /d "%INSTALL_DIR%"

:: 2. Create conda environment
echo.
echo [2/9] Creating conda environment [%CONDA_ENV%]...
call conda create -n %CONDA_ENV% python=3.10 -y
if errorlevel 1 goto :error_conda

:: 3. Activate environment
echo.
echo [3/9] Activating conda environment...
call conda activate %CONDA_ENV%

:: 4. Install PyTorch [CUDA 12.4]
echo.
echo [4/9] Installing PyTorch 2.5.1 [CUDA 12.4]...
pip install torch==2.5.1 torchvision==0.20.1 torchaudio==2.5.1 --index-url https://download.pytorch.org/whl/cu124
if errorlevel 1 goto :error_pytorch

:: 5. Install dependencies (excluding bpy which requires Blender)
echo.
echo [5/9] Installing dependencies...
echo [!] Skipping bpy (Blender Python) - not available via pip
findstr /v /i "^bpy" requirements.txt > requirements_no_bpy.txt
pip install -r requirements_no_bpy.txt
del requirements_no_bpy.txt

:: 6. Install custom rasterizer
echo.
echo [6/9] Installing custom rasterizer...
cd hy3dpaint\custom_rasterizer
pip install -e . --no-build-isolation
cd ..\..
if errorlevel 1 goto :error_rasterizer

:: 7. Install differentiable renderer
echo.
echo [7/9] Installing differentiable renderer...
cd hy3dpaint\DifferentiableRenderer
call compile_mesh_painter.bat 2>nul || bash compile_mesh_painter.sh
cd ..\..

:: 8. Download RealESRGAN model
echo.
echo [8/9] Downloading RealESRGAN model...
if not exist "hy3dpaint\ckpt" mkdir hy3dpaint\ckpt
curl -L -o hy3dpaint\ckpt\RealESRGAN_x4plus.pth https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth

:: 9. Additional dependencies
echo.
echo [9/9] Installing additional dependencies...
pip install gradio spaces

echo.
echo ========================================
echo  Installation Complete!
echo ========================================
echo.
echo  Run: run_hunyuan21.bat
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

:error_rasterizer
echo [ERROR] custom rasterizer installation failed
pause
exit /b 1
