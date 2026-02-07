@echo off
title Test Gemma
cd /d "%~dp0"
echo Running test_gemma.py with uv...
uv run --with google-genai python test_gemma.py
if errorlevel 1 (
    echo.
    echo Execution failed.
    pause
    exit /b 1
)
pause
