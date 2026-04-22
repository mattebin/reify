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

    full_body = body
    if fm:
        metadata_lines = []
        for k in ("model_name", "effort", "severity", "affected_tool",
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
    ap.add_argument("--repo", default="mattebin/reify",
                    help="GitHub repo to file to (owner/name). Default: mattebin/reify")
    ap.add_argument("--dry-run", action="store_true",
                    help="Do everything except actually call `gh issue create`.")
    args = ap.parse_args()

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
            url = file_to_github(path, args.repo, args.dry_run)
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
