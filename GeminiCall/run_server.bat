@echo off
title GeminiCall Server
cd /d "%~dp0"
echo Starting GeminiCall Server...
uv run python main.py %* --verbose
pause
