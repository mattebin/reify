using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class ComponentModifyTool
    {
        [ReifyTool("component-modify")]
        public static Task<object> Handle(JToken args)
        {
            var properties = args?["properties"] as JObject
                ?? throw new ArgumentException("'properties' object is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var component = ComponentLookup.ResolveFromArgs(args);
                Undo.RecordObject(component, $"Reify: modify {component.GetType().Name}");

                using var so = new SerializedObject(component);
                var applied = new List<string>();
                var failed  = new List<object>();

                foreach (var kv in properties)
                {
                    var p = so.FindProperty(kv.Key);
                    if (p == null)
                    {
                        failed.Add(new { name = kv.Key, reason = "property_not_found" });
                        continue;
                    }
                    try
                    {
                        SerializedPropertyWriter.Apply(p, kv.Value);
                        applied.Add(kv.Key);
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new { name = kv.Key, reason = ex.Message });
                    }
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(component);

                return new
                {
                    component = new
                    {
                        type_fqn    = component.GetType().FullName,
                        instance_id = GameObjectResolver.InstanceIdOf(component),
                        gameobject_path = GameObjectResolver.PathOf(component.gameObject)
                    },
                    applied,
                    failed,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }
    }
}
