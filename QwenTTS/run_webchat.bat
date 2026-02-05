@echo off
cd /d "%~dp0Qwen3-TTS-apiwrap"
echo Starting Web Chat UI (Port 23007)...
echo.
uv run web_ui.py --verbose
pause
