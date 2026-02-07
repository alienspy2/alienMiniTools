@echo off
title Test JSON-RPC
cd /d "%~dp0"
echo Running test_jsonrpc.py with uv (requests)...
uv run --with requests python test_jsonrpc.py
if errorlevel 1 (
    echo.
    echo Execution failed.
    pause
    exit /b 1
)
pause
