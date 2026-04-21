using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class PersistenceStatusServerTool
{
    [McpServerTool(Name = "persistence-status"), Description(
        "10th Phase C philosophy tool. What would be lost if Unity crashed " +
        "right now? Walks open scenes for isDirty, walks loaded Unity " +
        "objects for dirty assets (bounded to 50,000 objects). Returns " +
        "any_dirty as the single-flag gate for 'should I save first?'. " +
        "\n\n" +
        "Warnings highlight dirty active scene (save before domain reload), " +
        "total scene and asset counts, and an overflow warning if the " +
        "object-scan cap was hit. Project-settings-dirty tracking isn't " +
        "directly exposed by Unity — the tool notes this limitation.")]
    public static async Task<JsonElement> PersistenceStatus(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("persistence-status", null, ct);
}
