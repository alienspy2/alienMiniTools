@echo off
chcp 65001 >nul
setlocal

set LOGS_DIR=%~dp0logs

if not exist "%LOGS_DIR%" (
    echo [INFO] 로그 디렉토리가 없습니다: %LOGS_DIR%
    echo [INFO] 서버를 먼저 실행해주세요.
    pause
    exit /b 1
)

echo ========================================
echo  ThemeAssetGen Log Viewer
echo ========================================
echo.
echo [1] 최신 서버 로그 (tail)
echo [2] 에러 로그만 보기
echo [3] 로그 디렉토리 열기
echo [4] 로그 파일 삭제
echo.
set /p CHOICE="선택 [1-4]: "

if "%CHOICE%"=="1" goto :view_server
if "%CHOICE%"=="2" goto :view_error
if "%CHOICE%"=="3" goto :open_dir
if "%CHOICE%"=="4" goto :delete_logs

echo 잘못된 선택입니다.
pause
exit /b 1

:view_server
echo.
echo === 최신 서버 로그 (Ctrl+C로 종료) ===
echo.
:: 가장 최신 server 로그 파일 찾기
for /f "delims=" %%f in ('dir /b /o-d "%LOGS_DIR%\server_*.log" 2^>nul') do (
    set "LATEST_LOG=%LOGS_DIR%\%%f"
    goto :found_server
)
echo 서버 로그 파일이 없습니다.
pause
exit /b 1

:found_server
echo 파일: %LATEST_LOG%
echo.
powershell -Command "Get-Content -Path '%LATEST_LOG%' -Tail 50 -Wait"
exit /b 0

:view_error
echo.
echo === 에러 로그 ===
echo.
for /f "delims=" %%f in ('dir /b /o-d "%LOGS_DIR%\error_*.log" 2^>nul') do (
    set "LATEST_ERROR=%LOGS_DIR%\%%f"
    goto :found_error
)
echo 에러 로그 파일이 없습니다.
pause
exit /b 1

:found_error
echo 파일: %LATEST_ERROR%
echo.
type "%LATEST_ERROR%"
echo.
pause
exit /b 0

:open_dir
explorer "%LOGS_DIR%"
exit /b 0

:delete_logs
echo.
set /p CONFIRM="정말 모든 로그를 삭제하시겠습니까? [y/n]: "
if /i "%CONFIRM%"=="y" (
    del /q "%LOGS_DIR%\*.log" 2>nul
    echo 로그 파일이 삭제되었습니다.
) else (
    echo 취소되었습니다.
)
pause
exit /b 0
