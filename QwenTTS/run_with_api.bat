@echo off
REM Run Qwen3-TTS server and API wrapper together
REM TTS server on port 23005, API wrapper on port 23006

cd /d "%~dp0"

echo Starting Qwen3-TTS Server...
cd Qwen3-TTS
start "Qwen3-TTS Server" cmd /c "uv run qwen-tts-demo Qwen/Qwen3-TTS-12Hz-1.7B-CustomVoice --ip 0.0.0.0 --port 23005 --no-flash-attn"
cd ..

REM Wait for TTS server to initialize
echo Waiting for TTS server to start (30 seconds)...
timeout /t 30 /nobreak > nul

echo Starting API Wrapper...
cd Qwen3-TTS-apiwrap
uv run python app.py %*
pause
