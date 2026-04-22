using System;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class ComponentAddTool
    {
        [ReifyTool("component-add")]
        public static Task<object> Handle(JToken args)
        {
            var path     = ComponentLookup.ReadGameObjectPathArg(args)
                ?? throw new ArgumentException("gameobject_path (alias: path) is required.");
            var typeName = ComponentLookup.ReadComponentTypeArg(args)
                ?? throw new ArgumentException("component_type (alias: type_name) is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var go = GameObjectResolver.ByPath(path)
                    ?? throw new InvalidOperationException($"GameObject not found: {path}");

                var type = ResolveComponentType(typeName)
                    ?? throw new InvalidOperationException(
                        $"Type '{typeName}' not found. Try the full name " +
                        "(e.g. 'UnityEngine.BoxCollider') or ensure the " +
                        "defining assembly is loaded.");

                if (!typeof(Component).IsAssignableFrom(type))
                    throw new InvalidOperationException(
                        $"Type '{type.FullName}' is not a Component subclass.");

                // Capture pre-write state for the receipt. Count existing
                // instances of this type so the caller can tell whether
                // the add duplicated or replaced.
                var beforeCount = 0;
                foreach (var c in go.GetComponents(type)) if (c != null) beforeCount++;
                var beforeComponentCount = go.GetComponents<Component>().Length;

                var component = Undo.AddComponent(go, type)
                    ?? throw new InvalidOperationException(
                        $"Unity refused to add {type.FullName} to '{go.name}' " +
                        "(may conflict with an existing component, e.g. two Rigidbodies).");

                var afterCount = 0;
                foreach (var c in go.GetComponents(type)) if (c != null) afterCount++;
                var afterComponentCount = go.GetComponents<Component>().Length;

                return new
                {
                    added = new
                    {
                        type_fqn    = type.FullName,
                        instance_id = GameObjectResolver.InstanceIdOf(component),
                        gameobject  = GameObjectDto.Build(go, includeComponents: true)
                    },
                    // ADR-002: self-proving receipt. before/after are the
                    // counts of this component type on this GameObject —
                    // so the caller can prove "I added one, total went
                    // from 0 -> 1" (or 1 -> 2 on duplicate-allowed types).
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

        private static Type ResolveComponentType(string typeName)
        {
            var t = Type.GetType(typeName);
            if (t != null) return t;

            // Unity Component types typically live in these assemblies.
            foreach (var qualified in new[]
            {
                typeName + ", UnityEngine",
                typeName + ", UnityEngine.CoreModule",
                typeName + ", UnityEngine.PhysicsModule",
                typeName + ", UnityEngine.UI",
                typeName + ", Assembly-CSharp"
            })
            {
                t = Type.GetType(qualified);
                if (t != null) return t;
            }

            // Last resort — scan all loaded assemblies.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(typeName, throwOnError: false);
                    if (t != null) return t;
                }
                catch (ReflectionTypeLoadException) { /* ignore */ }
            }
            return null;
        }
    }
}
