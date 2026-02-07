@echo off
title Qwen3-TTS MCP Server (Port 23016)
cd /d "%~dp0Qwen3-TTS-apiwrap"
echo Starting MCP Server (Port 23016)...
echo.
uv run server.py --verbose --webhook-url "https://unintended-rheumatoidally-shalanda.ngrok-free.dev/webhook/03702c50-450b-46a1-add1-320915a6eb65"
pause
