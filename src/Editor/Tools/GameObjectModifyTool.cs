using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class GameObjectModifyTool
    {
        // Every field this tool recognises. Used for:
        //  (a) rejecting unknown keys — silent no-ops are reliability bugs.
        //  (b) telling the caller which fields actually triggered a write,
        //      so the receipt isn't a lie.
        private static readonly HashSet<string> Recognised = new()
        {
            "path", "instance_id",
            "new_name", "reparent_to", "active", "layer", "tag",
            "local_position", "local_rotation_euler", "local_scale",
            "world_position", "world_rotation_euler"
        };

        [ReifyTool("gameobject-modify")]
        public static Task<object> Handle(JToken args)
        {
            var obj = args as JObject
                ?? throw new ArgumentException("gameobject-modify args must be an object.");

            // Reject unknown keys up-front so typos (e.g. `world_pos` instead
            // of `world_position`) never silently succeed.
            var unknown = obj.Properties()
                .Select(p => p.Name)
                .Where(n => !Recognised.Contains(n))
                .ToArray();
            if (unknown.Length > 0)
                throw new ArgumentException(
                    $"Unknown field(s): {string.Join(", ", unknown)}. " +
                    $"Accepted: {string.Join(", ", Recognised)}.");

            var path          = obj.Value<string>("path");
            var instanceId    = obj["instance_id"]?.Type == JTokenType.Integer
                                    ? obj.Value<int?>("instance_id") : null;
            var newName       = obj.Value<string>("new_name");
            var reparentPath  = obj.Value<string>("reparent_to");   // "" = make root
            var active        = obj["active"]?.Type == JTokenType.Boolean
                                    ? obj.Value<bool?>("active") : null;
            var layer         = obj["layer"]?.Type == JTokenType.Integer
                                    ? obj.Value<int?>("layer") : null;
            var tag           = obj.Value<string>("tag");
            var localPos      = ReadVec3(obj["local_position"]);
            var localRot      = ReadVec3(obj["local_rotation_euler"]);
            var localScale    = ReadVec3(obj["local_scale"]);
            var worldPos      = ReadVec3(obj["world_position"]);
            var worldRot      = ReadVec3(obj["world_rotation_euler"]);

            if (string.IsNullOrEmpty(path) && !instanceId.HasValue)
                throw new ArgumentException("Either 'path' or 'instance_id' is required.");

            // If no mutation field was set, fail loudly rather than return a
            // misleading OK. This was the silent-no-op bug found in live
            // validation: gameobject-modify would silently ignore
            // `world_position` and report success.
            var willWrite = newName != null || reparentPath != null
                || active.HasValue || layer.HasValue || !string.IsNullOrEmpty(tag)
                || localPos.HasValue || localRot.HasValue || localScale.HasValue
                || worldPos.HasValue || worldRot.HasValue;
            if (!willWrite)
                throw new ArgumentException(
                    "No recognised mutation field was set. Provide at least one of: " +
                    "new_name, reparent_to, active, layer, tag, local_position, " +
                    "local_rotation_euler, local_scale, world_position, " +
                    "world_rotation_euler.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var go = instanceId.HasValue
                    ? (GameObjectResolver.ByInstanceId(instanceId.Value) as GameObject
                        ?? throw new InvalidOperationException($"No GameObject with instance_id {instanceId}."))
                    : (GameObjectResolver.ByPath(path)
                        ?? throw new InvalidOperationException($"GameObject not found: {path}"));

                Undo.RegisterFullObjectHierarchyUndo(go, $"Reify: modify GameObject '{go.name}'");

                // Track what actually changed so the receipt is honest.
                var applied = new List<object>();

                if (newName != null && newName != go.name)
                {
                    var before = go.name;
                    go.name = newName;
                    applied.Add(new { field = "new_name", before, after = go.name });
                }

                if (reparentPath != null)
                {
                    var beforeParent = go.transform.parent != null
                        ? GameObjectResolver.PathOf(go.transform.parent.gameObject) : "";
                    if (reparentPath.Length == 0)
                        Undo.SetTransformParent(go.transform, null, "Reify: unparent GameObject");
                    else
                    {
                        var parent = GameObjectResolver.ByPath(reparentPath)
                            ?? throw new InvalidOperationException($"Reparent target not found: {reparentPath}");
                        Undo.SetTransformParent(go.transform, parent.transform, "Reify: reparent GameObject");
                    }
                    var afterParent = go.transform.parent != null
                        ? GameObjectResolver.PathOf(go.transform.parent.gameObject) : "";
                    applied.Add(new { field = "reparent_to", before = beforeParent, after = afterParent });
                }

                if (active.HasValue && active.Value != go.activeSelf)
                {
                    var before = go.activeSelf;
                    go.SetActive(active.Value);
                    applied.Add(new { field = "active", before, after = go.activeSelf });
                }

                if (layer.HasValue && layer.Value != go.layer)
                {
                    var before = go.layer;
                    go.layer = layer.Value;
                    applied.Add(new { field = "layer", before, after = go.layer });
                }

                if (!string.IsNullOrEmpty(tag) && tag != go.tag)
                {
                    var before = go.tag;
                    try { go.tag = tag; }
                    catch (UnityException ex)
                    {
                        throw new InvalidOperationException(
                            $"Tag '{tag}' is not defined in Tags & Layers settings: {ex.Message}");
                    }
                    applied.Add(new { field = "tag", before, after = go.tag });
                }

                if (localPos.HasValue)
                {
                    var before = go.transform.localPosition;
                    Undo.RecordObject(go.transform, "Reify: set localPosition");
                    go.transform.localPosition = localPos.Value;
                    applied.Add(new { field = "local_position", before = V3(before), after = V3(go.transform.localPosition) });
                }
                if (localRot.HasValue)
                {
                    var before = go.transform.localEulerAngles;
                    Undo.RecordObject(go.transform, "Reify: set localEulerAngles");
                    go.transform.localEulerAngles = localRot.Value;
                    applied.Add(new { field = "local_rotation_euler", before = V3(before), after = V3(go.transform.localEulerAngles) });
                }
                if (localScale.HasValue)
                {
                    var before = go.transform.localScale;
                    Undo.RecordObject(go.transform, "Reify: set localScale");
                    go.transform.localScale = localScale.Value;
                    applied.Add(new { field = "local_scale", before = V3(before), after = V3(go.transform.localScale) });
                }
                if (worldPos.HasValue)
                {
                    var before = go.transform.position;
                    Undo.RecordObject(go.transform, "Reify: set position");
                    go.transform.position = worldPos.Value;
                    applied.Add(new { field = "world_position", before = V3(before), after = V3(go.transform.position) });
                }
                if (worldRot.HasValue)
                {
                    var before = go.transform.eulerAngles;
                    Undo.RecordObject(go.transform, "Reify: set eulerAngles");
                    go.transform.eulerAngles = worldRot.Value;
                    applied.Add(new { field = "world_rotation_euler", before = V3(before), after = V3(go.transform.eulerAngles) });
                }

                EditorUtility.SetDirty(go);

                return new
                {
                    gameobject      = GameObjectDto.Build(go, includeComponents: false),
                    applied_fields  = applied.ToArray(),
                    applied_count   = applied.Count,
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        private static Vector3? ReadVec3(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            return new Vector3(
                token.Value<float?>("x") ?? 0f,
                token.Value<float?>("y") ?? 0f,
                token.Value<float?>("z") ?? 0f);
        }

        private static object V3(Vector3 v) => new { x = v.x, y = v.y, z = v.z };
    }
}
