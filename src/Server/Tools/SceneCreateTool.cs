using System.ComponentModel;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class SceneCreateTool
{
    [McpServerTool(Name = "scene-create"), Description(
        "Create a new scene and save it to the given asset path. The path must " +
        "start with 'Assets/' and end in '.unity'. When setup_default is true " +
        "(the default), the scene is created with Unity's default GameObjects " +
        "(Main Camera, Directional Light); otherwise the scene is empty. " +
        "Returns the new scene's structured metadata."
    )]
    public static async Task<SceneMutationResponse> SceneCreate(
        UnityClient unity,
        [Description("Asset path for the new scene, e.g. 'Assets/Scenes/Level1.unity'")]
        string path,
        [Description("If true, populate default GameObjects; if false, create empty. Default true.")]
        bool? setup_default,
        CancellationToken ct
    ) => await unity.CallAsync<SceneMutationResponse>(
        "scene-create",
        new SceneCreateArgs(path, setup_default),
        ct
    );
}
