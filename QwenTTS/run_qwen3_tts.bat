@echo off
title Qwen3-TTS Server (Port 23015)
cd /d "%~dp0"
echo Starting Qwen3-TTS Server (Port 23015)...
echo.
REM uv run --project Qwen3-TTS/pyproject.toml qwen-tts-demo Qwen/Qwen3-TTS-12Hz-1.7B-CustomVoice --ip 0.0.0.0 --port 23015 --no-flash-attn
uv run --project Qwen3-TTS/pyproject.toml qwen-tts-demo Qwen/Qwen3-TTS-12Hz-0.6B-CustomVoice --ip 0.0.0.0 --port 23015 --no-flash-attn
pause
