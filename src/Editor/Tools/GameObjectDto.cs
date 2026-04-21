using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Structured-state projection of a GameObject for every gameobject-* /
    /// component-* tool response. Conforms to ADR-001 §3 — flat-ish JSON,
    /// code identifiers, every nested value has a documented unit.
    /// </summary>
    internal static class GameObjectDto
    {
        public static object Build(GameObject go, bool includeComponents)
        {
            if (go == null) return null;

            var t = go.transform;

            object components = null;
            if (includeComponents)
            {
                var comps = go.GetComponents<Component>();
                var list = new List<object>(comps.Length);
                foreach (var c in comps)
                {
                    if (c == null) // missing script
                    {
                        list.Add(new { type = "<missing-script>", instance_id = 0 });
                        continue;
                    }
                    list.Add(new
                    {
                        type        = c.GetType().FullName,
                        instance_id = GameObjectResolver.InstanceIdOf(c)
                    });
                }
                components = list.ToArray();
            }

            return new
            {
                instance_id         = GameObjectResolver.InstanceIdOf(go),
                name                = go.name,
                path                = GameObjectResolver.PathOf(go),
                parent_path         = t.parent != null ? GameObjectResolver.PathOf(t.parent.gameObject) : "",
                scene_path          = go.scene.path,
                tag                 = go.tag,
                layer               = go.layer,
                layer_name          = LayerMask.LayerToName(go.layer),
                active_self         = go.activeSelf,
                active_in_hierarchy = go.activeInHierarchy,
                is_static           = go.isStatic,
                transform = new
                {
                    local_position        = V(t.localPosition),
                    local_rotation_euler  = V(t.localEulerAngles),
                    local_scale           = V(t.localScale),
                    world_position        = V(t.position),
                    world_rotation_euler  = V(t.eulerAngles),
                    lossy_scale           = V(t.lossyScale)
                },
                child_count = t.childCount,
                components  = components
            };
        }

        public static object Wrap(object gameobject) => new
        {
            gameobject  = gameobject,
            read_at_utc = DateTime.UtcNow.ToString("o"),
            frame       = (long)Time.frameCount
        };

        public static object V(Vector3 v) => new { x = v.x, y = v.y, z = v.z };
    }
}
