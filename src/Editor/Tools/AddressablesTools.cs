using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Addressables surface via reflection so reify stays free of a hard
    /// dependency on `com.unity.addressables`. Covers settings, groups,
    /// entries, and the content-build pipeline.
    ///
    /// When the package isn't loaded every tool returns a structured
    /// PACKAGE_NOT_FOUND-style error rather than failing to compile.
    /// </summary>
    internal static class AddressablesTools
    {
        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName, false); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        private static object GetSettings()
        {
            var t = FindType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject")
                ?? throw new InvalidOperationException(
                    "AddressableAssetSettings not loaded — install `com.unity.addressables` to use Addressables tools.");
            var prop = t.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
            var settings = prop?.GetValue(null)
                ?? throw new InvalidOperationException(
                    "AddressableAssetSettings.Settings is null — project has the package installed but no " +
                    "settings asset. Open Window > Asset Management > Addressables > Groups once.");
            return settings;
        }

        // ---------- addressables-settings ----------
        [ReifyTool("addressables-settings")]
        public static Task<object> SettingsInspect(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var settings = GetSettings();
                var t = settings.GetType();

                var groups = (IEnumerable)t.GetProperty("groups")?.GetValue(settings);
                var groupCount = 0;
                foreach (var _g in groups ?? new object[0]) groupCount++;

                var labels = (IEnumerable)t.GetProperty("GetLabels")?.GetValue(settings)
                            ?? (IEnumerable)t.GetMethod("GetLabels", Type.EmptyTypes)?.Invoke(settings, null);
                var labelCount = 0;
                if (labels != null) foreach (var _l in labels) labelCount++;

                var profileSettings = t.GetProperty("profileSettings")?.GetValue(settings);
                var activeProfileId = t.GetProperty("activeProfileId")?.GetValue(settings) as string;

                return new
                {
                    settings_asset_path = AssetDatabase.GetAssetPath(settings as UnityEngine.Object),
                    group_count         = groupCount,
                    label_count         = labelCount,
                    active_profile_id   = activeProfileId,
                    build_remote_catalog = (bool?)(t.GetProperty("BuildRemoteCatalog")?.GetValue(settings)),
                    disable_catalog_update_on_startup =
                        (bool?)(t.GetProperty("DisableCatalogUpdateOnStartup")?.GetValue(settings)),
                    profile_settings_type_fqn = profileSettings?.GetType().FullName,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- addressables-group-list ----------
        [ReifyTool("addressables-group-list")]
        public static Task<object> GroupList(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var settings = GetSettings();
                var groups = (IEnumerable)settings.GetType().GetProperty("groups")?.GetValue(settings)
                    ?? throw new InvalidOperationException("settings.groups is null");

                var list = new List<object>();
                foreach (var g in groups)
                {
                    if (g == null) continue;
                    var gt = g.GetType();
                    var entries = gt.GetProperty("entries")?.GetValue(g) as ICollection;
                    list.Add(new
                    {
                        name             = gt.GetProperty("Name")?.GetValue(g) as string
                                           ?? gt.GetProperty("name")?.GetValue(g) as string,
                        guid             = gt.GetProperty("Guid")?.GetValue(g) as string,
                        read_only        = (bool?)(gt.GetProperty("ReadOnly")?.GetValue(g)),
                        is_default_group = (bool?)(gt.GetProperty("Default")?.GetValue(g)),
                        entry_count      = entries?.Count ?? 0,
                        schema_types     = ListSchemaTypes(g)
                    });
                }

                return new
                {
                    group_count = list.Count,
                    groups      = list.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- addressables-entry-list ----------
        [ReifyTool("addressables-entry-list")]
        public static Task<object> EntryList(JToken args)
        {
            var groupName = args?.Value<string>("group_name");
            var label     = args?.Value<string>("label");
            var limit     = args?.Value<int?>("limit") ?? 500;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var settings = GetSettings();
                var groups = (IEnumerable)settings.GetType().GetProperty("groups")?.GetValue(settings);

                var hits = new List<object>();
                var truncated = false;
                foreach (var g in groups ?? new object[0])
                {
                    if (g == null) continue;
                    var gt = g.GetType();
                    var gname = gt.GetProperty("Name")?.GetValue(g) as string ?? gt.GetProperty("name")?.GetValue(g) as string;
                    if (!string.IsNullOrEmpty(groupName) && gname != groupName) continue;

                    var entries = (IEnumerable)gt.GetProperty("entries")?.GetValue(g);
                    if (entries == null) continue;
                    foreach (var e in entries)
                    {
                        if (hits.Count >= limit) { truncated = true; break; }
                        var et = e.GetType();
                        var labelsCol = et.GetProperty("labels")?.GetValue(e) as ICollection;
                        var entryLabels = new List<string>();
                        if (labelsCol != null)
                            foreach (var l in labelsCol) entryLabels.Add(l?.ToString());

                        if (!string.IsNullOrEmpty(label) && !entryLabels.Contains(label)) continue;

                        hits.Add(new
                        {
                            address        = et.GetProperty("address")?.GetValue(e) as string,
                            guid           = et.GetProperty("guid")?.GetValue(e) as string,
                            asset_path     = et.GetProperty("AssetPath")?.GetValue(e) as string,
                            main_asset_type = (et.GetProperty("MainAssetType")?.GetValue(e) as Type)?.FullName,
                            group_name     = gname,
                            labels         = entryLabels.ToArray(),
                            is_sub_asset   = (bool?)(et.GetProperty("IsSubAsset")?.GetValue(e))
                        });
                    }
                    if (truncated) break;
                }

                return new
                {
                    group_filter = groupName,
                    label_filter = label,
                    entry_count  = hits.Count,
                    truncated    = truncated,
                    entries      = hits.ToArray(),
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        // ---------- addressables-entry-set ----------
        [ReifyTool("addressables-entry-set")]
        public static Task<object> EntrySet(JToken args)
        {
            var assetPath = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");
            var makeAddressable = args?.Value<bool?>("make_addressable") ?? true;
            var address  = args?.Value<string>("address");
            var group    = args?.Value<string>("group_name");
            var label    = args?.Value<string>("label");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var settings = GetSettings();
                var st = settings.GetType();
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                    throw new InvalidOperationException($"Asset not found: {assetPath}");

                // Capture the before-state.
                var findEntryMethod = st.GetMethod("FindAssetEntry", new[] { typeof(string) });
                var beforeEntry = findEntryMethod?.Invoke(settings, new object[] { guid });
                bool wasAddressable = beforeEntry != null;
                string beforeAddress = null;
                string beforeGroup   = null;
                if (beforeEntry != null)
                {
                    var bt = beforeEntry.GetType();
                    beforeAddress = bt.GetProperty("address")?.GetValue(beforeEntry) as string;
                    var g = bt.GetProperty("parentGroup")?.GetValue(beforeEntry);
                    beforeGroup = g?.GetType().GetProperty("Name")?.GetValue(g) as string;
                }

                if (!makeAddressable)
                {
                    if (beforeEntry == null)
                        return UnchangedEnvelope(assetPath, "already_not_addressable", wasAddressable);
                    // Call RemoveAssetEntry(guid)
                    var rm = st.GetMethod("RemoveAssetEntry", new[] { typeof(string), typeof(bool) })
                            ?? st.GetMethod("RemoveAssetEntry", new[] { typeof(string) });
                    rm.Invoke(settings, rm.GetParameters().Length == 2
                        ? new object[] { guid, true } : new object[] { guid });
                    AssetDatabase.SaveAssets();
                    return new
                    {
                        asset_path = assetPath, guid,
                        applied_fields = new object[]
                        {
                            new { field = "is_addressable", before = true, after = false },
                            new { field = "address", before = beforeAddress, after = (string)null },
                            new { field = "group",   before = beforeGroup,   after = (string)null }
                        },
                        applied_count = 3,
                        read_at_utc = DateTime.UtcNow.ToString("o"),
                        frame       = (long)Time.frameCount
                    };
                }

                // Resolve target group.
                object targetGroup = null;
                if (!string.IsNullOrEmpty(group))
                {
                    var findGroup = st.GetMethod("FindGroup", new[] { typeof(string) });
                    targetGroup = findGroup?.Invoke(settings, new object[] { group })
                        ?? throw new InvalidOperationException($"Addressables group not found: {group}");
                }
                else
                {
                    targetGroup = st.GetProperty("DefaultGroup")?.GetValue(settings)
                        ?? throw new InvalidOperationException("No DefaultGroup on settings.");
                }

                // CreateOrMoveEntry(guid, group, readOnly=false, postEvent=true)
                var cm = st.GetMethod("CreateOrMoveEntry",
                    new[] { typeof(string), targetGroup.GetType(), typeof(bool), typeof(bool) });
                var entry = cm?.Invoke(settings, new object[] { guid, targetGroup, false, true })
                    ?? throw new InvalidOperationException("CreateOrMoveEntry returned null.");

                var et = entry.GetType();
                if (!string.IsNullOrEmpty(address))
                {
                    et.GetProperty("address")?.SetValue(entry, address);
                }
                if (!string.IsNullOrEmpty(label))
                {
                    // SetLabel(string,bool,bool,bool) adds a label; boolean params control
                    // forceAdd/postEvent across versions. Call the 3-arg variant first.
                    var sl3 = et.GetMethod("SetLabel", new[] { typeof(string), typeof(bool), typeof(bool) });
                    var sl4 = et.GetMethod("SetLabel", new[] { typeof(string), typeof(bool), typeof(bool), typeof(bool) });
                    if (sl4 != null) sl4.Invoke(entry, new object[] { label, true, true, true });
                    else if (sl3 != null) sl3.Invoke(entry, new object[] { label, true, true });
                }
                AssetDatabase.SaveAssets();

                var afterAddress = et.GetProperty("address")?.GetValue(entry) as string;
                var afterGroupObj = et.GetProperty("parentGroup")?.GetValue(entry);
                var afterGroup = afterGroupObj?.GetType().GetProperty("Name")?.GetValue(afterGroupObj) as string;

                return new
                {
                    asset_path = assetPath, guid,
                    applied_fields = new object[]
                    {
                        new { field = "is_addressable", before = wasAddressable, after = true },
                        new { field = "address", before = beforeAddress, after = afterAddress },
                        new { field = "group",   before = beforeGroup,   after = afterGroup }
                    },
                    applied_count = 3,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- addressables-build-job ----------
        // Build content is long-running; route through ReifyJobs so callers
        // poll rather than block.
        [ReifyTool("addressables-build-job")]
        public static Task<object> BuildJob(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var job = ReifyJobs.Start("addressables-build");
                ReifyJobs.SetRunning(job, "queued", 0f);

                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        ReifyJobs.SetRunning(job, "BuildPlayerContent running", -1f);
                        var contentT = FindType("UnityEditor.AddressableAssets.AddressableAssetSettings")
                            ?? throw new InvalidOperationException("Addressables package not installed.");
                        var build = contentT.GetMethod("BuildPlayerContent",
                            BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                        if (build == null)
                        {
                            // Newer API: AddressableAssetSettings.BuildPlayerContent(out result)
                            var outT = FindType("UnityEditor.AddressableAssets.Build.AddressablesPlayerBuildResult");
                            build = contentT.GetMethod("BuildPlayerContent",
                                BindingFlags.Public | BindingFlags.Static, null,
                                new[] { outT.MakeByRefType() }, null);
                            if (build == null)
                                throw new InvalidOperationException("No BuildPlayerContent signature matched.");
                            var resultArgs = new object[] { null };
                            build.Invoke(null, resultArgs);
                            ReifyJobs.Succeed(job, new { result_type = resultArgs[0]?.GetType().FullName });
                        }
                        else
                        {
                            build.Invoke(null, null);
                            ReifyJobs.Succeed(job, new { note = "BuildPlayerContent completed (no result object)." });
                        }
                    }
                    catch (Exception ex)
                    {
                        ReifyJobs.Fail(job, $"{ex.GetType().Name}: {ex.Message}");
                    }
                };

                return new
                {
                    job = ReifyJobs.Serialize(job, includeResult: false, includeEvents: false),
                    note = "Addressables build queued. Poll job-status / job-result.",
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static string[] ListSchemaTypes(object group)
        {
            var schemas = (IEnumerable)group.GetType().GetProperty("Schemas")?.GetValue(group)
                          ?? (IEnumerable)group.GetType().GetProperty("schemas")?.GetValue(group);
            if (schemas == null) return new string[0];
            var list = new List<string>();
            foreach (var s in schemas) if (s != null) list.Add(s.GetType().FullName);
            return list.ToArray();
        }

        private static object UnchangedEnvelope(string assetPath, string reason, bool wasAddressable) => new
        {
            asset_path      = assetPath,
            applied_fields  = new object[0],
            applied_count   = 0,
            note            = reason,
            is_addressable  = wasAddressable,
            read_at_utc     = DateTime.UtcNow.ToString("o"),
            frame           = (long)Time.frameCount
        };
    }
}
