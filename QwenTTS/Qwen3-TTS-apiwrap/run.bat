@echo off
REM Qwen3-TTS API Wrapper - Run Script
cd /d "%~dp0"
uv run python app.py %*
pause
