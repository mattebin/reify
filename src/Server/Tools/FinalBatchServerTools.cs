using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class ScriptableObjectServerTools
{
    [McpServerTool(Name = "scriptable-object-list-types"), Description(
        "List every concrete ScriptableObject-derived type loaded in the " +
        "AppDomain. Filters: name_filter (case-insensitive substring), " +
        "user_only=true (default) skips Unity/System/mscorlib assemblies, " +
        "limit (default 500). Includes CreateAssetMenuAttribute metadata " +
        "when present — answers 'what ScriptableObject types can I create?' " +
        "in one call.")]
    public static async Task<JsonElement> ScriptableObjectListTypes(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("scriptable-object-list-types", args, ct);

    [McpServerTool(Name = "scriptable-object-inspect"), Description(
        "Inspect a ScriptableObject asset: type_fqn, hide_flags, and every " +
        "serialized field (name + type + typed value). Use this instead of " +
        "generic asset-get when you want an SO-focused response shape.")]
    public static async Task<JsonElement> ScriptableObjectInspect(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("scriptable-object-inspect", args, ct);
}

[McpServerToolType]
public static class AnimationClipServerTools
{
    [McpServerTool(Name = "animation-clip-inspect"), Description(
        "Read AnimationClip metadata: length, frame_rate, is_looping, " +
        "legacy flag, humanMotion flag, local bounds, wrap mode, curve " +
        "counts (regular + object-ref), event count. Warnings for zero-" +
        "length, no curves, and very short looping clips.")]
    public static async Task<JsonElement> AnimationClipInspect(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("animation-clip-inspect", args, ct);

    [McpServerTool(Name = "animation-clip-list-curves"), Description(
        "List every float curve on an AnimationClip with path + component " +
        "type + propertyName + key_count + duration + first/last value. " +
        "Paginated by `limit` (default 500).")]
    public static async Task<JsonElement> AnimationClipListCurves(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("animation-clip-list-curves", args, ct);

    [McpServerTool(Name = "animation-clip-set-curve"), Description(
        "Create or replace an AnimationCurve on a clip. Args: asset_path, " +
        "relative_path (e.g. 'Armature/Hips', '' for the root), type_name " +
        "(e.g. 'UnityEngine.Transform'), property_name (e.g. " +
        "'localPosition.x'), keyframes[] array of {time, value}. " +
        "Undo-backed. Returns the post-write curve count.")]
    public static async Task<JsonElement> AnimationClipSetCurve(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("animation-clip-set-curve", args, ct);
}

[McpServerToolType]
public static class InputSystemServerTools
{
    [McpServerTool(Name = "input-actions-asset-inspect"), Description(
        "Inspect an InputActionAsset (new Input System). Returns action " +
        "maps (name + action_count + binding_count + per-action details: " +
        "action_type, expected_control_type, interactions, processors, id), " +
        "plus control schemes (name + bindingGroup). Read via reflection — " +
        "returns a structured error if com.unity.inputsystem isn't " +
        "installed.")]
    public static async Task<JsonElement> InputActionsAssetInspect(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("input-actions-asset-inspect", args, ct);

    [McpServerTool(Name = "input-player-input-inspect"), Description(
        "Read a PlayerInput component's state: actions asset path, " +
        "default and current action map, notification behavior, player " +
        "index, split-screen index, inputIsActive. Resolve by instance_id " +
        "or gameobject_path. Package-gated like input-actions-asset-" +
        "inspect.")]
    public static async Task<JsonElement> InputPlayerInputInspect(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("input-player-input-inspect", args, ct);

    [McpServerTool(Name = "input-devices"), Description(
        "List every connected InputDevice (new Input System): type_fqn, " +
        "name, display_name, device_id, enabled, native. Useful for " +
        "diagnostics like 'is a gamepad plugged in'.")]
    public static async Task<JsonElement> InputDevices(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("input-devices", null, ct);
}
