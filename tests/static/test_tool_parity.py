#!/usr/bin/env python3
"""Fail when Unity bridge tools are not exposed by the MCP server."""

from __future__ import annotations

import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
EDITOR_TOOLS = ROOT / "src" / "Editor" / "Tools"
SERVER_TOOLS = ROOT / "src" / "Server" / "Tools"

EDITOR_RE = re.compile(r'\[ReifyTool\("([^"]+)"\)\]')
SERVER_RE = re.compile(r'McpServerTool\(Name\s*=\s*"([^"]+)"\)')


def collect(folder: Path, pattern: re.Pattern[str]) -> set[str]:
    names: set[str] = set()
    for path in sorted(folder.glob("*.cs")):
        text = path.read_text(encoding="utf-8", errors="replace")
        names.update(pattern.findall(text))
    return names


def main() -> int:
    editor = collect(EDITOR_TOOLS, EDITOR_RE)
    server = collect(SERVER_TOOLS, SERVER_RE)

    missing = sorted(editor - server)
    extra = sorted(server - editor)

    print(f"editor_tools={len(editor)} server_tools={len(server)}")

    if not missing and not extra:
        print("tool parity ok")
        return 0

    if missing:
        print("missing MCP server wrappers:")
        for name in missing:
            print(f"  - {name}")

    if extra:
        print("server wrappers without editor handlers:")
        for name in extra:
            print(f"  - {name}")

    return 1


if __name__ == "__main__":
    sys.exit(main())
