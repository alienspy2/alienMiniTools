@echo off
chcp 65001 >nul
echo ========================================
echo  ThemeAssetGen Server
echo ========================================
echo.

cd /d "%~dp0"

:: 파라미터 확인 (--debug)
set DEBUG_MODE=
if "%1"=="--debug" set DEBUG_MODE=--debug
if "%1"=="-d" set DEBUG_MODE=--debug

:: Check if conda environment exists
call conda activate themegen 2>nul
if errorlevel 1 goto :create_env

goto :start_server

:create_env
echo [INFO] Creating conda environment [themegen]...
call conda create -n themegen python=3.11 -y
if errorlevel 1 goto :conda_error

call conda activate themegen
if errorlevel 1 goto :conda_error

echo [INFO] Installing dependencies...
pip install -r requirements.txt
if errorlevel 1 goto :pip_error

:start_server
echo.
echo Starting server...
echo URL: http://localhost:8000
echo API docs: http://localhost:8000/docs
echo Logs: %~dp0logs\
if defined DEBUG_MODE echo [DEBUG MODE ENABLED]
echo ========================================
echo.

python run.py %DEBUG_MODE%

pause
exit /b 0

:conda_error
echo [ERROR] Failed to create conda environment
pause
exit /b 1

:pip_error
echo [ERROR] Failed to install dependencies
pause
exit /b 1
