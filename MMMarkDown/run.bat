@echo off
setlocal

cd /d "%~dp0"
start "" uv run pythonw mmm_app.py --tray --state "%USERPROFILE%\Nextcloud\mmm\mindmap.mmm"
