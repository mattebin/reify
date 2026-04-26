param(
    [ValidateSet("claude", "cursor", "vscode", "windsurf")]
    [string]$Client = "claude",
    [string]$OutputPath = "",
    [switch]$Dev,
    [switch]$Preview
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$ConfigDir = Join-Path $ProjectRoot "client-config"

$templates = @{
    claude = if ($Dev) { "claude-code.mcp.json" } else { "claude-code.mcp.published.json" }
    cursor = "cursor.mcp.json"
    vscode = "vscode-mcp.json"
    windsurf = "windsurf.mcp.json"
}

if (-not $OutputPath) {
    switch ($Client) {
        "claude" {
            if (-not $env:APPDATA) { throw "APPDATA is not set; pass -OutputPath." }
            $OutputPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
        }
        "cursor" {
            if (-not $env:USERPROFILE) { throw "USERPROFILE is not set; pass -OutputPath." }
            $OutputPath = Join-Path $env:USERPROFILE ".cursor\mcp.json"
        }
        default {
            $name = $templates[$Client]
            $OutputPath = Join-Path $ProjectRoot ".scratch\client-config\$name"
        }
    }
}

$templatePath = Join-Path $ConfigDir $templates[$Client]
$escapedRoot = $ProjectRoot.Replace("\", "\\")
$content = (Get-Content -LiteralPath $templatePath -Raw) -replace "<PATH_TO_REIFY>", $escapedRoot

Write-Host "Client:   $Client"
Write-Host "Template: $templatePath"
Write-Host "Output:   $OutputPath"

if ($Preview) {
    Write-Host ""
    Write-Host $content
    exit 0
}

$outDir = Split-Path -Parent $OutputPath
if ($outDir) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

Set-Content -LiteralPath $OutputPath -Value $content -Encoding UTF8

Write-Host ""
Write-Host "Wrote MCP config."
if (-not $Dev) {
    Write-Host "Build/update the published server with:"
    Write-Host "  powershell -ExecutionPolicy Bypass -File scripts\build-server.ps1 -Publish"
}
