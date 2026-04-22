using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Shared Component resolver for component-modify / component-remove /
    /// component-set-property. Two keying strategies:
    ///  - instance_id (preferred — stable, unambiguous).
    ///  - gameobject_path + component_type (use when you don't have the id,
    ///    e.g. when a Component was just added and the caller kept only the
    ///    GameObject path).
    /// </summary>
    internal static class ComponentLookup
    {
        /// <summary>
        /// Read a GameObject-path argument accepting both aliases so
        /// callers don't have to memorise which tool uses which name.
        /// Canonical is `gameobject_path`; `path` and `go_path` also accepted.
        /// </summary>
        public static string ReadGameObjectPathArg(JToken args)
            => args?.Value<string>("gameobject_path")
               ?? args?.Value<string>("path")
               ?? args?.Value<string>("go_path");

        /// <summary>
        /// Read a component-type argument accepting both aliases.
        /// Canonical is `component_type`; `type_name` and `type` also accepted.
        /// </summary>
        public static string ReadComponentTypeArg(JToken args)
            => args?.Value<string>("component_type")
               ?? args?.Value<string>("type_name")
               ?? args?.Value<string>("type");

        /// <summary>
        /// Read an instance_id argument tolerantly — accepts `instance_id`
        /// or (component-specific alias) `component_instance_id`.
        /// </summary>
        public static int? ReadInstanceIdArg(JToken args)
        {
            var t = args?["instance_id"];
            if (t != null && t.Type == JTokenType.Integer) return t.Value<int>();
            var t2 = args?["component_instance_id"];
            if (t2 != null && t2.Type == JTokenType.Integer) return t2.Value<int>();
            return null;
        }

        /// <summary>
        /// One-shot: pull the three standard resolver args from the envelope
        /// (with aliases) and hand back a Component.
        /// </summary>
        public static Component ResolveFromArgs(JToken args)
            => Resolve(ReadInstanceIdArg(args),
                       ReadGameObjectPathArg(args),
                       ReadComponentTypeArg(args));

        public static Component Resolve(int? instanceId, string gameObjectPath, string componentType)
        {
            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException(
                        $"No object with instance_id {instanceId}.");
                if (obj is Component c) return c;
                throw new InvalidOperationException(
                    $"instance_id {instanceId} resolves to {obj.GetType().Name}, not a Component.");
            }

            if (string.IsNullOrEmpty(gameObjectPath) || string.IsNullOrEmpty(componentType))
                throw new ArgumentException(
                    "Provide either instance_id, or both gameobject_path and component_type.");

            var go = GameObjectResolver.ByPath(gameObjectPath)
                ?? throw new InvalidOperationException($"GameObject not found: {gameObjectPath}");

            var fullMatches = new List<Component>();
            var shortMatches = new List<Component>();
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                var t = comp.GetType();
                if (t.FullName == componentType)
                    fullMatches.Add(comp);
                else if (t.Name == componentType)
                    shortMatches.Add(comp);
            }

            if (fullMatches.Count == 1) return fullMatches[0];
            if (fullMatches.Count > 1)
                throw new InvalidOperationException(
                    BuildAmbiguousComponentMessage(go, componentType, fullMatches));

            if (shortMatches.Count == 1) return shortMatches[0];
            if (shortMatches.Count > 1)
                throw new InvalidOperationException(
                    BuildAmbiguousComponentMessage(go, componentType, shortMatches));

            throw new InvalidOperationException(
                $"No component of type '{componentType}' on GameObject '{gameObjectPath}'.");
        }

        private static string BuildAmbiguousComponentMessage(GameObject go, string componentType, List<Component> matches)
        {
            var candidates = new string[matches.Count];
            for (var i = 0; i < matches.Count; i++)
            {
                var comp = matches[i];
                candidates[i] = $"{comp.GetType().FullName} (instance_id {GameObjectResolver.InstanceIdOf(comp)})";
            }

            return
                $"Component lookup is ambiguous for '{componentType}' on '{GameObjectResolver.QualifiedPathOf(go)}'. " +
                $"Matches: {string.Join("; ", candidates)}. Use instance_id instead.";
        }
    }
}
