param(
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$needle = $ProjectRoot.ToLowerInvariant()

$processes = Get-CimInstance Win32_Process | Where-Object {
    $_.CommandLine -and
    $_.CommandLine.ToLowerInvariant().Contains($needle) -and
    ($_.CommandLine -like "*reify-server.dll*" -or
     $_.CommandLine -like "*reify-server.exe*" -or
     ($_.CommandLine -like "*dotnet*run*" -and
      $_.CommandLine -like "*Reify.Server.csproj*"))
}

if (-not $processes) {
    Write-Host "No reify server processes found for $ProjectRoot"
    exit 0
}

foreach ($process in $processes) {
    Write-Host "PID $($process.ProcessId): $($process.CommandLine)"
}

if ($WhatIf) {
    Write-Host ""
    Write-Host "WhatIf set; no processes stopped."
    Write-Host "Run without -WhatIf before rebuilding an in-place published server."
    exit 0
}

foreach ($process in $processes) {
    try {
        Stop-Process -Id $process.ProcessId -Force
        Write-Host "Stopped PID $($process.ProcessId)"
    } catch {
        Write-Warning "Could not stop PID $($process.ProcessId): $_"
    }
}
