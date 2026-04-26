param(
    [string]$BridgeUrl = "http://127.0.0.1:17777/",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

function Invoke-BridgeTool {
    param(
        [string]$Tool,
        [object]$Args = @{}
    )

    $uri = ($BridgeUrl.TrimEnd("/") + "/tool")
    $body = @{
        tool = $Tool
        args = $Args
    } | ConvertTo-Json -Depth 20 -Compress

    Invoke-RestMethod -Method Post -Uri $uri -ContentType "application/json" -Body $body -TimeoutSec 15
}

function Section {
    param([string]$Name)
    Write-Host ""
    Write-Host "== $Name =="
}

Section "Project"
Write-Host $ProjectRoot

Section "dotnet"
& dotnet --version

Section "Running reify servers"
$needle = $ProjectRoot.ToLowerInvariant()
$servers = Get-CimInstance Win32_Process | Where-Object {
    $_.CommandLine -and
    $_.CommandLine.ToLowerInvariant().Contains($needle) -and
    ($_.CommandLine -like "*reify-server.dll*" -or
     $_.CommandLine -like "*Reify.Server.csproj*")
}
if ($servers) {
    $servers | Select-Object ProcessId, CreationDate, CommandLine | Format-List
} else {
    Write-Host "None found."
}

Section "Tool parity"
& python (Join-Path $ProjectRoot "tests\static\test_tool_parity.py")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipBuild) {
    Section "Scratch server build"
    $output = Join-Path $ProjectRoot ".scratch\doctor-build"
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ProjectRoot "scripts\build-server.ps1") -OutputDir $output
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Section "Unity bridge"
try {
    $ping = Invoke-BridgeTool -Tool "ping"
    if (-not $ping.ok) {
        throw "ping returned error: $($ping | ConvertTo-Json -Depth 10)"
    }
    $data = $ping.data
    Write-Host "ping ok"
    Write-Host "Unity:  $($data.unity_version)"
    Write-Host "Project: $($data.project_name)"
    Write-Host "Frame:   $($data.frame)"

    $health = Invoke-BridgeTool -Tool "reify-health"
    if ($health.ok) {
        Write-Host "health ok"
        Write-Host ($health.data | ConvertTo-Json -Depth 8)
    } else {
        Write-Warning "reify-health returned: $($health | ConvertTo-Json -Depth 8)"
    }
} catch {
    Write-Warning "Unity bridge check failed at $BridgeUrl : $_"
}

Section "Unsafe tool gates"
Write-Host "script-execute requires REIFY_ALLOW_SCRIPT_EXECUTE=1 in the Unity Editor environment before launch."
Write-Host "reflection-method-call requires REIFY_ALLOW_REFLECTION_CALL=1 in the Unity Editor environment before launch."

Write-Host ""
Write-Host "Doctor complete."
