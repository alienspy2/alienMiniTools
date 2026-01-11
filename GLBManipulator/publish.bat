@echo off
echo Publishing GLBManipulator...

dotnet publish GLBManipulator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish

echo Copying meshoptimizer.dll...
copy /Y libs\meshoptimizer.dll publish\

echo.
echo Done! Output: ./publish/GLBManipulator.exe
pause
