using System.ComponentModel;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class PingTool
{
    [McpServerTool(Name = "ping"), Description(
        "Verify the Reify bridge to Unity Editor is alive. Returns the Unity " +
        "version, project name and path, current platform target, whether " +
        "play mode or a domain-reload compile is active, current frame " +
        "counter, and a UTC read timestamp. Cheap — call freely as a health check."
    )]
    public static async Task<PingResponse> Ping(
        UnityClient unity,
        CancellationToken ct
    ) => await unity.CallAsync<PingResponse>("ping", args: null, ct);
}
