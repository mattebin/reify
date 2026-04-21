using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class GameObjectDestroyTool
    {
        public static Task<object> Handle(JToken args)
        {
            var path       = args?.Value<string>("path");
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;

            if (string.IsNullOrEmpty(path) && !instanceId.HasValue)
                throw new ArgumentException("Provide either 'path' or 'instance_id'.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                GameObject go;
                if (instanceId.HasValue)
                {
                    go = GameObjectResolver.ByInstanceId(instanceId.Value) as GameObject
                        ?? throw new InvalidOperationException(
                            $"instance_id {instanceId} does not resolve to a GameObject.");
                }
                else
                {
                    go = GameObjectResolver.ByPath(path)
                        ?? throw new InvalidOperationException($"GameObject not found: {path}");
                }

                var snapshot = new
                {
                    instance_id = go.GetInstanceID(),
                    name        = go.name,
                    path        = GameObjectResolver.PathOf(go)
                };

                Undo.DestroyObjectImmediate(go);

                return new
                {
                    destroyed   = snapshot,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }
    }
}
