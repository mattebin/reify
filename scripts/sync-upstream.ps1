<#
.SYNOPSIS
  Fetch both upstream remotes and report new commits since last sync.

.DESCRIPTION
  PowerShell equivalent of sync-upstream.sh. Windows-friendly.
  Updates refs for coplay/ and murzak/, tracks last-seen SHAs in
  .upstream-sync-state, prints diff summaries. No merges.

.EXAMPLE
  PS> .\scripts\sync-upstream.ps1
#>

$ErrorActionPreference = 'Stop'

Set-Location (Join-Path $PSScriptRoot '..')

$remotes = @('coplay', 'murzak')
$stateFile = '.upstream-sync-state'

# Verify remotes
$configuredRemotes = git remote
foreach ($remote in $remotes) {
    if ($configuredRemotes -notcontains $remote) {
        Write-Error "Remote '$remote' not configured. Add with:"
        switch ($remote) {
            'coplay' { Write-Host '  git remote add coplay https://github.com/CoplayDev/unity-mcp.git' }
            'murzak' { Write-Host '  git remote add murzak https://github.com/IvanMurzak/Unity-MCP.git' }
        }
        exit 1
    }
}

# Load previous state
$lastSeen = @{}
if (Test-Path $stateFile) {
    Get-Content $stateFile | ForEach-Object {
        if ($_ -match '^([^=]+)=(.+)$') {
            $lastSeen[$Matches[1]] = $Matches[2]
        }
    }
}

foreach ($remote in $remotes) {
    Write-Host "=== Fetching $remote ==="
    git fetch $remote --prune

    $defaultBranch = $null
    foreach ($candidate in @('main', 'master')) {
        $null = git show-ref --quiet "refs/remotes/$remote/$candidate"
        if ($LASTEXITCODE -eq 0) { $defaultBranch = $candidate; break }
    }
    if (-not $defaultBranch) {
        Write-Host "  (could not find main or master on $remote, skipping diff)"
        continue
    }

    $currentHead = (git rev-parse "$remote/$defaultBranch").Trim()
    $key = "$remote/$defaultBranch"
    $previous = $lastSeen[$key]

    Write-Host "  HEAD: $currentHead on $remote/$defaultBranch"
    if (-not $previous) {
        Write-Host '  (no previous sync state — run again to start tracking)'
    } elseif ($previous -eq $currentHead) {
        Write-Host '  No new commits since last sync.'
    } else {
        $newCount = (git rev-list --count "$previous..$currentHead").Trim()
        Write-Host "  $newCount new commit(s) since $previous"
        Write-Host ''
        git log --oneline --no-decorate "$previous..$currentHead" | Select-Object -First 40 | ForEach-Object { "    $_" }
        Write-Host ''
    }

    $lastSeen[$key] = $currentHead
}

# Rewrite state file
$lines = $lastSeen.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }
$lines | Set-Content -Path $stateFile -Encoding ASCII

Write-Host "=== State saved to $stateFile ==="
