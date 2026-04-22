using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Reify.Editor.Tools
{
    internal static class PackageTools
    {
        // package-search bypasses the shared RunRequestAsync helper because
        // live validation found NRE paths that weren't reachable from that
        // helper's catch points on Unity 6 — specifically around
        // request.IsCompleted / request.Status access. Inlined so every
        // step has its own try/catch and surfaces a `[step=X]` trace on
        // failure instead of a bare "Object reference not set".
        [ReifyTool("package-search")]
        public static Task<object> Search(JToken args)
        {
            var query          = args?.Value<string>("query") ?? throw new ArgumentException("query is required.");
            var includePreview = args?.Value<bool?>("include_preview") ?? false;
            var offlineMode    = args?.Value<bool?>("offline_mode") ?? false;

            // Inlined (not RunRequestAsync) so every single reference access
            // is wrapped and any residual NRE gets a traceable message.
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            _ = MainThreadDispatcher.RunAsync<object>(() =>
            {
                try
                {
                    SearchRequest request;
                    try { request = Client.SearchAll(offlineMode); }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(new InvalidOperationException(
                            $"[step=Client.SearchAll] {ex.GetType().Name}: {ex.Message}", ex));
                        return null;
                    }
                    if (request == null)
                    {
                        tcs.TrySetException(new InvalidOperationException(
                            "[step=Client.SearchAll] returned null"));
                        return null;
                    }

                    var startedAt = EditorApplication.timeSinceStartup;
                    EditorApplication.CallbackFunction poll = null;
                    poll = () =>
                    {
                        try
                        {
                            bool done;
                            try { done = request.IsCompleted; }
                            catch (Exception ex)
                            {
                                EditorApplication.update -= poll;
                                tcs.TrySetException(new InvalidOperationException(
                                    $"[step=request.IsCompleted] {ex.GetType().Name}: {ex.Message}", ex));
                                return;
                            }
                            if (!done)
                            {
                                if ((EditorApplication.timeSinceStartup - startedAt) * 1000d > 30000d)
                                {
                                    EditorApplication.update -= poll;
                                    tcs.TrySetException(new TimeoutException("package-search timed out after 30s."));
                                }
                                return;
                            }
                            EditorApplication.update -= poll;

                            StatusCode status;
                            try { status = request.Status; }
                            catch (Exception ex)
                            {
                                tcs.TrySetException(new InvalidOperationException(
                                    $"[step=request.Status] {ex.GetType().Name}: {ex.Message}", ex));
                                return;
                            }

                            if (status != StatusCode.Success)
                            {
                                var msg = "unknown";
                                try { msg = request.Error?.message ?? status.ToString(); } catch { }
                                tcs.TrySetException(new InvalidOperationException($"package-search failed: {msg}"));
                                return;
                            }

                            try
                            {
                                tcs.TrySetResult(ProjectSearchResult(request, query, includePreview, offlineMode));
                            }
                            catch (Exception ex)
                            {
                                tcs.TrySetException(new InvalidOperationException(
                                    $"[step=project] {ex.GetType().Name}: {ex.Message} " +
                                    $"at {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}", ex));
                            }
                        }
                        catch (Exception ex)
                        {
                            EditorApplication.update -= poll;
                            tcs.TrySetException(new InvalidOperationException(
                                $"[step=poll outer] {ex.GetType().Name}: {ex.Message}", ex));
                        }
                    };
                    EditorApplication.update += poll;
                    return null;
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(new InvalidOperationException(
                        $"[step=schedule] {ex.GetType().Name}: {ex.Message}", ex));
                    return null;
                }
            });

            return tcs.Task;
        }

        private static object ProjectSearchResult(SearchRequest request, string query, bool includePreview, bool offlineMode)
        {
                    // Every reference access wrapped so any residual null
                    // source gets reported with a traceable message
                    // instead of a bare "Object reference not set".
                    Dictionary<string, PackageManagerPackageInfo> installed;
                    try { installed = InstalledPackagesByName(); }
                    catch (Exception ex)
                    { throw new InvalidOperationException($"InstalledPackagesByName failed: {ex.Message}", ex); }

                    var packages = new List<object>();
                    PackageManagerPackageInfo[] results;
                    try { results = request.Result; }
                    catch (Exception ex)
                    { throw new InvalidOperationException($"request.Result access failed: {ex.Message}", ex); }

                    if (results != null)
                    {
                        for (var i = 0; i < results.Length; i++)
                        {
                            var package = results[i];
                            try
                            {
                                if (package == null) continue;
                                if (!MatchesQuery(package, query)) continue;
                                if (!includePreview && IsPreviewVersion(package.version)) continue;
                                packages.Add(PackageSummary(package, installed));
                            }
                            catch (Exception ex)
                            {
                                throw new InvalidOperationException(
                                    $"Package[{i}] '{package?.name ?? "<null>"}' processing failed: " +
                                    $"{ex.GetType().Name}: {ex.Message}", ex);
                            }
                        }
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

        private static Dictionary<string, PackageManagerPackageInfo> InstalledPackagesByName()
        {
            var map = new Dictionary<string, PackageManagerPackageInfo>(StringComparer.OrdinalIgnoreCase);
            // GetAllRegisteredPackages can return null before the first
            // package-manager registration pass completes — the symptom was
            // a raw "Object reference not set to an instance of an object"
            // from package-search.
            var installed = PackageManagerPackageInfo.GetAllRegisteredPackages();
            if (installed == null) return map;
            foreach (var package in installed)
            {
                if (package == null || string.IsNullOrEmpty(package.name)) continue;
                map[package.name] = package;
            }
            return map;
        }

        private static bool MatchesQuery(PackageManagerPackageInfo package, string query)
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

        private static object PackageSummary(PackageManagerPackageInfo package, Dictionary<string, PackageManagerPackageInfo> installed)
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
