using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class AssetDiffServerTools
{
    [McpServerTool(Name = "asset-snapshot"), Description(
        "Capture an asset-level inventory under a folder (default 'Assets'). " +
        "Each entry: path, guid, type_fqn, length_bytes, last_write_utc. " +
        "filter is passed to AssetDatabase.FindAssets (e.g. 't:Prefab', " +
        "'t:Material Glow'). Use before an import/move/refactor and feed " +
        "the result to asset-diff to prove exactly which assets the " +
        "operation touched.")]
    public static async Task<JsonElement> AssetSnapshot(
        UnityClient unity,
        string? folder = null,
        string? filter = null,
        bool? include_length_bytes = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("asset-snapshot", new
    {
        folder, filter, include_length_bytes
    }, ct);

    [McpServerTool(Name = "asset-diff"), Description(
        "Compare a prior asset-snapshot against the current AssetDatabase " +
        "state. Returns: " +
        "\n" +
        "  - added[]: guid present now, absent in snapshot. " +
        "\n" +
        "  - removed[]: guid in snapshot, absent now. " +
        "\n" +
        "  - moved[]: same guid, path changed (from/to). " +
        "\n" +
        "  - modified[]: same guid + path, length_bytes or last_write_utc " +
        "changed — evidence of a rewrite. " +
        "\n\n" +
        "folder and filter default to the snapshot's values if omitted. " +
        "Same philosophy as scene-diff: prove what the write did instead " +
        "of a screenshot.")]
    public static async Task<JsonElement> AssetDiff(
        UnityClient unity,
        JsonElement before_snapshot,
        string? folder = null,
        string? filter = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("asset-diff", new
    {
        before_snapshot, folder, filter
    }, ct);
}
