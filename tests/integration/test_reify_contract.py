"""
Reify integration contract tests. Run against a live Unity with the reify
HTTP bridge on 127.0.0.1:17777.

Usage:
    python -m pytest tests/integration/test_reify_contract.py -v

Or standalone without pytest:
    python tests/integration/test_reify_contract.py

The tests exercise the critical path: ping, read, write+diff,
error-shape discrimination, ADR-002 receipts. They are intentionally
written without any test-framework dependency beyond optional pytest,
so they can run in CI or by hand.
"""
from __future__ import annotations

import json
import sys
import time
import urllib.error
import urllib.request
from typing import Any

BASE = "http://127.0.0.1:17777/tool"


def call(tool: str, args: dict | None = None, timeout: float = 15.0) -> dict:
    """POST /tool and return the parsed envelope."""
    body = json.dumps({"tool": tool, "args": args or {}}).encode("utf-8")
    req = urllib.request.Request(
        BASE, data=body, headers={"Content-Type": "application/json"}
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.loads(r.read())
    except urllib.error.HTTPError as e:
        return json.loads(e.read())


def ok(envelope: dict) -> dict:
    assert envelope.get("ok"), f"expected ok=true, got {envelope}"
    return envelope["data"]


def err(envelope: dict, code: str) -> dict:
    assert not envelope.get("ok"), f"expected error, got success: {envelope}"
    assert envelope["error"]["code"] == code, (
        f"expected code={code}, got {envelope['error']}"
    )
    return envelope["error"]


def request_compile_and_wait(timeout: float = 30.0) -> dict:
    """Ask Unity to compile/reload scripts, then wait for the editor to settle."""
    ok(call("editor-request-script-compilation"))

    started = time.time()
    while time.time() - started < timeout:
        envelope = call("domain-reload-status", timeout=10.0)
        if envelope.get("ok"):
            data = envelope["data"]
            if not data.get("is_busy", True):
                return data
        time.sleep(0.5)

    raise AssertionError(f"Unity did not become ready within {timeout:.1f}s")


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

def test_ping_live():
    """Bridge responds with Unity version + frame count."""
    d = ok(call("ping"))
    assert "unity_version" in d
    assert "frame" in d
    assert "read_at_utc" in d


def test_read_has_evidence_fields():
    """Every read response carries read_at_utc + frame per ADR-001."""
    d = ok(call("scene-list"))
    assert "read_at_utc" in d
    assert "frame" in d


def test_script_inspect_has_roslyn_evidence():
    """script-inspect returns code-grounded diagnostics + type summaries."""
    asset_path = f"Assets/ReifyContract/_inspect_{int(time.time() * 1000)}.cs"
    content = (
        "using UnityEngine;\n\n"
        "public class ReifyContractInspect : MonoBehaviour\n"
        "{\n"
        "    public int value = 1;\n"
        "\n"
        "    void Start()\n"
        "    {\n"
        "        Debug.Log(value);\n"
        "    }\n"
        "}\n"
    )

    ok(call("script-update-or-create", {"asset_path": asset_path, "content": content}))
    try:
        request_compile_and_wait()
        d = ok(call("script-inspect", {"asset_path": asset_path}))
        assert d["roslyn_available"] is True
        assert d["asset_path"] == asset_path
        assert "guid" in d
        assert d["inspection_mode"] in ("syntax_only", "syntax_and_semantic")
        assert "compile_diagnostics" in d
        assert "classes" in d
        assert any(cls["name"] == "ReifyContractInspect" for cls in d["classes"])
        assert "script" in d
        assert "read_at_utc" in d
        assert "frame" in d
    finally:
        ok(call("script-delete", {"asset_path": asset_path, "use_trash": False}))
        request_compile_and_wait()


def test_error_discrimination_unknown_tool():
    err(call("definitely-not-a-real-tool"), "UNKNOWN_TOOL")


def test_error_discrimination_invalid_args():
    """Tools that reject missing required args emit INVALID_ARGS (400)."""
    err(call("scene-diff", {}), "INVALID_ARGS")
    err(call("gameobject-modify", {"path": "Main Camera"}), "INVALID_ARGS")  # empty mutation
    err(call("gameobject-modify", {"path": "Main Camera", "fake_field": 1}), "INVALID_ARGS")


def test_error_discrimination_component_not_found():
    """Missing component resolves to COMPONENT_NOT_FOUND (not TOOL_EXCEPTION)."""
    # Main Camera doesn't have an Animator — should be 404 not 500.
    e = err(call("animator-state", {"gameobject_path": "Main Camera"}),
            "COMPONENT_NOT_FOUND")
    assert "Main Camera" in e["message"]


def test_scene_diff_roundtrip_noop():
    """scene-snapshot → scene-diff on unchanged scene = 0/0/0."""
    before = ok(call("scene-snapshot"))
    d = ok(call("scene-diff", {"before_snapshot": before}))
    assert d["added_count"] == 0, d
    assert d["removed_count"] == 0, d
    assert d["changed_count"] == 0, d


def test_write_roundtrip_create_diff_destroy():
    """Full verifiable-write cycle with self-proving receipts."""
    name = f"_test_{int(time.time() * 1000)}"
    before = ok(call("scene-snapshot"))

    # Create
    created = ok(call("gameobject-create", {"name": name}))
    assert "gameobject" in created
    assert created["gameobject"]["name"] == name

    # Diff shows added=1
    d1 = ok(call("scene-diff", {"before_snapshot": before}))
    assert d1["added_count"] == 1, d1
    assert d1["removed_count"] == 0

    # Modify with ADR-002 receipt: applied_fields with before/after
    mod = ok(call("gameobject-modify", {
        "path": name,
        "local_position": {"x": 1.0, "y": 2.0, "z": 3.0}
    }))
    assert "applied_fields" in mod, "ADR-002: gameobject-modify must return applied_fields"
    fields = {f["field"]: f for f in mod["applied_fields"]}
    assert "local_position" in fields
    assert fields["local_position"]["after"]["x"] == 1.0
    assert fields["local_position"]["after"]["y"] == 2.0

    # Unknown field should hard-fail, not silent-OK
    e = err(call("gameobject-modify", {"path": name, "fake_field": 1}),
            "INVALID_ARGS")
    assert "Unknown field" in e["message"]

    # Destroy
    ok(call("gameobject-destroy", {"path": name}))

    # Back to baseline
    d2 = ok(call("scene-diff", {"before_snapshot": before}))
    assert d2["added_count"] == 0 and d2["removed_count"] == 0 and d2["changed_count"] == 0


def test_adr002_component_add_receipt():
    """component-add returns before/after counts (ADR-002)."""
    name = f"_test_{int(time.time() * 1000)}"
    ok(call("gameobject-create", {"name": name}))
    try:
        r = ok(call("component-add", {
            "gameobject_path": name,
            "component_type": "UnityEngine.Rigidbody"
        }))
        assert "applied_fields" in r, "ADR-002: component-add must return applied_fields"
        fields = {f["field"]: f for f in r["applied_fields"]}
        assert fields["component_type_count"]["before"] == 0
        assert fields["component_type_count"]["after"] == 1
    finally:
        ok(call("gameobject-destroy", {"path": name}))


def test_adr002_primitive_create_has_mesh_bounds():
    """gameobject-create with primitive returns mesh_bounds + primitive_defaults."""
    name = f"_cap_{int(time.time() * 1000)}"
    r = ok(call("gameobject-create", {
        "name": name, "primitive": "Capsule",
        "scale": {"x": 0.3, "y": 0.35, "z": 0.3}
    }))
    try:
        assert r.get("primitive") == "Capsule"
        assert r["primitive_defaults"]["height"] == 2.0
        assert r["primitive_defaults"]["radius"] == 0.5
        assert abs(r["mesh_bounds"]["world_size"]["y"] - 0.7) < 1e-4
        assert abs(r["world_height"] - 0.7) < 1e-4
    finally:
        ok(call("gameobject-destroy", {"path": name}))


def test_resolver_arg_aliases():
    """All four component-tool aliases accepted."""
    name = f"_aliases_{int(time.time() * 1000)}"
    ok(call("gameobject-create", {"name": name}))
    try:
        # path + type_name
        ok(call("component-add", {"path": name, "type_name": "UnityEngine.Rigidbody"}))
        # gameobject_path + component_type (mixed aliases across tools)
        ok(call("component-remove", {
            "gameobject_path": name, "component_type": "UnityEngine.Rigidbody"
        }))
    finally:
        ok(call("gameobject-destroy", {"path": name}))


def test_reify_self_check_passes():
    """The reify-self-check tool passes on a healthy install."""
    d = ok(call("reify-self-check"))
    assert d["fail_count"] == 0, d
    assert d["ok"] is True


# ---------------------------------------------------------------------------
# Standalone runner (no pytest needed)
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    tests = [obj for name, obj in globals().items() if name.startswith("test_") and callable(obj)]
    failed = []
    for t in tests:
        try:
            t()
            print(f"PASS  {t.__name__}")
        except AssertionError as e:
            print(f"FAIL  {t.__name__}: {e}")
            failed.append(t.__name__)
        except Exception as e:
            print(f"ERROR {t.__name__}: {type(e).__name__}: {e}")
            failed.append(t.__name__)
    print()
    print(f"{len(tests) - len(failed)} passed, {len(failed)} failed")
    sys.exit(1 if failed else 0)
