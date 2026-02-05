@echo off
cd /d "%~dp0"

REM Activate virtual environment if exists
if exist ".venv\Scripts\activate.bat" (
    call .venv\Scripts\activate.bat
)

REM Run config editor
python edit_config.py

pause
