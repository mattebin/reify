using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class EditorOpsServerTools
{
    [McpServerTool(Name = "editor-menu-execute"), Description(
        "Execute a Unity menu item by its path (e.g. 'GameObject/3D Object/" +
        "Cube', 'Assets/Refresh', 'Edit/Project Settings...'). Throws " +
        "TOOL_EXCEPTION when the path doesn't exist OR the item is disabled " +
        "in the current editor context (Unity doesn't distinguish). Escape " +
        "hatch for anything reify doesn't expose natively.")]
    public static async Task<JsonElement> EditorMenuExecute(UnityClient unity,
        string path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("editor-menu-execute", new EditorMenuExecuteArgs(path), ct);

    [McpServerTool(Name = "editor-undo"), Description(
        "Perform one step of Unity's Undo. Returns the label that was on top " +
        "of the undo stack (undone_label) and the label now on top " +
        "(new_top_label). Reverses any Undo-registered mutation — both " +
        "reify-caused and user-caused.")]
    public static async Task<JsonElement> EditorUndo(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("editor-undo", null, ct);

    [McpServerTool(Name = "editor-redo"), Description(
        "Perform one step of Unity's Redo. Returns the label now on top of " +
        "the undo stack.")]
    public static async Task<JsonElement> EditorRedo(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("editor-redo", null, ct);

    [McpServerTool(Name = "editor-undo-history"), Description(
        "Return the current undo/redo stacks as structured lists. Mini " +
        "philosophy tool — the Edit menu as JSON instead of a screenshot. " +
        "Carries current_group_index, current_group_label, and the full " +
        "undo_records + redo_records arrays.")]
    public static async Task<JsonElement> EditorUndoHistory(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("editor-undo-history", null, ct);

    [McpServerTool(Name = "editor-selection-get"), Description(
        "Read the current Selection as structured data. 'active' is the " +
        "single active object; 'selection' is every object in the multi-" +
        "select. Each item carries type_fqn, instance_id, name, and either " +
        "asset_path or gameobject_path (scene path).")]
    public static async Task<JsonElement> EditorSelectionGet(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("editor-selection-get", null, ct);

    [McpServerTool(Name = "editor-selection-set"), Description(
        "Set Selection by instance_ids and/or paths. Paths are tried first as " +
        "scene GameObject paths, then as asset paths. Returns the resolved " +
        "selection DTO and any unresolved entries (you get a record of what " +
        "didn't resolve, not silent ignore).")]
    public static async Task<JsonElement> EditorSelectionSet(UnityClient unity,
        int[]? instance_ids, string[]? paths, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("editor-selection-set",
        new EditorSelectionSetArgs(instance_ids, paths), ct);
}
