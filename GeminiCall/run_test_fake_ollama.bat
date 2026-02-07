@echo off
title Test Fake Ollama
cd /d "%~dp0"
echo Running test_fake_ollama.py with uv (requests)...
uv run --with requests python test_fake_ollama.py
if errorlevel 1 (
    echo.
    echo Execution failed.
    pause
    exit /b 1
)
pause
