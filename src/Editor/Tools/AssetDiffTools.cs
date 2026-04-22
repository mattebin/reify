using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Asset-level snapshot + diff. Captures every asset under a folder
    /// as {path, guid, type_fqn, length_bytes, last_write_utc} then
    /// compares two such inventories. Use to prove exactly which assets
    /// an import / move / refactor actually touched.
    /// </summary>
    internal static class AssetDiffTools
    {
        // ---------- asset-snapshot ----------
        [ReifyTool("asset-snapshot")]
        public static Task<object> Snapshot(JToken args)
        {
            var folder = args?.Value<string>("folder") ?? "Assets";
            var pattern = args?.Value<string>("filter");
            var includeBytes = args?.Value<bool?>("include_length_bytes") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (!AssetDatabase.IsValidFolder(folder))
                    throw new ArgumentException($"folder '{folder}' is not a valid AssetDatabase folder.");

                var guids = string.IsNullOrEmpty(pattern)
                    ? AssetDatabase.FindAssets("", new[] { folder })
                    : AssetDatabase.FindAssets(pattern, new[] { folder });

                var entries = new List<object>(guids.Length);
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;
                    if (AssetDatabase.IsValidFolder(path)) continue; // skip folders — we want leaf assets

                    long len = 0;
                    string lastWrite = null;
                    if (includeBytes && File.Exists(path))
                    {
                        try
                        {
                            var fi = new FileInfo(path);
                            len = fi.Length;
                            lastWrite = fi.LastWriteTimeUtc.ToString("o");
                        }
                        catch { }
                    }

                    var typeObj = AssetDatabase.GetMainAssetTypeAtPath(path);
                    entries.Add(new
                    {
                        path            = path,
                        guid            = guid,
                        type_fqn        = typeObj != null ? typeObj.FullName : null,
                        length_bytes    = includeBytes ? len : 0,
                        last_write_utc  = lastWrite
                    });
                }

                return new
                {
                    folder               = folder,
                    filter               = pattern,
                    include_length_bytes = includeBytes,
                    asset_count          = entries.Count,
                    assets               = entries.ToArray(),
                    read_at_utc          = DateTime.UtcNow.ToString("o"),
                    frame                = (long)Time.frameCount
                };
            });
        }

        // ---------- asset-diff ----------
        [ReifyTool("asset-diff")]
        public static Task<object> Diff(JToken args)
        {
            var before = args?["before_snapshot"] as JObject
                ?? throw new ArgumentException("before_snapshot is required (pass a prior asset-snapshot result).");

            var folder = args?.Value<string>("folder") ?? before.Value<string>("folder") ?? "Assets";
            var pattern = args?.Value<string>("filter") ?? before.Value<string>("filter");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                // Index before by guid AND by path.
                var beforeAssets = before["assets"] as JArray
                    ?? throw new ArgumentException("before_snapshot.assets is missing or not an array.");

                var beforeByGuid = new Dictionary<string, JObject>();
                var beforeByPath = new Dictionary<string, JObject>();
                foreach (JObject a in beforeAssets.OfType<JObject>())
                {
                    var g = a.Value<string>("guid");
                    var p = a.Value<string>("path");
                    if (!string.IsNullOrEmpty(g)) beforeByGuid[g] = a;
                    if (!string.IsNullOrEmpty(p)) beforeByPath[p] = a;
                }

                // Build current snapshot inline.
                if (!AssetDatabase.IsValidFolder(folder))
                    throw new ArgumentException($"folder '{folder}' is not a valid AssetDatabase folder.");
                var guids = string.IsNullOrEmpty(pattern)
                    ? AssetDatabase.FindAssets("", new[] { folder })
                    : AssetDatabase.FindAssets(pattern, new[] { folder });

                var currentByGuid = new Dictionary<string, JObject>();
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) continue;
                    long len = 0; string lastWrite = null;
                    if (File.Exists(path))
                    {
                        try { var fi = new FileInfo(path); len = fi.Length; lastWrite = fi.LastWriteTimeUtc.ToString("o"); } catch { }
                    }
                    var typeObj = AssetDatabase.GetMainAssetTypeAtPath(path);
                    currentByGuid[guid] = JObject.FromObject(new
                    {
                        path,
                        guid,
                        type_fqn        = typeObj?.FullName,
                        length_bytes    = len,
                        last_write_utc  = lastWrite
                    });
                }

                var added = new List<object>();
                var removed = new List<object>();
                var moved = new List<object>();
                var modified = new List<object>();

                foreach (var kvp in currentByGuid)
                {
                    if (!beforeByGuid.TryGetValue(kvp.Key, out var beforeAsset))
                    {
                        added.Add(kvp.Value);
                        continue;
                    }

                    var beforePath = beforeAsset.Value<string>("path");
                    var afterPath  = kvp.Value.Value<string>("path");
                    if (!string.Equals(beforePath, afterPath, StringComparison.Ordinal))
                    {
                        moved.Add(new { guid = kvp.Key, from = beforePath, to = afterPath });
                    }

                    var beforeLen = beforeAsset.Value<long?>("length_bytes") ?? 0;
                    var afterLen  = kvp.Value.Value<long?>("length_bytes") ?? 0;
                    var beforeWrite = beforeAsset.Value<string>("last_write_utc");
                    var afterWrite  = kvp.Value.Value<string>("last_write_utc");

                    if (beforeLen != afterLen ||
                        !string.Equals(beforeWrite, afterWrite, StringComparison.Ordinal))
                    {
                        modified.Add(new
                        {
                            guid = kvp.Key,
                            path = afterPath,
                            length_bytes   = new { before = beforeLen, after = afterLen },
                            last_write_utc = new { before = beforeWrite, after = afterWrite }
                        });
                    }
                }

                foreach (var kvp in beforeByGuid)
                {
                    if (!currentByGuid.ContainsKey(kvp.Key))
                    {
                        removed.Add(new
                        {
                            guid = kvp.Key,
                            path = kvp.Value.Value<string>("path"),
                            type_fqn = kvp.Value.Value<string>("type_fqn")
                        });
                    }
                }

                return new
                {
                    folder         = folder,
                    filter         = pattern,
                    added_count    = added.Count,
                    removed_count  = removed.Count,
                    moved_count    = moved.Count,
                    modified_count = modified.Count,
                    added          = added.ToArray(),
                    removed        = removed.ToArray(),
                    moved          = moved.ToArray(),
                    modified       = modified.ToArray(),
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }
    }
}
