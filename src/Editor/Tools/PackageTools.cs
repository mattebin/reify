using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class PackageTools
    {
        [ReifyTool("package-search")]
        public static Task<object> Search(JToken args)
        {
            var query          = args?.Value<string>("query") ?? throw new ArgumentException("query is required.");
            var includePreview = args?.Value<bool?>("include_preview") ?? false;
            var offlineMode    = args?.Value<bool?>("offline_mode") ?? false;

            return RunRequestAsync(
                () => Client.SearchAll(offlineMode),
                request =>
                {
                    var installed = InstalledPackagesByName();
                    var packages = new List<object>();
                    foreach (var package in request.Result)
                    {
                        if (!MatchesQuery(package, query)) continue;
                        if (!includePreview && IsPreviewVersion(package.version)) continue;
                        packages.Add(PackageSummary(package, installed));
                    }

                    return new
                    {
                        query,
                        include_preview = includePreview,
                        offline_mode    = offlineMode,
                        match_count     = packages.Count,
                        packages        = packages.ToArray(),
                        read_at_utc     = DateTime.UtcNow.ToString("o"),
                        frame           = (long)Time.frameCount
                    };
                },
                "package-search");
        }

        [ReifyTool("package-add")]
        public static Task<object> Add(JToken args)
        {
            var packageName = args?.Value<string>("package_name") ?? throw new ArgumentException("package_name is required.");
            var version     = args?.Value<string>("version");
            var spec        = string.IsNullOrWhiteSpace(version) ? packageName : $"{packageName}@{version}";

            return RunRequestAsync(
                () => Client.Add(spec),
                request =>
                {
                    AssetDatabase.Refresh();
                    var installed = InstalledPackagesByName();
                    return new
                    {
                        requested   = spec,
                        package     = PackageSummary(request.Result, installed),
                        read_at_utc = DateTime.UtcNow.ToString("o"),
                        frame       = (long)Time.frameCount
                    };
                },
                "package-add",
                timeoutMs: 60000);
        }

        [ReifyTool("package-remove")]
        public static Task<object> Remove(JToken args)
        {
            var packageName = args?.Value<string>("package_name") ?? throw new ArgumentException("package_name is required.");

            return RunRequestAsync(
                () => Client.Remove(packageName),
                _ =>
                {
                    AssetDatabase.Refresh();
                    var installed = InstalledPackagesByName();
                    var stillInstalled = installed.TryGetValue(packageName, out var package);
                    return new
                    {
                        removed = new
                        {
                            package_name = packageName,
                            still_installed = stillInstalled
                        },
                        package     = stillInstalled ? PackageSummary(package, installed) : null,
                        read_at_utc = DateTime.UtcNow.ToString("o"),
                        frame       = (long)Time.frameCount
                    };
                },
                "package-remove",
                timeoutMs: 60000);
        }

        private static Task<object> RunRequestAsync<TRequest>(
            Func<TRequest> start,
            Func<TRequest, object> project,
            string operation,
            int timeoutMs = 30000)
            where TRequest : Request
        {
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            _ = MainThreadDispatcher.RunAsync<object>(() =>
            {
                var request = start() ?? throw new InvalidOperationException($"{operation} did not create a request.");
                var startedAt = EditorApplication.timeSinceStartup;
                EditorApplication.CallbackFunction poll = null;

                poll = () =>
                {
                    if (request.IsCompleted)
                    {
                        EditorApplication.update -= poll;
                        try
                        {
                            if (request.Status == StatusCode.Success)
                            {
                                tcs.TrySetResult(project(request));
                            }
                            else
                            {
                                tcs.TrySetException(new InvalidOperationException(BuildRequestError(operation, request)));
                            }
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                        return;
                    }

                    if ((EditorApplication.timeSinceStartup - startedAt) * 1000d > timeoutMs)
                    {
                        EditorApplication.update -= poll;
                        tcs.TrySetException(new TimeoutException($"{operation} timed out after {timeoutMs} ms."));
                    }
                };

                EditorApplication.update += poll;
                return null;
            }).ContinueWith(task =>
            {
                if (task.IsFaulted)
                    tcs.TrySetException(task.Exception?.GetBaseException() ?? new InvalidOperationException($"{operation} failed."));
                else if (task.IsCanceled)
                    tcs.TrySetCanceled();
            }, TaskScheduler.Default);

            return tcs.Task;
        }

        private static string BuildRequestError(string operation, Request request)
        {
            var message = request.Error != null && !string.IsNullOrEmpty(request.Error.message)
                ? request.Error.message
                : request.Status.ToString();
            return $"{operation} failed: {message}";
        }

        private static Dictionary<string, PackageInfo> InstalledPackagesByName()
        {
            var map = new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase);
            var installed = PackageInfo.GetAllRegisteredPackages();
            foreach (var package in installed)
                map[package.name] = package;
            return map;
        }

        private static bool MatchesQuery(PackageInfo package, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            return Contains(package.name, query) ||
                   Contains(package.displayName, query) ||
                   Contains(package.description, query);
        }

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) &&
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsPreviewVersion(string version) =>
            !string.IsNullOrEmpty(version) &&
            (version.IndexOf("-preview", StringComparison.OrdinalIgnoreCase) >= 0 ||
             version.IndexOf("-pre", StringComparison.OrdinalIgnoreCase) >= 0 ||
             version.IndexOf("-exp", StringComparison.OrdinalIgnoreCase) >= 0);

        private static object PackageSummary(PackageInfo package, Dictionary<string, PackageInfo> installed)
        {
            var isInstalled = installed.TryGetValue(package.name, out var installedInfo);
            return new
            {
                name              = package.name,
                display_name      = package.displayName,
                version           = package.version,
                source            = package.source.ToString(),
                description       = package.description,
                is_direct_dep     = isInstalled && installedInfo.isDirectDependency,
                is_installed      = isInstalled,
                installed_version = isInstalled ? installedInfo.version : null
            };
        }
    }
}
