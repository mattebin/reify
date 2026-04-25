#!/usr/bin/env python3
"""
Interactive review of LLM-reported issues. Reads reports/llm-issues/pending/,
shows each one to the user, and on approval files it to GitHub via `gh`.

Usage:
    python scripts/review-llm-issues.py
    python scripts/review-llm-issues.py --repo mattebin/reify
    python scripts/review-llm-issues.py --dry-run

Per report the user picks one of:
    [y]es       file to GitHub, move to submitted/
    [n]o        not worth filing, move to dismissed/
    [d]elete    remove the file entirely
    [s]kip      leave in pending/ for next run
    [q]uit      stop reviewing

gh needs to be authenticated with write access to the target repo. If the
current env var GITHUB_TOKEN is a fine-grained PAT without access, the
script re-invokes gh without that env var so git-credential falls back to
the keyring token (same trick used elsewhere in this repo).
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

# Force UTF-8 stdout/stderr so report bodies containing non-ASCII display
# on Windows consoles (default cp1252) instead of crashing.
try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

try:
    import yaml  # optional; we parse the tiny frontmatter by hand if absent
    HAS_YAML = True
except ImportError:
    HAS_YAML = False

ROOT = Path(__file__).resolve().parents[1]
PENDING = ROOT / "reports" / "llm-issues" / "pending"
SUBMITTED = ROOT / "reports" / "llm-issues" / "submitted"
DISMISSED = ROOT / "reports" / "llm-issues" / "dismissed"


def detect_repo_from_origin() -> str | None:
    """Return 'owner/name' from the reify repo's origin remote.

    Fork users: their origin points to their fork, so their LLM-reported
    issues file into their own tracker by default — not upstream.
    """
    try:
        out = subprocess.run(
            ["git", "-C", str(ROOT), "remote", "get-url", "origin"],
            capture_output=True, text=True, check=False, timeout=5,
        )
        if out.returncode != 0:
            return None
        url = out.stdout.strip()
        # Handle https://github.com/owner/repo.git and git@github.com:owner/repo.git
        if url.startswith("git@"):
            _, _, tail = url.partition(":")
        else:
            tail = url.split("github.com/", 1)[-1]
        owner_repo = tail.removesuffix(".git").strip("/")
        if "/" not in owner_repo:
            return None
        return owner_repo
    except Exception:
        return None


def current_gh_user() -> str | None:
    """Whoever `gh` will authenticate as on this machine, sans GITHUB_TOKEN.

    Using GITHUB_TOKEN here would mask the keyring token that actually
    has repo-write scope in our setup. Same trick as file_to_github().
    """
    env = os.environ.copy()
    env.pop("GITHUB_TOKEN", None)
    try:
        out = subprocess.run(
            ["gh", "api", "user", "--jq", ".login"],
            capture_output=True, text=True, env=env, check=False, timeout=5,
        )
        if out.returncode != 0:
            return None
        login = out.stdout.strip()
        return login or None
    except FileNotFoundError:
        return None
    except Exception:
        return None


def parse_frontmatter(text: str) -> tuple[dict, str]:
    """Minimal YAML frontmatter parser — no dependencies needed."""
    if not text.startswith("---"):
        return {}, text
    end = text.find("\n---", 3)
    if end == -1:
        return {}, text
    header = text[3:end].strip()
    body = text[end + 4:].lstrip("\n")
    if HAS_YAML:
        try:
            return yaml.safe_load(header) or {}, body
        except Exception:
            pass
    # Fallback: line-by-line `key: "value"`
    fm = {}
    for line in header.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        if ":" not in line:
            continue
        k, _, v = line.partition(":")
        v = v.strip().strip('"').strip("'")
        fm[k.strip()] = v
    return fm, body


def ensure_dirs() -> None:
    PENDING.mkdir(parents=True, exist_ok=True)
    SUBMITTED.mkdir(parents=True, exist_ok=True)
    DISMISSED.mkdir(parents=True, exist_ok=True)


LABEL_COLORS = {
    "llm-reported":    ("0E8A16", "Filed via reify-log-issue"),
    "severity:info":   ("C5DEF5", "info severity"),
    "severity:warn":   ("FBCA04", "warn severity"),
    "severity:error":  ("D93F0B", "error severity"),
    "severity:critical": ("B60205", "critical severity"),
    "effort:S":        ("BFDADC", "small fix"),
    "effort:M":        ("C2E0C6", "medium fix"),
    "effort:L":        ("5319E7", "large fix"),
}


def ensure_labels_exist(repo: str, labels: list[str]) -> list[str]:
    """Create labels in the target repo if they don't exist yet. Returns
    the filtered label list (drops labels whose creation failed).

    Labels like `reporter:<model>` are created dynamically on first use
    so each new LLM gets its own filter bucket without prep work.
    """
    env = os.environ.copy()
    env.pop("GITHUB_TOKEN", None)
    kept = []
    for label in labels:
        # Pick a deterministic colour + description for known labels,
        # generic ones for dynamic labels like reporter:*.
        if label in LABEL_COLORS:
            color, desc = LABEL_COLORS[label]
        elif label.startswith("reporter:"):
            color, desc = "B4A7E5", f"Issues reported by {label.split(':',1)[1]}"
        else:
            color, desc = "CCCCCC", ""
        try:
            subprocess.run(
                ["gh", "label", "create", label,
                 "--color", color, "--description", desc,
                 "--repo", repo, "--force"],
                capture_output=True, text=True, env=env, check=False, timeout=10,
            )
            # --force makes it idempotent (create-or-update); assume success.
            kept.append(label)
        except Exception:
            # Drop the label rather than fail the whole issue filing.
            pass
    return kept


def file_to_github(path: Path, repo: str, dry_run: bool) -> str | None:
    """Return the issue URL on success, None on failure or dry-run."""
    text = path.read_text(encoding="utf-8")
    fm, body = parse_frontmatter(text)
    title = fm.get("issue_title") or path.stem
    labels = ["llm-reported"]
    severity = fm.get("severity")
    if severity:
        labels.append(f"severity:{severity}")
    effort = fm.get("effort")
    if effort:
        labels.append(f"effort:{effort}")
    model = fm.get("model_name")
    if model:
        labels.append(f"reporter:{model.replace(' ', '-')}")

    # Ensure all labels exist on the repo (idempotent) so first-use filings
    # don't fail on missing dynamic labels like `reporter:<model>`.
    if not dry_run:
        labels = ensure_labels_exist(repo, labels)

    full_body = body
    if fm:
        metadata_lines = []
        for k in ("model_name", "effort", "severity", "affected_tool",
                  "ai_recommendation", "ai_reason",
                  "unity_version", "platform", "reify_tool_count",
                  "frame_at_detection", "timestamp_utc", "reify_commit"):
            if fm.get(k):
                metadata_lines.append(f"- **{k}**: {fm[k]}")
        if metadata_lines:
            full_body = "## Metadata\n\n" + "\n".join(metadata_lines) + "\n\n" + body

    cmd = [
        "gh", "issue", "create",
        "--repo", repo,
        "--title", title,
        "--body", full_body,
    ]
    for l in labels:
        cmd.extend(["--label", l])

    if dry_run:
        print(f"  [dry-run] would run: {' '.join(cmd[:7])} ... (+{len(labels)} labels)")
        return None

    # Unset GITHUB_TOKEN if it's set — same trick git-push uses elsewhere.
    env = os.environ.copy()
    env.pop("GITHUB_TOKEN", None)

    try:
        result = subprocess.run(cmd, capture_output=True, text=True, env=env, check=False)
    except FileNotFoundError:
        print("  ERROR: `gh` CLI not found. Install from https://cli.github.com/")
        return None
    if result.returncode != 0:
        print(f"  ERROR: gh issue create failed:\n{result.stderr}")
        return None
    url = result.stdout.strip()
    return url


def show_report(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    print("=" * 72)
    print(f"  {path.name}")
    print("=" * 72)
    print(text)
    print("=" * 72)


def prompt_choice() -> str:
    while True:
        choice = input("  [y]es submit / [n]o dismiss / [d]elete / [s]kip / [q]uit? ").strip().lower()
        if choice and choice[0] in ("y", "n", "d", "s", "q"):
            return choice[0]
        print("  Please choose y / n / d / s / q.")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--repo", default=None,
                    help="GitHub repo to file to (owner/name). Defaults to "
                         "the origin remote of this reify checkout.")
    ap.add_argument("--dry-run", action="store_true",
                    help="Do everything except actually call `gh issue create`.")
    ap.add_argument("--yes-i-know-filing-identity", action="store_true",
                    help="Skip the one-time identity confirmation. For CI only.")
    args = ap.parse_args()

    # --- Target repo: prefer explicit flag, fall back to origin autodetect ---
    target_repo = args.repo or detect_repo_from_origin()
    if not target_repo:
        print("ERROR: could not determine a target repo.")
        print("  Either pass --repo owner/name, or make sure this checkout has a")
        print("  github.com origin remote.")
        return 2

    # --- Identity gate: show who this will post as BEFORE showing any report ---
    gh_user = current_gh_user()
    if not gh_user and not args.dry_run:
        print("ERROR: `gh` is not authenticated.")
        print("  Run `gh auth login` first, or pass --dry-run to preview without filing.")
        return 2

    print("=" * 72)
    print(f"  Filing target:  {target_repo}")
    print(f"  Filing as:      {gh_user or '(unauthenticated — dry-run only)'}")
    print(f"  Dry run:        {args.dry_run}")
    print("=" * 72)
    if not args.yes_i_know_filing_identity and not args.dry_run:
        confirm = input(f"  Proceed filing to {target_repo} as {gh_user}? [y/N] ").strip().lower()
        if confirm != "y":
            print("  Aborted.")
            return 0
    print()

    ensure_dirs()
    reports = sorted(PENDING.glob("*.md"))
    if not reports:
        print(f"No pending reports in {PENDING.relative_to(ROOT)}.")
        return 0

    print(f"Found {len(reports)} pending report(s) under {PENDING.relative_to(ROOT)}.\n")
    stats = {"submitted": 0, "dismissed": 0, "deleted": 0, "skipped": 0}

    for path in reports:
        show_report(path)
        choice = prompt_choice()
        if choice == "q":
            print("Stopping.")
            break
        elif choice == "y":
            url = file_to_github(path, target_repo, args.dry_run)
            if url or args.dry_run:
                dest = SUBMITTED / path.name
                shutil.move(path, dest)
                if url:
                    dest.write_text(dest.read_text(encoding="utf-8") +
                                    f"\n\n<!-- filed to GitHub: {url} -->\n",
                                    encoding="utf-8")
                    print(f"  Filed: {url}")
                stats["submitted"] += 1
            else:
                print("  Not submitted — leaving in pending/.")
        elif choice == "n":
            shutil.move(path, DISMISSED / path.name)
            stats["dismissed"] += 1
            print("  Moved to dismissed/.")
        elif choice == "d":
            path.unlink()
            stats["deleted"] += 1
            print("  Deleted.")
        elif choice == "s":
            stats["skipped"] += 1
            print("  Skipped — stays in pending/.")
        print()

    print(f"Done: {stats}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
