using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server;

[McpServerResourceType]
public static class ReifyResources
{
    [McpServerResource(
        UriTemplate = "reify://about",
        Name = "reify-about",
        Title = "Reify About",
        MimeType = "application/json")]
    [Description(
        "High-level summary of the local reify server: thesis, scope, " +
        "tool/prompt/resource counts, supported clients, and bridge host/port. " +
        "Useful when orienting a client before any Unity calls.")]
    public static string About()
        => JsonSerializer.Serialize(ReifyServerCatalog.BuildSummary(), ReifyServerCatalog.Json);

    [McpServerResource(
        UriTemplate = "reify://philosophy/structured-state",
        Name = "reify-philosophy",
        Title = "Reify Philosophy",
        MimeType = "text/markdown")]
    [Description(
        "The short version of reify's operating philosophy: structured-state " +
        "first, screenshots last, read-before-write, verify-after-write, and " +
        "prefer ambiguity rejection over silent fallbacks.")]
    public static string StructuredStatePhilosophy()
        => """
           # Reify: structured-state first

           Reify is built for API agents that reason from code-shaped evidence.

           Core rules:
           - Prefer structured reads over screenshots.
           - Use stable identifiers, timestamps, and frame context.
           - Reject ambiguity instead of mutating the first match.
           - Read before write when identity or state is uncertain.
           - Verify writes by reading back the resulting state.
           - Use `batch-execute` to collect related evidence in one round trip.
           - Treat `structured-screenshot` as an opt-in escape hatch, not the default.
           - Treat `reflection-method-call` as a last resort and only when explicitly enabled.

           The goal is not "make Unity clickable through MCP". The goal is "make Unity inspectable and operable through evidence that an LLM can actually reason about."
           """;

    [McpServerResource(
        UriTemplate = "reify://tools/catalog",
        Name = "reify-tool-catalog",
        Title = "Reify Tool Catalog",
        MimeType = "application/json")]
    [Description(
        "Full server-side catalog of reify MCP tools, grouped by domain with " +
        "their descriptions and backing method names. Works even when Unity is " +
        "not currently reachable.")]
    public static string ToolCatalog()
        => JsonSerializer.Serialize(ReifyServerCatalog.BuildToolCatalog(), ReifyServerCatalog.Json);

    [McpServerResource(
        UriTemplate = "reify://tools/{name}",
        Name = "reify-tool-doc",
        Title = "Reify Tool Doc",
        MimeType = "application/json")]
    [Description(
        "Look up one reify MCP tool by name and return its description, domain, " +
        "container type, and backing method name.")]
    public static string ToolDoc(string name)
        => JsonSerializer.Serialize(
            ReifyServerCatalog.GetToolDocOrThrow(name),
            ReifyServerCatalog.Json);
}
