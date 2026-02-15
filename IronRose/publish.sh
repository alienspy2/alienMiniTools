#!/usr/bin/env bash
set -euo pipefail

PROJECT="src/IronRose.Demo/IronRose.Demo.csproj"
OUTPUT="publish"

echo "[IronRose] Publishing for linux-x64..."
dotnet publish "$PROJECT" -c Release -r linux-x64 -o "$OUTPUT"

echo "[IronRose] Done. Output: $OUTPUT/"
