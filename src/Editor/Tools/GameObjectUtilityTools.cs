using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class GameObjectUtilityTools
    {
        [ReifyTool("gameobject-duplicate")]
        public static Task<object> Duplicate(JToken args)
        {
            var path             = args?.Value<string>("path") ?? throw new ArgumentException("path is required.");
            var newName          = args?.Value<string>("new_name");
            var hasParentOverride = args?["parent_path"] != null;
            var parentPath       = args?.Value<string>("parent_path");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var source = GameObjectResolver.ByPath(path)
                    ?? throw new InvalidOperationException($"GameObject not found: {path}");

                var duplicate = UnityEngine.Object.Instantiate(source, source.transform.parent);
                Undo.RegisterCreatedObjectUndo(duplicate, $"Reify: duplicate GameObject '{source.name}'");

                if (!string.IsNullOrEmpty(newName))
                    duplicate.name = newName;

                if (hasParentOverride)
                {
                    if (string.IsNullOrEmpty(parentPath))
                    {
                        Undo.SetTransformParent(duplicate.transform, null, "Reify: unparent duplicated GameObject");
                    }
                    else
                    {
                        var parent = GameObjectResolver.ByPath(parentPath)
                            ?? throw new InvalidOperationException($"Parent not found: {parentPath}");
                        Undo.SetTransformParent(duplicate.transform, parent.transform, "Reify: reparent duplicated GameObject");
                    }
                }

                EditorUtility.SetDirty(duplicate);

                return new
                {
                    source      = GameObjectDto.Build(source, includeComponents: false),
                    duplicate   = GameObjectDto.Build(duplicate, includeComponents: false),
                    applied_fields = new object[]
                    {
                        new { field = "source_instance_id",
                              before = (int?)null,
                              after  = GameObjectResolver.InstanceIdOf(duplicate),
                              note = "new GameObject created, original unchanged" },
                        new { field = "duplicate_path",
                              before = (string)null,
                              after  = GameObjectResolver.PathOf(duplicate) }
                    },
                    applied_count = 2,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("gameobject-set-parent")]
        public static Task<object> SetParent(JToken args)
        {
            var path = args?.Value<string>("path") ?? throw new ArgumentException("path is required.");
            if (args?["parent_path"] == null)
                throw new ArgumentException("parent_path is required. Pass empty string to unparent.");
            var parentPath = args.Value<string>("parent_path");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var go = GameObjectResolver.ByPath(path)
                    ?? throw new InvalidOperationException($"GameObject not found: {path}");
                var previousParentPath = go.transform.parent != null
                    ? GameObjectResolver.PathOf(go.transform.parent.gameObject)
                    : "";

                Undo.RegisterFullObjectHierarchyUndo(go, $"Reify: set parent for '{go.name}'");

                if (string.IsNullOrEmpty(parentPath))
                {
                    Undo.SetTransformParent(go.transform, null, "Reify: unparent GameObject");
                }
                else
                {
                    var parent = GameObjectResolver.ByPath(parentPath)
                        ?? throw new InvalidOperationException($"Parent not found: {parentPath}");
                    Undo.SetTransformParent(go.transform, parent.transform, "Reify: set parent for GameObject");
                }

                EditorUtility.SetDirty(go);
                var afterParentPath = go.transform.parent != null
                    ? GameObjectResolver.PathOf(go.transform.parent.gameObject) : "";

                return new
                {
                    previous_parent_path = previousParentPath,
                    gameobject           = GameObjectDto.Build(go, includeComponents: false),
                    applied_fields       = new object[]
                    {
                        new { field = "parent_path",
                              before = previousParentPath, after = afterParentPath }
                    },
                    applied_count        = 1,
                    read_at_utc          = DateTime.UtcNow.ToString("o"),
                    frame                = (long)Time.frameCount
                };
            });
        }
    }
}
