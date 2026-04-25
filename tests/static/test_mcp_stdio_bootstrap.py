#!/usr/bin/env python3
"""Smoke-test the real MCP stdio server bootstrap and tools/list path."""

from __future__ import annotations

import json
import subprocess
import sys
import threading
import time
from pathlib import Path
from queue import Queue


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_DLL = ROOT / "src" / "Server" / "bin" / "Release" / "net8.0" / "reify-server.dll"
REQUIRED_TOOLS = {
    "ping",
    "reify-health",
    "reify-tool-list",
    "scene-snapshot",
    "scene-diff",
    "batch-execute",
    "compile-errors-structured",
}


def read_line_with_timeout(stream, timeout_seconds: float) -> str:
    q: Queue[str] = Queue()

    def worker() -> None:
        q.put(stream.readline())

    thread = threading.Thread(target=worker, daemon=True)
    thread.start()
    thread.join(timeout_seconds)
    if thread.is_alive():
        raise TimeoutError("timed out waiting for MCP server stdout")
    return q.get()


def send(process: subprocess.Popen[str], message: dict) -> None:
    assert process.stdin is not None
    process.stdin.write(json.dumps(message, separators=(",", ":")) + "\n")
    process.stdin.flush()


def receive(process: subprocess.Popen[str], timeout_seconds: float = 10.0) -> dict:
    assert process.stdout is not None
    line = read_line_with_timeout(process.stdout, timeout_seconds)
    if not line:
        raise RuntimeError("MCP server closed stdout")
    return json.loads(line)


def main() -> int:
    if len(sys.argv) > 1:
        command = sys.argv[1:]
    else:
        command = ["dotnet", str(DEFAULT_DLL)]

    process = subprocess.Popen(
        command,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        cwd=ROOT,
    )

    try:
        send(process, {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "initialize",
            "params": {
                "protocolVersion": "2025-03-26",
                "capabilities": {},
                "clientInfo": {"name": "reify-static-smoke", "version": "0"},
            },
        })
        init = receive(process)
        if init.get("id") != 1 or "result" not in init:
            raise AssertionError(f"bad initialize response: {init}")

        send(process, {
            "jsonrpc": "2.0",
            "method": "notifications/initialized",
            "params": {},
        })
        send(process, {
            "jsonrpc": "2.0",
            "id": 2,
            "method": "tools/list",
            "params": {},
        })
        listed = receive(process, timeout_seconds=15.0)
        tools = listed.get("result", {}).get("tools", [])
        names = {tool.get("name") for tool in tools}
        missing = sorted(REQUIRED_TOOLS - names)
        if missing:
            raise AssertionError(f"missing required MCP tools: {missing}")
        if len(names) < 250:
            raise AssertionError(f"unexpectedly low MCP tool count: {len(names)}")

        print(f"mcp stdio ok: tools={len(names)}")
        return 0
    finally:
        process.terminate()
        try:
            process.wait(timeout=3)
        except subprocess.TimeoutExpired:
            process.kill()
        if process.returncode not in (0, None, -15, 143):
            stderr = ""
            if process.stderr is not None:
                stderr = process.stderr.read()
            if stderr:
                print(stderr[-4000:], file=sys.stderr)


if __name__ == "__main__":
    sys.exit(main())
