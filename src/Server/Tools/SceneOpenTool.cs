using System.ComponentModel;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class SceneOpenTool
{
    [McpServerTool(Name = "scene-open"), Description(
        "Open a scene in the Unity Editor by its asset path. When additive is " +
        "true, loads the scene alongside currently open scenes; otherwise " +
        "replaces all open scenes. Returns the opened scene's structured " +
        "metadata (name, path, build_index, load/dirty/active flags, root " +
        "GameObject names) along with read_at_utc and frame timestamps."
    )]
    public static async Task<SceneMutationResponse> SceneOpen(
        UnityClient unity,
        [Description("Asset path to the scene, e.g. 'Assets/Scenes/Main.unity'")]
        string path,
        [Description("If true, load additively; otherwise replace open scenes. Default false.")]
        bool? additive,
        CancellationToken ct
    ) => await unity.CallAsync<SceneMutationResponse>(
        "scene-open",
        new SceneOpenArgs(path, additive),
        ct
    );
}
