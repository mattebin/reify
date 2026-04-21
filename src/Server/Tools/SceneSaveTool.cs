using System.ComponentModel;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class SceneSaveTool
{
    [McpServerTool(Name = "scene-save"), Description(
        "Save the active scene in the Unity Editor. Pass a path to save the " +
        "scene to a new location ('Save As'); omit it to save in place. " +
        "Pass save_as_copy=true with a path to write a copy without changing " +
        "the in-editor binding. Returns the post-save scene metadata; the " +
        "is_dirty field will be false on success."
    )]
    public static async Task<SceneMutationResponse> SceneSave(
        UnityClient unity,
        [Description("Optional target path for Save-As. Omit to save in place.")]
        string? path,
        [Description("If true, write a copy at 'path' without re-binding the editor. Default false.")]
        bool? save_as_copy,
        CancellationToken ct
    ) => await unity.CallAsync<SceneMutationResponse>(
        "scene-save",
        new SceneSaveArgs(path, save_as_copy),
        ct
    );
}
