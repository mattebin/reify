using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server;

internal sealed record ReifyToolDoc(
    string Name,
    string Domain,
    string Description,
    string ContainerType,
    string MethodName);

internal sealed record ReifyPromptDoc(
    string Name,
    string Description,
    string ContainerType,
    string MethodName);

internal sealed record ReifyResourceDoc(
    string Name,
    string UriTemplate,
    string Description,
    string MimeType,
    string ContainerType,
    string MemberName,
    bool IsTemplated);

internal static class ReifyServerCatalog
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly Lazy<IReadOnlyList<ReifyToolDoc>> ToolDocs = new(DiscoverTools);
    private static readonly Lazy<IReadOnlyList<ReifyPromptDoc>> PromptDocs = new(DiscoverPrompts);
    private static readonly Lazy<IReadOnlyList<ReifyResourceDoc>> ResourceDocs = new(DiscoverResources);

    public static IReadOnlyList<ReifyToolDoc> GetToolDocs() => ToolDocs.Value;
    public static IReadOnlyList<ReifyPromptDoc> GetPromptDocs() => PromptDocs.Value;
    public static IReadOnlyList<ReifyResourceDoc> GetResourceDocs() => ResourceDocs.Value;

    public static object BuildSummary()
    {
        var tools = GetToolDocs();
        var prompts = GetPromptDocs();
        var resources = GetResourceDocs();

        return new
        {
            server_name = "reify",
            package_name = "com.reify.unity",
            thesis = "Structured state for Unity, for LLMs that reason.",
            scope = new
            {
                unity = "Editor-only (runtime deferred)",
                transport = "MCP stdio + localhost Unity bridge",
                reflection_call = "opt-in via REIFY_ALLOW_REFLECTION_CALL=1"
            },
            counts = new
            {
                tools = tools.Count,
                prompts = prompts.Count,
                resources = resources.Count,
                tool_domains = tools.Select(t => t.Domain).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            },
            client_support = new[]
            {
                "Claude Code",
                "Cursor",
                "Windsurf",
                "VS Code MCP"
            },
            bridge = new
            {
                host = Environment.GetEnvironmentVariable("REIFY_BRIDGE_HOST") ?? "127.0.0.1",
                port = Environment.GetEnvironmentVariable("REIFY_BRIDGE_PORT") ?? "17777"
            }
        };
    }

    public static object BuildToolCatalog()
    {
        var tools = GetToolDocs();

        return new
        {
            total_count = tools.Count,
            domain_count = tools.Select(t => t.Domain).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            domains = tools
                .GroupBy(t => t.Domain, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    name = g.Key,
                    count = g.Count(),
                    tools = g
                        .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(t => new
                        {
                            t.Name,
                            t.Description,
                            t.ContainerType,
                            method_name = t.MethodName
                        })
                })
        };
    }

    public static ReifyToolDoc GetToolDocOrThrow(string name)
    {
        var match = GetToolDocs().FirstOrDefault(
            t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        return match ?? throw new InvalidOperationException(
            $"No server tool named '{name}' is registered.");
    }

    private static IReadOnlyList<ReifyToolDoc> DiscoverTools()
        => DiscoverAttributedMethods<McpServerToolTypeAttribute, McpServerToolAttribute>()
            .Select(x => new ReifyToolDoc(
                Name: string.IsNullOrWhiteSpace(x.Attribute.Name) ? x.Method.Name : x.Attribute.Name,
                Domain: DomainOf(string.IsNullOrWhiteSpace(x.Attribute.Name) ? x.Method.Name : x.Attribute.Name),
                Description: x.Method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
                ContainerType: x.Method.DeclaringType?.FullName ?? string.Empty,
                MethodName: x.Method.Name))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<ReifyPromptDoc> DiscoverPrompts()
        => DiscoverAttributedMethods<McpServerPromptTypeAttribute, McpServerPromptAttribute>()
            .Select(x => new ReifyPromptDoc(
                Name: string.IsNullOrWhiteSpace(x.Attribute.Name) ? x.Method.Name : x.Attribute.Name,
                Description: x.Method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
                ContainerType: x.Method.DeclaringType?.FullName ?? string.Empty,
                MethodName: x.Method.Name))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<ReifyResourceDoc> DiscoverResources()
        => DiscoverAttributedMethods<McpServerResourceTypeAttribute, McpServerResourceAttribute>()
            .Select(x =>
            {
                var uriTemplate = string.IsNullOrWhiteSpace(x.Attribute.UriTemplate)
                    ? $"{x.Attribute.Name ?? x.Method.Name}"
                    : x.Attribute.UriTemplate;

                return new ReifyResourceDoc(
                    Name: string.IsNullOrWhiteSpace(x.Attribute.Name) ? x.Method.Name : x.Attribute.Name,
                    UriTemplate: uriTemplate,
                    Description: x.Method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
                    MimeType: x.Attribute.MimeType ?? "text/plain",
                    ContainerType: x.Method.DeclaringType?.FullName ?? string.Empty,
                    MemberName: x.Method.Name,
                    IsTemplated: uriTemplate.Contains('{') && uriTemplate.Contains('}')
                );
            })
            .OrderBy(r => r.UriTemplate, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<(MethodInfo Method, TMemberAttribute Attribute)>
        DiscoverAttributedMethods<TTypeAttribute, TMemberAttribute>()
        where TTypeAttribute : Attribute
        where TMemberAttribute : Attribute
    {
        var assembly = typeof(UnityClient).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<TTypeAttribute>() is null)
            {
                continue;
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                var attribute = method.GetCustomAttribute<TMemberAttribute>();
                if (attribute is not null)
                {
                    yield return (method, attribute);
                }
            }
        }
    }

    private static string DomainOf(string toolName)
    {
        var split = toolName.Split('-', 2, StringSplitOptions.RemoveEmptyEntries);
        return split.Length == 0 ? "misc" : split[0];
    }
}
