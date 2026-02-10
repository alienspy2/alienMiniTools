@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Usage: deploy.bat [instance-name] [port]
    echo Example: deploy.bat api-8000 8000
    exit /b 1
)

if "%~2"=="" (
    echo Usage: deploy.bat [instance-name] [port]
    echo Example: deploy.bat api-8000 8000
    exit /b 1
)

set INSTANCE_NAME=%~1
set HTTP_PORT=%~2
set DEPLOY_DIR=deploy\%INSTANCE_NAME%

echo ========================================
echo GeminiCall Deployment Script
echo ========================================
echo Target: %DEPLOY_DIR%
echo Port: %HTTP_PORT%
echo ========================================

if exist "%DEPLOY_DIR%" (
    echo WARNING: Directory already exists: %DEPLOY_DIR%
    choice /C YN /M "Overwrite"
    if errorlevel 2 exit /b 1
    echo Cleaning existing directory...
    rd /s /q "%DEPLOY_DIR%"
)

echo Creating directory: %DEPLOY_DIR%
mkdir "%DEPLOY_DIR%"

echo.
echo Copying core Python files...
copy main.py "%DEPLOY_DIR%\" >nul
copy config_loader.py "%DEPLOY_DIR%\" >nul
copy queue_manager.py "%DEPLOY_DIR%\" >nul
copy rate_limiter.py "%DEPLOY_DIR%\" >nul
copy genai_service.py "%DEPLOY_DIR%\" >nul
copy mcp_service.py "%DEPLOY_DIR%\" >nul
copy schema.py "%DEPLOY_DIR%\" >nul

echo Copying dependency files...
copy pyproject.toml "%DEPLOY_DIR%\" >nul
copy uv.lock "%DEPLOY_DIR%\" >nul

echo Copying and configuring config.json...
if not exist "config-example.json" (
    echo ERROR: config-example.json not found.
    exit /b 1
)
copy config-example.json "%DEPLOY_DIR%\config.json" >nul

echo Setting http_port to %HTTP_PORT%...
powershell -Command "$content = Get-Content '%DEPLOY_DIR%\config.json' -Raw; $content = $content -replace '\"http_port\": \d+', '\"http_port\": %HTTP_PORT%'; Set-Content '%DEPLOY_DIR%\config.json' -Value $content -NoNewline"

echo Copying run scripts...
copy run_server.bat "%DEPLOY_DIR%\" >nul
powershell -Command "(Get-Content '%DEPLOY_DIR%\run_server.bat') -replace 'title GeminiCall Server', 'title %INSTANCE_NAME%' | Set-Content '%DEPLOY_DIR%\run_server.bat'"

if exist "mcp.json" (
    echo Copying MCP configuration...
    copy mcp.json "%DEPLOY_DIR%\" >nul
)

echo.
echo ========================================
echo Deployment completed successfully!
echo ========================================
echo Location: %DEPLOY_DIR%
echo.
echo Next steps:
echo 1. cd %DEPLOY_DIR%
echo 2. Edit config.json - Set your API key!
echo 3. run_server.bat
echo.
echo IMPORTANT: API key has been cleared for security.
echo Please update config.json with your actual API key.
echo ========================================
pause
