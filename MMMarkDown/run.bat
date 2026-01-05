@echo off
setlocal

call conda activate mmm
start "" pythonw mmm_app.py --tray --state "%USERPROFILE%\Nextcloud\mmm\mindmap.mmm"
