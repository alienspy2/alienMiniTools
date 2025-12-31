@echo off
setlocal

set ROOT=%~dp0

dotnet publish "%ROOT%src\RemoteKM.Server\RemoteKM.Server.csproj" -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -o "%ROOT%publish\server"
if errorlevel 1 exit /b 1

dotnet publish "%ROOT%src\RemoteKM.Client\RemoteKM.Client.csproj" -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -o "%ROOT%publish\client"
if errorlevel 1 exit /b 1

set /p COPY_APPS=Copy published EXEs to C:\apps? (y/n): 
if /i "%COPY_APPS%"=="y" goto copy_apps
if /i "%COPY_APPS%"=="n" goto done_copy
echo Invalid choice. Skipping copy.
goto done_copy

:copy_apps
set DEST=C:\apps
if not exist "%DEST%" mkdir "%DEST%"

copy /y "%ROOT%publish\server\RemoteKM.Server.exe" "%DEST%\RemoteKM.Server.exe"
if errorlevel 1 exit /b 1

copy /y "%ROOT%publish\client\RemoteKM.Client.exe" "%DEST%\RemoteKM.Client.exe"
if errorlevel 1 exit /b 1

:done_copy

echo Publish complete.

pause
