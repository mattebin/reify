using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class LightingDiagnosticServerTool
{
    [McpServerTool(Name = "lighting-diagnostic"), Description(
        "8th Phase C philosophy tool. Full structured state of a scene's " +
        "lighting: skybox material + shader, ambient mode/intensity/color, " +
        "reflection source + intensity + bounces, fog config, every Light " +
        "with type/mode/intensity/shadows/color, light probe group + probe " +
        "counts, reflection probe counts (baked vs realtime), lightmapper + " +
        "GI flags, URP Volumes with profile + priority + weight. " +
        "\n\n" +
        "Warnings flag the classic blind spots: no directional light, " +
        "ambient = Skybox but null skybox, reflection = Skybox but no " +
        "skybox, shader-compile failure on the skybox, fog enabled with " +
        "zero density, missing probe coverage, unassigned RenderSettings.sun. " +
        "\n\n" +
        "URP Volume component is read via reflection so reify carries no " +
        "URP package dependency. Built-in render pipeline projects get the " +
        "same response with an empty urp_volumes array."
    )]
    public static async Task<JsonElement> LightingDiagnostic(UnityClient unity,
        [Description("Scene asset path. Omit for active scene.")]
        string? scene_path,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("lighting-diagnostic",
        new LightingDiagnosticArgs(scene_path), ct);
}
