param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "",
    [switch]$Publish,
    [string]$RuntimeIdentifier = "",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$Project = Join-Path $ProjectRoot "src\Server\Reify.Server.csproj"

if (-not $OutputDir -and $Publish) {
    $OutputDir = Join-Path $ProjectRoot "dist\reify-server"
} elseif (-not $OutputDir) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputDir = Join-Path $ProjectRoot ".scratch\server-build\$stamp"
} elseif (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $ProjectRoot $OutputDir
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$mode = if ($Publish) { "publish" } else { "build" }
$dotnetArgs = @(
    $mode,
    $Project,
    "-c", $Configuration,
    "-o", $OutputDir
)

if ($Publish) {
    $dotnetArgs += "-p:PublishSingleFile=true"

    if ($RuntimeIdentifier) {
        $dotnetArgs += @("-r", $RuntimeIdentifier)
    }

    if ($SelfContained) {
        $dotnetArgs += "--self-contained"
    } else {
        $dotnetArgs += "--no-self-contained"
    }
}

Write-Host "Project: $ProjectRoot"
Write-Host "Mode:    $mode"
Write-Host "Output:  $OutputDir"
Write-Host ""

& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Server build ready:"
Get-ChildItem -LiteralPath $OutputDir -Filter "reify-server.*" |
    Select-Object FullName, Length |
    Format-Table -AutoSize
