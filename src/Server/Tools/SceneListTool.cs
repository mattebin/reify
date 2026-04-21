using System.ComponentModel;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class SceneListTool
{
    [McpServerTool(Name = "scene-list"), Description(
        "List every scene currently open in the Unity Editor. For each scene " +
        "returns its asset path, display name, build-settings index (or -1 if " +
        "not in build settings), whether it is loaded, whether it is dirty " +
        "(has unsaved changes), whether it is the active scene, and the names " +
        "of its root GameObjects. Structured-state first — the caller should " +
        "never need a screenshot to answer 'which scenes are open'."
    )]
    public static async Task<SceneListResponse> SceneList(
        UnityClient unity,
        CancellationToken ct
    ) => await unity.CallAsync<SceneListResponse>("scene-list", args: null, ct);
}
