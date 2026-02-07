@echo off
cd /d "%~dp0Qwen3-TTS"
uv run qwen-tts-demo Qwen/Qwen3-TTS-12Hz-1.7B-CustomVoice --ip 0.0.0.0 --port 23015 --no-flash-attn
pause
