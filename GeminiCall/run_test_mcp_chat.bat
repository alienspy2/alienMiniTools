@echo off
cd /d "%~dp0"
uv run python test_mcp_chat.py %*
pause
