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
    public static async Task<JsonElement> BatchExecute(
        UnityClient unity,
        [Description("Array of calls, each shaped like { tool, args? }.")]
        JsonElement calls,
        [Description("Whether the batch should stop at the first failing call.")]
        bool? stop_on_error = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("batch-execute", new
    {
        calls,
        stop_on_error
    }, ct);

    [McpServerTool(Name = "reify-tool-list"), Description(
        "Enumerate every registered reify tool, grouped by domain (first " +
        "kebab-case segment). Returns total_count, domain_count, domains[] " +
        "(name + count + tools[]), and all_tools (flat alphabetical). " +
        "Good meta-call to discover what's available before composing a " +
        "batch.")]
    public static async Task<JsonElement> ReifyToolList(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("reify-tool-list", null, ct);

    [McpServerTool(Name = "reify-version"), Description(
        "Return reify build info: package_name, assembly_name + version, " +
        "bridge_type_fqn, .NET runtime version, Unity version, registered " +
        "tool_count. Cheap health-check.")]
    public static async Task<JsonElement> ReifyVersion(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("reify-version", null, ct);

    [McpServerTool(Name = "reify-command-center-open"), Description(
        "Open the native Unity EditorWindow dashboard at Window/Reify/Command Center. " +
        "Useful when the user wants to inspect bridge health, tool inventory, config snippets, " +
        "and pending LLM issue reports from inside Unity.")]
    public static async Task<JsonElement> ReifyCommandCenterOpen(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("reify-command-center-open", null, ct);

    [McpServerTool(Name = "reflection-method-find"), Description(
        "Enumerate methods on a .NET type for reflection-driven discovery. " +
        "Args: type_name (FQN), optional method_name (exact) or name_like " +
        "(substring, case-insensitive), static_only, limit (default 100). " +
        "Returns each method's name, declaring_type, return_type, static/" +
        "public/virtual flags, and parameter list (name + type + out + " +
        "default-value info). Safe read-only - pair with reflection-method-" +
        "call when the found method should be invoked.")]
    public static async Task<JsonElement> ReflectionMethodFind(
        UnityClient unity,
        [Description("Fully qualified .NET type name.")]
        string type_name,
        [Description("Optional exact method name.")]
        string? method_name = null,
        [Description("Optional case-insensitive substring filter.")]
        string? name_like = null,
        [Description("If true, only static methods are returned.")]
        bool? static_only = null,
        [Description("Maximum number of methods to return. Default 100.")]
        int? limit = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("reflection-method-find", new
    {
        type_name,
        method_name,
        name_like,
        static_only,
        limit
    }, ct);

    [McpServerTool(Name = "reflection-method-call"), Description(
        "Invoke a method via reflection - the 'anything reify doesn't " +
        "expose natively' escape hatch. DISABLED BY DEFAULT: launch the " +
        "Unity Editor with REIFY_ALLOW_REFLECTION_CALL=1 to enable. Args: " +
        "type_name, method_name, optional parameter_types[] (FQNs) to " +
        "disambiguate overloads, arguments[] (JSON-coerced to each " +
        "parameter's type), instance_id (required for non-static methods). " +
        "Returns a stringified return value + return_type. Throws " +
        "structured errors on ambiguity, missing types, or method " +
        "exceptions.")]
    public static async Task<JsonElement> ReflectionMethodCall(
        UnityClient unity,
        [Description("Fully qualified .NET type name.")]
        string type_name,
        [Description("Method name to invoke.")]
        string method_name,
        [Description("Optional fully qualified parameter types to disambiguate overloads.")]
        JsonElement? parameter_types = null,
        [Description("Optional argument array passed positionally to the method.")]
        JsonElement? arguments = null,
        [Description("Required for non-static methods.")]
        int? instance_id = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("reflection-method-call", new
    {
        type_name,
        method_name,
        parameter_types,
        arguments,
        instance_id
    }, ct);
}
