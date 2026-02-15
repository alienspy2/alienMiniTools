@echo off
setlocal

set PROJECT=src\IronRose.Demo\IronRose.Demo.csproj
set OUTPUT=publish

echo [IronRose] Publishing single-file executable for win-x64...
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o "%OUTPUT%"

if %ERRORLEVEL% neq 0 (
    echo [IronRose] Publish failed.
    exit /b 1
)

echo [IronRose] Done. Output: %OUTPUT%\
endlocal
