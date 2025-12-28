@echo off
setlocal

call conda activate n8n
start "" pythonw mmm_app.py --tray --state "%USERPROFILE%\Nextcloud\mmm\mindmap.mmm"
