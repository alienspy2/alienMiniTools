@echo off
cd /d "%~dp0"
echo Starting Qwen3-TTS Server (Port 23005)...
echo.
uv run --project Qwen3-TTS/pyproject.toml qwen-tts-demo Qwen/Qwen3-TTS-12Hz-1.7B-CustomVoice --ip 0.0.0.0 --port 23005 --no-flash-attn
pause
