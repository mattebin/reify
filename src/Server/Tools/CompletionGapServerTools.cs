using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class CompletionGapServerTools
{
    [McpServerTool(Name = "script-execute"), Description(
        "Compile and execute a small C# snippet in-memory through Roslyn on the Unity side. " +
        "Provide full source code that contains a parameterless static entrypoint, plus type_name " +
        "and method_name if you want names other than the defaults. Returns compile diagnostics " +
        "and, on success, the entrypoint return value. Main-thread execution only.")]
    public static async Task<JsonElement> ScriptExecute(
        UnityClient unity,
        string code,
        string? type_name,
        string? method_name,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "script-execute",
        new ScriptExecuteArgs(code, type_name, method_name),
        ct);

    [McpServerTool(Name = "object-get-data"), Description(
        "Read any UnityEngine.Object through SerializedObject inspection. Resolve by instance_id, " +
        "asset_path, gameobject_path, or gameobject_path + component_type. Returns stable object " +
        "identity plus serialized properties so the caller can inspect oddball engine/project types " +
        "without waiting for a bespoke domain tool.")]
    public static async Task<JsonElement> ObjectGetData(
        UnityClient unity,
        string? asset_path,
        string? gameobject_path,
        string? component_type,
        int? instance_id,
        bool? include_properties,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "object-get-data",
        new ObjectGetArgs(asset_path, gameobject_path, component_type, instance_id, include_properties),
        ct);

    [McpServerTool(Name = "object-modify"), Description(
        "Generic SerializedObject write fallback. Resolve the target the same way as object-get-data, " +
        "then apply a properties dict where keys are SerializedProperty paths and values are JSON values. " +
        "Returns ADR-002 style applied_fields receipts and failed entries for unsupported or missing paths.")]
    public static async Task<JsonElement> ObjectModify(
        UnityClient unity,
        string? asset_path,
        string? gameobject_path,
        string? component_type,
        int? instance_id,
        Dictionary<string, JsonElement> properties,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "object-modify",
        new ObjectModifyArgs(asset_path, gameobject_path, component_type, instance_id, properties),
        ct);

    [McpServerTool(Name = "scene-set-active"), Description(
        "Set one of the currently opened scenes as Unity's active scene. Expects a loaded scene path. " +
        "Returns the new active scene plus a receipt showing the active scene path before and after.")]
    public static async Task<JsonElement> SceneSetActive(
        UnityClient unity,
        string path,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "scene-set-active",
        new ScenePathArgs(path),
        ct);

    [McpServerTool(Name = "scene-unload"), Description(
        "Unload one of the currently opened scenes in the Unity editor. Requires at least two open scenes. " +
        "Returns the unloaded scene snapshot and the remaining opened scenes.")]
    public static async Task<JsonElement> SceneUnload(
        UnityClient unity,
        string path,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "scene-unload",
        new ScenePathArgs(path),
        ct);

    [McpServerTool(Name = "asset-find-built-in"), Description(
        "Best-effort search over built-in/editor resources currently loaded by Unity. Useful for finding " +
        "built-in shaders, icons, and other editor resources that are not regular project assets. Accepts " +
        "optional name/type filters and a result limit.")]
    public static async Task<JsonElement> AssetFindBuiltIn(
        UnityClient unity,
        string? name,
        string? type,
        int? limit,
        bool? include_hidden,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "asset-find-built-in",
        new AssetFindBuiltInArgs(name, type, limit, include_hidden),
        ct);

    [McpServerTool(Name = "asset-shader-list-all"), Description(
        "List shaders from both project/package assets and currently loaded built-in Unity shaders. " +
        "Returns source, path/guid when available, support state, and hidden-shader status.")]
    public static async Task<JsonElement> AssetShaderListAll(
        UnityClient unity,
        bool? include_hidden,
        int? limit,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "asset-shader-list-all",
        new AssetShaderListAllArgs(include_hidden, limit),
        ct);

    [McpServerTool(Name = "prefab-save"), Description(
        "Save the currently opened prefab stage back to its prefab asset. This is the explicit write endpoint " +
        "for prefab-mode edits and returns prefab provenance plus a small receipt about the stage dirty state.")]
    public static async Task<JsonElement> PrefabSave(
        UnityClient unity,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "prefab-save",
        null,
        ct);

    [McpServerTool(Name = "component-list-all"), Description(
        "Enumerate every loaded Component type Unity can currently resolve, including project and package types. " +
        "Optional filters: name_like, namespace_like, include_editor_only, and limit.")]
    public static async Task<JsonElement> ComponentListAll(
        UnityClient unity,
        string? name_like,
        string? namespace_like,
        int? limit,
        bool? include_editor_only,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "component-list-all",
        new ComponentListAllArgs(name_like, namespace_like, limit, include_editor_only),
        ct);
}
