using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Reify.Server;

var builder = Host.CreateApplicationBuilder(args);
var assembly = typeof(UnityClient).Assembly;

builder.Logging.AddConsole(o =>
{
    // MCP servers on stdio must not write non-protocol bytes to stdout.
    o.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<UnityClient>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(assembly)
    .WithResourcesFromAssembly(assembly)
    .WithPromptsFromAssembly(assembly);

await builder.Build().RunAsync();
