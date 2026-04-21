using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class MetaBatchServerTools
{
    [McpServerTool(Name = "batch-execute"), Description(
        "Run multiple reify tool calls in a single round trip. Args: " +
        "calls[] (each { tool, args? }), stop_on_error (default false). " +
        "Returns {requested, executed, success_count, failure_count, " +
        "results[]} where each result is {tool, ok, data OR error}. " +
        "Per-call failures don't abort the batch unless stop_on_error=true. " +
        "Huge latency win for agent loops that orchestrate many reads.")]
    public static async Task<JsonElement> BatchExecute(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("batch-execute", args, ct);

    [McpServerTool(Name = "reify-tool-list"), Description(
        "Enumerate every registered reify tool, grouped by domain (first " +
        "kebab-case segment). Returns total_count, domain_count, domains[] " +
        "(name + count + tools[]), and all_tools (flat alphabetical). " +
        "Good meta-call to discover what's available before composing a " +
        "batch.")]
    public static async Task<JsonElement> ReifyToolList(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("reify-tool-list", null, ct);

    [McpServerTool(Name = "reify-version"), Description(
        "Return reify build info: package_name, assembly_name + version, " +
        "bridge_type_fqn, .NET runtime version, Unity version, registered " +
        "tool_count. Cheap health-check.")]
    public static async Task<JsonElement> ReifyVersion(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("reify-version", null, ct);

    [McpServerTool(Name = "reflection-method-find"), Description(
        "Enumerate methods on a .NET type for reflection-driven discovery. " +
        "Args: type_name (FQN), optional method_name (exact) or name_like " +
        "(substring, case-insensitive), static_only, limit (default 100). " +
        "Returns each method's name, declaring_type, return_type, static/" +
        "public/virtual flags, and parameter list (name + type + out + " +
        "default-value info). Safe read-only — pair with reflection-method-" +
        "call when the found method should be invoked.")]
    public static async Task<JsonElement> ReflectionMethodFind(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("reflection-method-find", args, ct);

    [McpServerTool(Name = "reflection-method-call"), Description(
        "Invoke a method via reflection — the 'anything reify doesn't " +
        "expose natively' escape hatch. DISABLED BY DEFAULT: set the " +
        "server env var REIFY_ALLOW_REFLECTION_CALL=1 to enable. Args: " +
        "type_name, method_name, optional parameter_types[] (FQNs) to " +
        "disambiguate overloads, arguments[] (JSON-coerced to each " +
        "parameter's type), instance_id (required for non-static methods). " +
        "Returns a stringified return value + return_type. Throws " +
        "structured errors on ambiguity, missing types, or method " +
        "exceptions.")]
    public static async Task<JsonElement> ReflectionMethodCall(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("reflection-method-call", args, ct);
}
