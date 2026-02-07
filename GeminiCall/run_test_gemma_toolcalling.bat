@echo off
title Test Gemma Tool Calling
cd /d "%~dp0"
echo Running test_gemma_toolcalling.py with uv...
uv run --with google-genai --with "mcp[cli]" python test_gemma_toolcalling.py %*
if errorlevel 1 (
    echo.
    echo Execution failed.
    pause
    exit /b 1
)
pause
