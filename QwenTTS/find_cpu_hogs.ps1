$Sample1 = Get-Process | Select-Object Name, Id, CPU
Start-Sleep -Seconds 1
$Sample2 = Get-Process | Select-Object Name, Id, CPU

$results = foreach ($p1 in $Sample1) {
    $p2 = $Sample2 | Where-Object { $_.Id -eq $p1.Id }
    if ($p2) {
        $CpuDelta = $p2.CPU - $p1.CPU
        [PSCustomObject]@{
            Name     = $p1.Name
            Id       = $p1.Id
            CpuDelta = $CpuDelta
        }
    }
}

$top = $results | Sort-Object CpuDelta -Descending | Select-Object -First 10

foreach ($t in $top) {
    if ($t.CpuDelta -gt 0) {
        $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId = $($t.Id)").CommandLine
        Write-Host "Name: $($t.Name) (PID: $($t.Id)) CPU Delta: $($t.CpuDelta)"
        Write-Host "CommandLine: $cmd"
        Write-Host "------------------------------------"
    }
}
