using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class GameObjectModifyTool
    {
        public static Task<object> Handle(JToken args)
        {
            var path          = args?.Value<string>("path")            ?? throw new ArgumentException("path is required");
            var newName       = args?.Value<string>("new_name");
            var reparentPath  = args?.Value<string>("reparent_to");   // "" means make root
            var active        = args?["active"]?.Type == JTokenType.Boolean ? args.Value<bool?>("active") : null;
            var layer         = args?["layer"]?.Type == JTokenType.Integer ? args.Value<int?>("layer") : null;
            var tag           = args?.Value<string>("tag");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var go = GameObjectResolver.ByPath(path)
                    ?? throw new InvalidOperationException($"GameObject not found: {path}");

                Undo.RegisterFullObjectHierarchyUndo(go, $"Reify: modify GameObject '{go.name}'");

                if (!string.IsNullOrEmpty(newName))
                    go.name = newName;

                if (reparentPath != null)
                {
                    if (reparentPath.Length == 0)
                        Undo.SetTransformParent(go.transform, null, "Reify: unparent GameObject");
                    else
                    {
                        var parent = GameObjectResolver.ByPath(reparentPath)
                            ?? throw new InvalidOperationException($"Reparent target not found: {reparentPath}");
                        Undo.SetTransformParent(go.transform, parent.transform, "Reify: reparent GameObject");
                    }
                }

                if (active.HasValue) go.SetActive(active.Value);
                if (layer.HasValue)  go.layer = layer.Value;
                if (!string.IsNullOrEmpty(tag))
                {
                    try { go.tag = tag; }
                    catch (UnityException ex)
                    {
                        throw new InvalidOperationException(
                            $"Tag '{tag}' is not defined in Tags & Layers settings: {ex.Message}");
                    }
                }

                EditorUtility.SetDirty(go);

                return GameObjectDto.Wrap(GameObjectDto.Build(go, includeComponents: false));
            });
        }
    }
}
