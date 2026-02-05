@echo off
setlocal

cd /d "%~dp0"

echo ========================================================
echo  Starting QwenTTS Ecosystem
echo ========================================================
echo.

echo [1/3] Launching Qwen3-TTS Server (Port 23005)...
start "QwenTTS-Server" run_qwen3_tts.bat

echo Waiting for TTS server to initialize...
timeout /t 10

echo [2/3] Launching MCP Server (Port 23006)...
start "QwenTTS-MCP" run_mcp.bat

echo [3/3] Launching Web Chat UI (Port 23007)...
start "QwenTTS-WebChat" run_webchat.bat

echo.
echo ========================================================
echo  All services launched in separate windows.
echo.
echo  - TTS Server: http://localhost:23005
echo  - MCP Server: http://localhost:23006/mcp
echo  - Web Chat UI: http://localhost:23007
echo ========================================================
echo.
pause
