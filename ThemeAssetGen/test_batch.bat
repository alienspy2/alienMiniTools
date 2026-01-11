@echo off
chcp 65001 >nul
echo ========================================
echo  ThemeAssetGen Batch Test
echo ========================================
echo.

cd /d "%~dp0"

:: Activate conda environment
call conda activate themegen 2>nul
if errorlevel 1 (
    echo [ERROR] themegen 환경을 찾을 수 없습니다.
    echo run_server.bat를 먼저 실행해주세요.
    pause
    exit /b 1
)

if "%1"=="" goto :menu
python test_batch.py %*
pause
exit /b 0

:menu
echo 사용법:
echo   test_batch.bat --check          서비스 상태 확인
echo   test_batch.bat --list           카탈로그 목록
echo   test_batch.bat --assets ^<id^>    에셋 목록
echo   test_batch.bat --single ^<id^>    단일 에셋 생성
echo   test_batch.bat --batch ^<id^>     배치 생성 (1개)
echo   test_batch.bat --batch ^<id^> --limit 3   배치 생성 (3개)
echo.
echo 예시:
echo   test_batch.bat --check
echo.

python test_batch.py
pause
