#!/usr/bin/env bash
# sync-upstream.sh — fetch both upstream remotes, report new commits since
# the last time we ran this. No merges, no pulls; this is informational.
#
# Usage:  ./scripts/sync-upstream.sh
# Effect: updates refs for coplay/ and murzak/, writes .upstream-sync-state
#         to track last-seen SHAs, prints diff summaries.

set -euo pipefail

cd "$(dirname "$0")/.."

REMOTES=("coplay" "murzak")
STATE_FILE=".upstream-sync-state"

# Verify remotes exist
for remote in "${REMOTES[@]}"; do
  if ! git remote | grep -q "^${remote}$"; then
    echo "ERROR: remote '${remote}' not configured. Add with:"
    case "$remote" in
      coplay) echo "  git remote add coplay https://github.com/CoplayDev/unity-mcp.git" ;;
      murzak) echo "  git remote add murzak https://github.com/IvanMurzak/Unity-MCP.git" ;;
    esac
    exit 1
  fi
done

# Load previous state (if any)
declare -A last_seen
if [[ -f "$STATE_FILE" ]]; then
  while IFS='=' read -r key value; do
    last_seen["$key"]="$value"
  done < "$STATE_FILE"
fi

# Fetch each remote and report
for remote in "${REMOTES[@]}"; do
  echo "=== Fetching $remote ==="
  git fetch "$remote" --prune

  # Determine default branch (main or master)
  default_branch=""
  for candidate in main master; do
    if git show-ref --quiet "refs/remotes/${remote}/${candidate}"; then
      default_branch="$candidate"
      break
    fi
  done
  if [[ -z "$default_branch" ]]; then
    echo "  (could not find main or master on $remote, skipping diff)"
    continue
  fi

  current_head=$(git rev-parse "${remote}/${default_branch}")
  key="${remote}/${default_branch}"
  previous=${last_seen[$key]:-}

  echo "  HEAD: $current_head on ${remote}/${default_branch}"
  if [[ -z "$previous" ]]; then
    echo "  (no previous sync state — run again to start tracking)"
  elif [[ "$previous" == "$current_head" ]]; then
    echo "  No new commits since last sync."
  else
    new_count=$(git rev-list --count "${previous}..${current_head}" 2>/dev/null || echo "?")
    echo "  $new_count new commit(s) since $previous"
    echo ""
    git log --oneline --no-decorate "${previous}..${current_head}" | head -40 | sed 's/^/    /'
    echo ""
  fi

  last_seen[$key]="$current_head"
done

# Rewrite state file
{
  for key in "${!last_seen[@]}"; do
    echo "${key}=${last_seen[$key]}"
  done
} > "$STATE_FILE"

echo "=== State saved to $STATE_FILE ==="
