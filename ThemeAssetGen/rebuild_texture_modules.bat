@echo off
chcp 65001 >nul
echo ========================================
echo  Rebuild Texture Modules for PyTorch 2.6
echo ========================================
echo.

set HUNYUAN_DIR=%~dp0hunyuan2
set CONDA_ENV=hunyuan2

:: Activate conda environment
call conda activate %CONDA_ENV%
if errorlevel 1 goto :error_conda

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

echo.
echo [1/2] Rebuilding custom_rasterizer...
cd /d "%HUNYUAN_DIR%\hy3dgen\texgen\custom_rasterizer"
pip uninstall custom_rasterizer -y 2>nul
pip install . --no-build-isolation
if errorlevel 1 goto :error_build

echo.
echo [2/2] Rebuilding differentiable_renderer...
cd /d "%HUNYUAN_DIR%\hy3dgen\texgen\differentiable_renderer"
pip uninstall differentiable_renderer -y 2>nul
pip install . --no-build-isolation
if errorlevel 1 goto :error_build

echo.
echo ========================================
echo  Rebuild Complete!
echo ========================================
pause
exit /b 0

:error_conda
echo [ERROR] hunyuan2 conda environment not found
pause
exit /b 1

:error_msvc
echo [ERROR] Visual Studio Build Tools not found
pause
exit /b 1

:error_cuda
echo [ERROR] CUDA 12.9 not found
pause
exit /b 1

:error_build
echo [ERROR] Build failed
pause
exit /b 1
