using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class AssetTools
{
    [McpServerTool(Name = "asset-find"), Description(
        "Search the AssetDatabase. Provide any combination of name, type " +
        "(e.g. 'Material', 'Texture', 'Prefab'), guid, or path. name+type " +
        "map to AssetDatabase.FindAssets; guid/path resolve directly. Returns " +
        "match_count and an array of {path, guid, type_fqn, name, instance_id}.")]
    public static async Task<JsonElement> AssetFind(UnityClient unity,
        string? name, string? type, string? guid, string? path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("asset-find", new AssetFindArgs(name, type, guid, path), ct);

    [McpServerTool(Name = "asset-create"), Description(
        "Create an asset at the given path. kind ∈ {'folder', 'material', " +
        "'scriptable_object'}. For 'material', optional shader (defaults to " +
        "URP/Lit). For 'scriptable_object', type_name must be a loaded " +
        "ScriptableObject subclass. Parent folders auto-created. Returns the " +
        "created asset's summary.")]
    public static async Task<JsonElement> AssetCreate(UnityClient unity,
        string kind, string path, string? type_name, string? shader, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("asset-create", new AssetCreateArgs(kind, path, type_name, shader), ct);

    [McpServerTool(Name = "asset-delete"), Description(
        "Delete an asset. Default is MoveAssetToTrash (reversible). Set " +
        "use_trash=false for permanent DeleteAsset. Includes a 'warnings' " +
        "array flagging forward dependencies; reverse-dependency scan (who " +
        "references this?) is NOT performed — budget aware.")]
    public static async Task<JsonElement> AssetDelete(UnityClient unity,
        string path, bool? use_trash, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("asset-delete", new AssetDeleteArgs(path, use_trash), ct);

    [McpServerTool(Name = "asset-get"), Description(
        "Get an asset's metadata (path, guid, type, importer, labels, forward " +
        "dependencies). Set include_properties=true to also emit every " +
        "SerializedObject property (name, type, typed value) — same shape as " +
        "component-get. Use this instead of screenshotting the Inspector.")]
    public static async Task<JsonElement> AssetGet(UnityClient unity,
        string path, bool? include_properties, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("asset-get", new AssetGetArgs(path, include_properties), ct);

    [McpServerTool(Name = "asset-rename"), Description(
        "Rename an asset in place (same folder). new_name is the leaf only, " +
        "without extension. Returns the post-rename path and summary.")]
    public static async Task<JsonElement> AssetRename(UnityClient unity,
        string path, string new_name, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("asset-rename", new AssetRenameArgs(path, new_name), ct);

    [McpServerTool(Name = "asset-move"), Description(
        "Move an asset to a new path. Parent folders of 'to' are auto-created. " +
        "Returns {from, to} and the post-move summary.")]
    public static async Task<JsonElement> AssetMove(UnityClient unity,
        string from, string to, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("asset-move", new AssetMoveArgs(from, to), ct);
}

[McpServerToolType]
public static class PrefabTools
{
    [McpServerTool(Name = "prefab-create"), Description(
        "Save a scene GameObject as a prefab asset. connect_instance=true " +
        "(default) replaces the scene object with a prefab instance; false " +
        "leaves the scene object disconnected. Returns both the prefab asset " +
        "summary and (when connected) the updated scene instance DTO.")]
    public static async Task<JsonElement> PrefabCreate(UnityClient unity,
        string gameobject_path, string asset_path, bool? connect_instance, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("prefab-create",
        new PrefabCreateArgs(gameobject_path, asset_path, connect_instance), ct);

    [McpServerTool(Name = "prefab-instantiate"), Description(
        "Instantiate a prefab into the active scene. Optional parent_path, " +
        "local position, local rotation (euler degrees). Undo-registered. " +
        "Returns the scene instance DTO.")]
    public static async Task<JsonElement> PrefabInstantiate(UnityClient unity,
        string asset_path, string? parent_path, Vec3Arg? position, Vec3Arg? rotation_euler, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("prefab-instantiate",
        new PrefabInstantiateArgs(asset_path, parent_path, position, rotation_euler), ct);

    [McpServerTool(Name = "prefab-open"), Description(
        "Open a prefab asset in isolation (Prefab Mode). Returns the stage " +
        "metadata. Use prefab-close to return to the main scene.")]
    public static async Task<JsonElement> PrefabOpen(UnityClient unity,
        string asset_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("prefab-open", new PrefabOpenArgs(asset_path), ct);

    [McpServerTool(Name = "prefab-close"), Description(
        "Leave Prefab Mode and return to the main scene. No-op if not in " +
        "Prefab Mode. Returns the asset path of the closed stage, if any.")]
    public static async Task<JsonElement> PrefabClose(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("prefab-close", null, ct);

    [McpServerTool(Name = "prefab-get-overrides"), Description(
        "Structured diff of a prefab instance vs its source asset. Returns " +
        "property_modifications (each with target_type, property_path, value, " +
        "object_reference), added_components, removed_components, and " +
        "added_gameobjects. Plus total_overrides as a fast-path count. " +
        "Mini philosophy tool — shows the override state structurally instead " +
        "of forcing the caller to read Unity's Overrides dropdown.")]
    public static async Task<JsonElement> PrefabGetOverrides(UnityClient unity,
        string gameobject_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("prefab-get-overrides",
        new PrefabGameObjectArgs(gameobject_path), ct);

    [McpServerTool(Name = "prefab-apply-overrides"), Description(
        "Apply all overrides on a prefab instance back to the source asset. " +
        "Operates on the nearest prefab instance root. Returns the applied " +
        "root's path. Prefer prefab-get-overrides first if you want to " +
        "inspect before applying.")]
    public static async Task<JsonElement> PrefabApplyOverrides(UnityClient unity,
        string gameobject_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("prefab-apply-overrides",
        new PrefabGameObjectArgs(gameobject_path), ct);

    [McpServerTool(Name = "prefab-revert-overrides"), Description(
        "Revert all overrides on a prefab instance back to the source asset " +
        "state. Operates on the nearest prefab instance root. Undo-backed.")]
    public static async Task<JsonElement> PrefabRevertOverrides(UnityClient unity,
        string gameobject_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("prefab-revert-overrides",
        new PrefabGameObjectArgs(gameobject_path), ct);
}
