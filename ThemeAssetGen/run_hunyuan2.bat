@echo off
chcp 65001 >nul
echo ========================================
echo  Hunyuan3D-2.0 [Stable Version]
echo ========================================
echo.

set HUNYUAN_DIR=%~dp0hunyuan2


:: Check installation
if not exist "%HUNYUAN_DIR%" goto :not_installed



cd /d "%HUNYUAN_DIR%"

:: Run Gradio App - v2.0 Version
echo.
echo ========================================
echo  Model: Hunyuan3D-DiT-v2-0 [Shape]
echo  Model: Hunyuan3D-Paint-v2-0 [Texture]
echo  VRAM: 16GB required
echo ========================================
echo.
echo Starting Gradio server...
echo Access: http://0.0.0.0:7860
echo Press Ctrl+C to stop
echo.

uv run gradio_app.py --model_path tencent/Hunyuan3D-2 --subfolder hunyuan3d-dit-v2-0 --texgen_model_path tencent/Hunyuan3D-2 --low_vram_mode --port 7860 --host 0.0.0.0

pause
exit /b 0

:not_installed
echo [ERROR] Hunyuan3D is not installed.
echo Please run install_hunyuan2.bat first.
pause
exit /b 1


