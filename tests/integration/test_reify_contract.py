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
    last_exc: Exception | None = None
    for attempt in range(5):
        try:
            with urllib.request.urlopen(req, timeout=timeout) as r:
                payload = r.read()
                if not payload.strip():
                    raise json.JSONDecodeError("empty response", "", 0)
                return json.loads(payload)
        except urllib.error.HTTPError as e:
            payload = e.read()
            if not payload.strip():
                last_exc = json.JSONDecodeError("empty error response", "", 0)
            else:
                return json.loads(payload)
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as e:
            last_exc = e

        if attempt < 4:
            time.sleep(0.2)
            continue

    assert last_exc is not None
    raise last_exc


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
    suffix = int(time.time() * 1000)
    class_name = f"ReifyContractInspect_{suffix}"
    asset_path = f"Assets/ReifyContract/_inspect_{suffix}.cs"
    content = (
        "using UnityEngine;\n\n"
        f"public class {class_name} : MonoBehaviour\n"
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
        assert any(cls["name"] == class_name for cls in d["classes"])
        assert "script" in d
        assert "read_at_utc" in d
        assert "frame" in d
    finally:
        ok(call("script-delete", {"asset_path": asset_path, "use_trash": False}))
        request_compile_and_wait()


def test_script_execute_smoke():
    """script-execute compiles and runs a parameterless static entrypoint."""
    d = ok(call("script-execute", {
        "code": (
            "using UnityEngine;\n"
            "public static class ReifyScriptExecution\n"
            "{\n"
            "    public static object Run()\n"
            "    {\n"
            "        return Application.productName + \":\" + 7;\n"
            "    }\n"
            "}\n"
        )
    }))
    assert d["executed"] is True
    assert d["compile_succeeded"] is True
    assert d["return_value"].endswith(":7")


def test_object_get_modify_roundtrip():
    """Generic object-get/object-modify works on arbitrary UnityEngine.Objects."""
    name = f"_obj_{int(time.time() * 1000)}"
    created = ok(call("gameobject-create", {"name": name}))
    try:
        added = ok(call("component-add", {
            "gameobject_path": name,
            "component_type": "UnityEngine.Rigidbody"
        }))
        instance_id = added["added"]["instance_id"]

        got = ok(call("object-get-data", {
            "instance_id": instance_id,
            "include_properties": True
        }))
        assert got["target"]["type_fqn"] == "UnityEngine.Rigidbody"
        assert got["property_count"] > 0

        mod = ok(call("object-modify", {
            "instance_id": instance_id,
            "properties": {"m_Mass": 3.5}
        }))
        assert mod["applied_count"] == 1
        assert mod["applied_fields"][0]["field"] == "m_Mass"
        assert abs(mod["applied_fields"][0]["after"] - 3.5) < 1e-6
    finally:
        ok(call("gameobject-destroy", {"path": name}))


def test_gap_utility_reads():
    """New discovery utilities return useful data without requiring clicks."""
    builtins = ok(call("asset-find-built-in", {"type": "Shader", "limit": 25}))
    assert "match_count" in builtins
    assert "warnings" in builtins

    shaders = ok(call("asset-shader-list-all", {"limit": 100}))
    assert shaders["shader_count"] > 0

    components = ok(call("component-list-all", {"name_like": "Rigid", "limit": 100}))
    assert components["component_type_count"] > 0


def test_spatial_primitive_evidence_smoke():
    """Spatial evidence returns bounds, anchors, and transform basis."""
    name = f"_spatial_{int(time.time() * 1000)}"
    ok(call("gameobject-create", {"name": name, "primitive": "Cube"}))
    try:
        d = ok(call("spatial-primitive-evidence", {"gameobject_path": name}))
        primitive = d["primitive"]
        assert primitive["instance_id"]
        assert primitive["path"] == name
        assert primitive["primitive_kind"] == "Cube"
        assert abs(primitive["height_world"] - 1.0) < 1e-4
        assert "top" in primitive["anchors_world"]
        assert "bottom" in primitive["anchors_world"]
        assert "up" in primitive["transform"]
        assert "lossy_scale" in primitive["transform"]
    finally:
        ok(call("gameobject-destroy", {"path": name}))


def test_spatial_anchor_distance_touching_cubes():
    """Anchor distance proves exact cube contact instead of eyeballing it."""
    suffix = int(time.time() * 1000)
    lower = f"_lower_{suffix}"
    upper = f"_upper_{suffix}"
    ok(call("gameobject-create", {"name": lower, "primitive": "Cube"}))
    ok(call("gameobject-create", {
        "name": upper,
        "primitive": "Cube",
        "position": {"x": 0.0, "y": 1.0, "z": 0.0}
    }))
    try:
        d = ok(call("spatial-anchor-distance", {
            "a_path": lower,
            "a_anchor": "top",
            "b_path": upper,
            "b_anchor": "bottom",
            "tolerance": 0.001
        }))
        assert d["within_tolerance"] is True
        assert d["distance_meters"] < 1e-4
        assert d["axis_gap_meters"]["y"] < 1e-4
    finally:
        ok(call("gameobject-destroy", {"path": upper}))
        ok(call("gameobject-destroy", {"path": lower}))


def test_scene_set_active_unload_roundtrip():
    """scene-set-active and scene-unload work together in a safe additive flow."""
    settings = ok(call("project-build-settings"))
    assert settings["enabled_scene_count"] > 0
    base_path = settings["scenes_in_build"][0]["path"]
    temp_path = f"Assets/ReifyContract/_scene_{int(time.time() * 1000)}.unity"

    ok(call("scene-open", {"path": base_path, "additive": False}))
    try:
        ok(call("scene-create", {"path": temp_path, "setup_default": False}))
        ok(call("scene-open", {"path": base_path, "additive": True}))
        time.sleep(0.2)

        active = ok(call("scene-set-active", {"path": base_path}))
        assert active["active_scene"]["path"] == base_path

        unloaded = ok(call("scene-unload", {"path": temp_path}))
        assert unloaded["unloaded"]["path"] == temp_path
        assert unloaded["remaining_open_scene_count"] >= 1
    finally:
        ok(call("scene-open", {"path": base_path, "additive": False}))
        ok(call("asset-delete", {"path": temp_path, "use_trash": False}))


def test_prefab_save_roundtrip():
    """prefab-save persists prefab-mode modifications and can be read back."""
    name = f"_prefab_{int(time.time() * 1000)}"
    prefab_path = f"Assets/ReifyContract/{name}.prefab"
    base_path = "Assets/Scenes/SampleScene.unity"

    ok(call("scene-open", {"path": base_path, "additive": False}))
    ok(call("gameobject-create", {"name": name}))
    try:
        ok(call("prefab-create", {
            "gameobject_path": name,
            "asset_path": prefab_path,
            "connect_instance": False
        }))
        ok(call("gameobject-destroy", {"path": name}))

        opened = ok(call("prefab-open", {"asset_path": prefab_path}))
        root = opened["stage"]["prefab_name"]
        ok(call("gameobject-modify", {
            "path": root,
            "local_position": {"x": 2.0, "y": 0.0, "z": 0.0}
        }))
        saved = ok(call("prefab-save"))
        assert saved["prefab"]["asset_path"] == prefab_path
        ok(call("prefab-close"))

        reopened = ok(call("prefab-open", {"asset_path": prefab_path}))
        root2 = reopened["stage"]["prefab_name"]
        found = ok(call("gameobject-find", {"path": root2}))
        assert found["match_count"] == 1
        pos = found["matches"][0]["transform"]["local_position"]
        assert pos["x"] == 2.0
        ok(call("prefab-close"))
    finally:
        call("prefab-close")
        ok(call("scene-open", {"path": base_path, "additive": False}))
        ok(call("asset-delete", {"path": prefab_path, "use_trash": False}))


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
