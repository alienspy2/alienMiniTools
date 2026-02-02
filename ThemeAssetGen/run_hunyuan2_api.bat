@echo off
chcp 65001 >nul
echo ========================================
echo  Hunyuan3D-2 API Server
echo ========================================
echo.

set HUNYUAN_DIR=%~dp0hunyuan2
set CONDA_ENV=hunyuan2

:: Check installation
if not exist "%HUNYUAN_DIR%" goto :not_installed

:: Activate conda environment
echo Activating conda environment [%CONDA_ENV%]...
call conda activate %CONDA_ENV%
if errorlevel 1 goto :conda_error

cd /d "%HUNYUAN_DIR%"

:: Run API Server
echo.
echo ========================================
echo  REST API Server (for ThemeAssetGen)
echo  Model: Hunyuan3D-DiT-v2-0 [Shape]
echo  Model: Hunyuan3D-Paint-v2-0 [Texture]
echo  VRAM: 16GB required
echo  Port: 8080
echo ========================================
echo.
echo Starting API server...
echo API: http://localhost:8080/generate
echo Press Ctrl+C to stop
echo.

python api_server.py --model_path tencent/Hunyuan3D-2 --subfolder hunyuan3d-dit-v2-0 --tex_model_path tencent/Hunyuan3D-2 --port 8080 --enable_tex

pause
exit /b 0

:not_installed
echo [ERROR] Hunyuan3D is not installed.
echo Please run install_hunyuan2.bat first.
pause
exit /b 1

:conda_error
echo [ERROR] Failed to activate conda environment.
echo Check if environment exists: conda env list
pause
exit /b 1
