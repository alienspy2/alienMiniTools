@echo off
title Test Gemini TTS
cd /d "%~dp0"
echo Running test_gemini_tts.py with uv...
uv run --with google-genai --with soundfile --with numpy python test_gemini_tts.py
if errorlevel 1 (
    echo.
    echo Execution failed.
    pause
    exit /b 1
)
pause
