using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class PackageServerTools
{
    [McpServerTool(Name = "package-search"), Description(
        "Search Unity packages available to Package Manager. query filters by " +
        "package name, display name, or description. include_preview=false " +
        "hides preview/experimental versions. project-packages remains the " +
        "installed-package snapshot; this tool is for discovery.")]
    public static async Task<JsonElement> PackageSearch(UnityClient unity,
        [Description("Search text matched against package name, display name, and description.")]
        string query,
        [Description("Whether preview/experimental versions should be included. Default false.")]
        bool? include_preview,
        [Description("Whether to use Package Manager offline mode. Default false.")]
        bool? offline_mode,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("package-search",
        new PackageSearchArgs(query, include_preview, offline_mode), ct);

    [McpServerTool(Name = "package-add"), Description(
        "Add a UPM package by package_name, optionally pinning a specific " +
        "version. Equivalent to editing Packages/manifest.json and letting " +
        "Unity resolve it, but with a structured response from the actual " +
        "Package Manager result.")]
    public static async Task<JsonElement> PackageAdd(UnityClient unity,
        [Description("Package id such as 'com.unity.textmeshpro'.")]
        string package_name,
        [Description("Optional version string, e.g. '3.0.6'.")]
        string? version,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("package-add",
        new PackageAddArgs(package_name, version), ct);

    [McpServerTool(Name = "package-remove"), Description(
        "Remove a direct UPM dependency from the project by package_name. " +
        "Returns whether the package still appears installed after the remove " +
        "request completes.")]
    public static async Task<JsonElement> PackageRemove(UnityClient unity,
        [Description("Package id such as 'com.unity.textmeshpro'.")]
        string package_name,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("package-remove",
        new PackageRemoveArgs(package_name), ct);
}
