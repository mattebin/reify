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
            var path     = args?.Value<string>("path")      ?? throw new ArgumentException("path is required");
            var typeName = args?.Value<string>("type_name") ?? throw new ArgumentException("type_name is required");

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

                var component = Undo.AddComponent(go, type)
                    ?? throw new InvalidOperationException(
                        $"Unity refused to add {type.FullName} to '{go.name}' " +
                        "(may conflict with an existing component, e.g. two Rigidbodies).");

                return new
                {
                    added = new
                    {
                        type_fqn    = type.FullName,
                        instance_id = GameObjectResolver.InstanceIdOf(component),
                        gameobject  = GameObjectDto.Build(go, includeComponents: true)
                    },
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
