using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class ScriptServerTools
{
    [McpServerTool(Name = "script-read"), Description(
        "Read a C# script under Assets/. Returns structured code evidence " +
        "(guid, SHA-256, line count, namespace, using directives, declared " +
        "types, file-name/type-name match warnings) and optionally the raw " +
        "content. Use this instead of browsing code visually in Unity's " +
        "Inspector or Project window.")]
    public static async Task<JsonElement> ScriptRead(
        UnityClient unity,
        [Description("Asset path under Assets/, e.g. 'Assets/Scripts/PlayerController.cs'.")]
        string asset_path,
        [Description("Include full file content in the response. Defaults to true.")]
        bool? include_content,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "script-read",
        new ScriptReadArgs(asset_path, include_content),
        ct);

    [McpServerTool(Name = "script-update-or-create"), Description(
        "Create or overwrite a C# script under Assets/. Parent folders are " +
        "auto-created. Returns before/after structured code evidence " +
        "(including SHA-256, declarations, namespace, warnings) so the write " +
        "is verified by code-state read-back rather than trust. Writing a " +
        ".cs file usually triggers Unity recompilation asynchronously.")]
    public static async Task<JsonElement> ScriptUpdateOrCreate(
        UnityClient unity,
        [Description("Asset path under Assets/, e.g. 'Assets/Scripts/PlayerController.cs'.")]
        string asset_path,
        [Description("Complete file content to write.")]
        string content,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "script-update-or-create",
        new ScriptUpdateOrCreateArgs(asset_path, content),
        ct);

    [McpServerTool(Name = "script-delete"), Description(
        "Delete a C# script under Assets/. Default is MoveAssetToTrash " +
        "(reversible). Returns the deleted script's last known structured " +
        "code evidence snapshot so the caller has a durable record of what " +
        "was removed. Deleting a .cs file usually triggers recompilation.")]
    public static async Task<JsonElement> ScriptDelete(
        UnityClient unity,
        [Description("Asset path under Assets/, e.g. 'Assets/Scripts/PlayerController.cs'.")]
        string asset_path,
        [Description("If true or omitted, move to trash. If false, permanently delete.")]
        bool? use_trash,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "script-delete",
        new ScriptDeleteArgs(asset_path, use_trash),
        ct);
}
