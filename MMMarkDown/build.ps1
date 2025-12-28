# Build a single-file portable exe into .\buildOutput
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'buildOutput'
$work = Join-Path $dist 'build'
$spec = Join-Path $dist 'spec'

if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

Get-Process -Name MMMarkDown -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

conda run -n n8n python -m pip install --upgrade pyinstaller

$exeName = 'MMMarkDown'
$entry = Join-Path $root 'mmm_app.py'
$icon = Join-Path $root 'assets\\mmm_app.ico'
$static = Join-Path $root 'static'

conda run -n n8n python -m PyInstaller --noconsole --onefile --name $exeName `
    --icon $icon `
    --add-data "$static;static" `
    --hidden-import=pystray._win32 `
    --distpath $dist --workpath $work --specpath $spec `
    --clean $entry

# Keep output tidy: remove build and spec folders
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
if (Test-Path $spec) { Remove-Item -Recurse -Force $spec }

Write-Host "Build complete: $dist\\$exeName.exe"
