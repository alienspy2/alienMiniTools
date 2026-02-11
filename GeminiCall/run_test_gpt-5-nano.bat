@echo off
title GPT-5-nano Test Client
cd /d "%~dp0"
echo Starting GPT-5-nano Test Client...
uv run python test_gpt-5-nano.py %*
pause
