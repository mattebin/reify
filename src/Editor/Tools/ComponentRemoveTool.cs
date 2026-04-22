using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class ComponentRemoveTool
    {
        [ReifyTool("component-remove")]
        public static Task<object> Handle(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var component = ComponentLookup.ResolveFromArgs(args);

                // Transform can never be removed.
                if (component is Transform)
                    throw new InvalidOperationException(
                        "Cannot remove Transform component from a GameObject.");

                var type = component.GetType();
                var go   = component.gameObject;
                var snapshot = new
                {
                    type_fqn        = type.FullName,
                    instance_id     = GameObjectResolver.InstanceIdOf(component),
                    gameobject_path = GameObjectResolver.PathOf(go),
                    was_enabled     = component is Behaviour b ? b.enabled : (object)null
                };

                // Pre-write counts for ADR-002 receipt.
                var beforeCount = 0;
                foreach (var c in go.GetComponents(type)) if (c != null) beforeCount++;
                var beforeComponentCount = go.GetComponents<Component>().Length;

                Undo.DestroyObjectImmediate(component);

                var afterCount = 0;
                foreach (var c in go.GetComponents(type)) if (c != null) afterCount++;
                var afterComponentCount = go.GetComponents<Component>().Length;

                return new
                {
                    removed     = snapshot,
                    applied_fields = new object[]
                    {
                        new { field = "component_type_count", component_type = type.FullName,
                              before = beforeCount, after = afterCount },
                        new { field = "gameobject_component_count",
                              before = beforeComponentCount, after = afterComponentCount }
                    },
                    applied_count = 2,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }
    }
}
