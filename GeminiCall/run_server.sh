#!/bin/bash
cd "$(dirname "$0")"
echo "Starting GeminiCall Server..."
uv run python main.py "$@" --verbose
