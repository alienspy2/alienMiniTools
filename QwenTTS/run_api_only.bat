@echo off
REM Qwen3-TTS API Wrapper Only - Run Script
REM Assumes TTS server is already running on port 23005

cd /d "%~dp0"

echo Starting API Wrapper...
cd Qwen3-TTS-apiwrap
uv run python app.py %*
pause
