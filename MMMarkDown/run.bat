@echo off
setlocal

call conda activate n8n
python app.py --state "%USERPROFILE%\Nextcloud\mmm\mindmap.mmm"
