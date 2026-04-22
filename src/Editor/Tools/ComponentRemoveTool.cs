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

                var snapshot = new
                {
                    type_fqn        = component.GetType().FullName,
                    instance_id     = GameObjectResolver.InstanceIdOf(component),
                    gameobject_path = GameObjectResolver.PathOf(component.gameObject)
                };

                Undo.DestroyObjectImmediate(component);

                return new
                {
                    removed     = snapshot,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }
    }
}
