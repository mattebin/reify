using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class EditorOpsTools
    {
        // ---------- editor-request-script-compilation ----------
        // When Unity is in the background, saved-file changes don't trigger
        // a script recompile until the editor regains focus. That blocks
        // any reify-driven fix cycle. This tool calls the official
        // CompilationPipeline.RequestScriptCompilation so callers can
        // remotely re-compile and re-load the new assembly.
        [ReifyTool("editor-request-script-compilation")]
        public static Task<object> RequestScriptCompilation(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                // RequestScriptCompilation (RequestScriptCompilationOptions)
                // exists on 2021.2+ and is the supported API. It's async —
                // callers should poll domain-reload-status to know when the
                // new assembly is live.
                CompilationPipeline.RequestScriptCompilation();
                return new
                {
                    requested      = true,
                    note           = "Script compilation requested. Poll domain-reload-status " +
                                     "until last_compile_finished_utc advances past read_at_utc.",
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        // ---------- editor-menu-execute ----------
        [ReifyTool("editor-menu-execute")]
        public static Task<object> MenuExecute(JToken args)
        {
            var path = args?.Value<string>("path") ?? throw new ArgumentException("path is required (e.g. 'GameObject/3D Object/Cube').");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var ok = EditorApplication.ExecuteMenuItem(path);
                if (!ok)
                    // ExecuteMenuItem returns false both when the path doesn't
                    // exist AND when the item is disabled in the current
                    // context. Unity doesn't distinguish — we can't either.
                    throw new InvalidOperationException(
                        $"Menu item '{path}' could not be executed. Either the path " +
                        "is wrong or the item is disabled in the current editor state.");

                return new
                {
                    menu_path    = path,
                    executed     = true,
                    note         = "Unity's ExecuteMenuItem returns a boolean only — no side-effect details are exposed by the engine.",
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        // ---------- editor-undo ----------
        [ReifyTool("editor-undo")]
        public static Task<object> Undo(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var beforeGroup = UnityEditor.Undo.GetCurrentGroupName();
                UnityEditor.Undo.PerformUndo();
                var afterGroup  = UnityEditor.Undo.GetCurrentGroupName();
                return new
                {
                    action = "undo",
                    undone_label   = beforeGroup,
                    new_top_label  = afterGroup,
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        // ---------- editor-redo ----------
        [ReifyTool("editor-redo")]
        public static Task<object> Redo(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                UnityEditor.Undo.PerformRedo();
                var afterGroup = UnityEditor.Undo.GetCurrentGroupName();
                return new
                {
                    action        = "redo",
                    new_top_label = afterGroup,
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }

        // ---------- editor-undo-history ----------
        [ReifyTool("editor-undo-history")]
        public static Task<object> UndoHistory(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                // Undo.GetRecords exists on some Unity versions as a public
                // API and on others only via reflection. Try reflection so
                // this compiles across the 2021.3 > 6.x range we target.
                var undoList = new List<string>();
                var redoList = new List<string>();
                var m = typeof(UnityEditor.Undo).GetMethod("GetRecords",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
                    null, new[] { typeof(List<string>), typeof(List<string>) }, null);
                if (m != null)
                {
                    try { m.Invoke(null, new object[] { undoList, redoList }); }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Undo.GetRecords invocation failed: {ex.Message}");
                    }
                }
                // If the method isn't present, undoList/redoList stay empty
                // — the response still carries current_group_* so the caller
                // gets the most useful single piece of state.

                return new
                {
                    current_group_index = UnityEditor.Undo.GetCurrentGroup(),
                    current_group_label = UnityEditor.Undo.GetCurrentGroupName(),
                    undo_stack_depth    = undoList.Count,
                    redo_stack_depth    = redoList.Count,
                    undo_records        = undoList.ToArray(),
                    redo_records        = redoList.ToArray(),
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }

        // ---------- editor-selection-get ----------
        [ReifyTool("editor-selection-get")]
        public static Task<object> SelectionGet(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var objs = Selection.objects ?? Array.Empty<UnityEngine.Object>();
                var items = new List<object>(objs.Length);
                foreach (var o in objs)
                {
                    if (o == null) continue;
                    items.Add(DescribeObject(o));
                }
                var active = Selection.activeObject != null ? DescribeObject(Selection.activeObject) : null;

                return new
                {
                    active,
                    count       = items.Count,
                    selection   = items.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- editor-selection-set ----------
        [ReifyTool("editor-selection-set")]
        public static Task<object> SelectionSet(JToken args)
        {
            var ids   = args?["instance_ids"] as JArray;
            var paths = args?["paths"] as JArray;

            if ((ids == null || ids.Count == 0) && (paths == null || paths.Count == 0))
                throw new ArgumentException("Provide instance_ids[] or paths[] (at least one non-empty).");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var resolved = new List<UnityEngine.Object>();
                var unresolved = new List<object>();

                if (ids != null)
                    foreach (var tok in ids)
                    {
                        var id = tok.Value<int>();
                        var obj = GameObjectResolver.ByInstanceId(id);
                        if (obj != null) resolved.Add(obj);
                        else unresolved.Add(new { kind = "instance_id", value = id });
                    }

                if (paths != null)
                    foreach (var tok in paths)
                    {
                        var p = tok.Value<string>();
                        // Try scene path first, fall back to asset path.
                        var go = GameObjectResolver.ByPath(p);
                        if (go != null) { resolved.Add(go); continue; }
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
                        if (asset != null) { resolved.Add(asset); continue; }
                        unresolved.Add(new { kind = "path", value = p });
                    }

                Selection.objects = resolved.ToArray();

                var items = new List<object>(resolved.Count);
                foreach (var o in resolved) items.Add(DescribeObject(o));

                return new
                {
                    selected    = items.ToArray(),
                    unresolved  = unresolved.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static object DescribeObject(UnityEngine.Object obj)
        {
            var isGo = obj is GameObject;
            var assetPath = AssetDatabase.GetAssetPath(obj);
            return new
            {
                type_fqn        = obj.GetType().FullName,
                instance_id     = GameObjectResolver.InstanceIdOf(obj),
                name            = obj.name,
                asset_path      = string.IsNullOrEmpty(assetPath) ? null : assetPath,
                gameobject_path = isGo ? GameObjectResolver.PathOf((GameObject)obj) : null
            };
        }
    }
}
