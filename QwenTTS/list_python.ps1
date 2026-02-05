$procs = Get-CimInstance Win32_Process | Select-Object ProcessId, CommandLine
$procs | Where-Object { $_.CommandLine -like "*python*" } | Format-Table -AutoSize
