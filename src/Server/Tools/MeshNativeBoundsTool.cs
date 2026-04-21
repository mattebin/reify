using System.ComponentModel;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class MeshNativeBoundsTool
{
    [McpServerTool(Name = "mesh-native-bounds"), Description(
        "Return a mesh's NATIVE (unscaled) geometric state — bounds, vertex " +
        "and triangle counts, attribute presence, readability, and import " +
        "scale factor — so the caller never has to guess scale from a " +
        "screenshot. Optionally also returns post-scale world-space bounds " +
        "when the source is a scene GameObject. " +
        "\n\n" +
        "Resolve by exactly one of: asset_path (to a .fbx / .obj / .mesh or " +
        "similar), gameobject_path (scene path like 'Root/Child'), or " +
        "instance_id (Unity's stable object ID). " +
        "\n\n" +
        "The 'warnings' array is the killer feature: meshes smaller than " +
        "0.1m or larger than 100m, non-unit import scale factors, unreadable " +
        "meshes, and renderer.bounds vs mesh.bounds × lossyScale mismatches " +
        "are all flagged. Use these to reason about placement before acting."
    )]
    public static async Task<MeshNativeBoundsResponse> MeshNativeBounds(
        UnityClient unity,
        [Description("Asset path (e.g. 'Assets/Models/Market.fbx'). Exactly one of asset_path / gameobject_path / instance_id.")]
        string? asset_path,
        [Description("Scene GameObject path (e.g. 'Environment/Buildings/Market').")]
        string? gameobject_path,
        [Description("Stable Unity instance id for a Mesh or GameObject.")]
        int? instance_id,
        CancellationToken ct
    ) => await unity.CallAsync<MeshNativeBoundsResponse>(
        "mesh-native-bounds",
        new MeshNativeBoundsArgs(asset_path, gameobject_path, instance_id),
        ct
    );
}
