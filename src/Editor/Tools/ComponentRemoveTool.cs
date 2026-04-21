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
        public static Task<object> Handle(JToken args)
        {
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var goPath     = args?.Value<string>("gameobject_path");
            var compType   = args?.Value<string>("component_type");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var component = ComponentLookup.Resolve(instanceId, goPath, compType);

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
