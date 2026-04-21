using System;
using System.Collections.Generic;
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
