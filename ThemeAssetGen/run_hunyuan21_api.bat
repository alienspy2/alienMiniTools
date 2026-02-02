@echo off
chcp 65001 >nul
echo ========================================
echo  Hunyuan3D-2.1 API Server
echo ========================================
echo.

set HUNYUAN_DIR=%~dp0hunyuan21
set CONDA_ENV=hunyuan21

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
echo  Model: Hunyuan3D-DiT-v2-1 [Shape]
echo  Model: Hunyuan3D-Paint-v2-1 [Texture]
echo  Port: 8081
echo ========================================
echo.
echo Starting API server...
echo Endpoint: http://localhost:8081
echo Press Ctrl+C to stop
echo.

python api_server.py --host 0.0.0.0 --port 8081 --model_path tencent/Hunyuan3D-2.1 --tex_model_path tencent/Hunyuan3D-2.1 --enable_tex

pause
exit /b 0

:not_installed
echo [ERROR] Hunyuan3D-2.1 is not installed.
echo Please run install_hunyuan21.bat first.
pause
exit /b 1

:conda_error
echo [ERROR] Failed to activate conda environment.
echo Check if environment exists: conda env list
pause
exit /b 1
