@echo off
setlocal

set ROOT=%~dp0

dotnet publish "%ROOT%src\RemoteKM.Server\RemoteKM.Server.csproj" -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -o "%ROOT%publish\server"
if errorlevel 1 exit /b 1

dotnet publish "%ROOT%src\RemoteKM.Client\RemoteKM.Client.csproj" -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -o "%ROOT%publish\client"
if errorlevel 1 exit /b 1

echo Publish complete.

pause
