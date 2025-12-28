# Kill MMMarkDown processes by matching mmm_app.py in the command line.
$targets = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*mmm_app.py*' }
if (-not $targets) {
    Write-Host 'No mmm_app.py processes found.'
    exit 0
}
$pids = $targets.ProcessId
Write-Host ('Stopping: ' + ($pids -join ', '))
Stop-Process -Id $pids -Force
