$proc = Get-CimInstance Win32_Process -Filter "ProcessId = 3384"
if ($proc) {
    Write-Host "CommandLine: $($proc.CommandLine)"
    Write-Host "ExecutablePath: $($proc.ExecutablePath)"
    # Working directory is not directly in Win32_Process for all OS versions, but let's try
    Write-Host "WorkingDir: $($proc.WorkingDirectory)"
}
else {
    Write-Host "Process not found."
}
