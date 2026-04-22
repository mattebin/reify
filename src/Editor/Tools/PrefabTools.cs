using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class PrefabTools
    {
        // ---------- prefab-create ----------
        [ReifyTool("prefab-create")]
        public static Task<object> Create(JToken args)
        {
            var goPath     = args?.Value<string>("gameobject_path") ?? throw new ArgumentException("gameobject_path is required.");
            var assetPath  = args?.Value<string>("asset_path")      ?? throw new ArgumentException("asset_path is required.");
            var connect    = args?.Value<bool?>("connect_instance") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var go = GameObjectResolver.ByPath(goPath)
                    ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
                if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !assetPath.EndsWith(".prefab", StringComparison.Ordinal))
                    throw new ArgumentException($"asset_path must start with 'Assets/' and end in '.prefab': {assetPath}");

                GameObject saved;
                if (connect)
                    saved = PrefabUtility.SaveAsPrefabAssetAndConnect(go, assetPath, InteractionMode.UserAction);
                else
                    saved = PrefabUtility.SaveAsPrefabAsset(go, assetPath);
                if (saved == null)
                    throw new InvalidOperationException($"Prefab save failed. Check Unity Console.");

                var prov = AssetProvenance.Summarize(assetPath);
                return new
                {
                    prefab = new
                    {
                        asset_path = assetPath,
                        guid       = AssetDatabase.AssetPathToGUID(assetPath),
                        name       = saved.name
                    },
                    instance    = connect ? GameObjectDto.Build(go, includeComponents: false) : null,
                    prefab_provenance = prov,
                    guids_touched = new[] { prov },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- prefab-instantiate ----------
        [ReifyTool("prefab-instantiate")]
        public static Task<object> Instantiate(JToken args)
        {
            var assetPath  = args?.Value<string>("asset_path") ?? throw new ArgumentException("asset_path is required.");
            var parentPath = args?.Value<string>("parent_path");
            var position   = ReadVec3(args?["position"])       ?? Vector3.zero;
            var rotation   = ReadVec3(args?["rotation_euler"]) ?? Vector3.zero;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath)
                    ?? throw new InvalidOperationException($"Prefab asset not found: {assetPath}");

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (instance == null)
                    throw new InvalidOperationException($"InstantiatePrefab returned null for {assetPath}.");

                Undo.RegisterCreatedObjectUndo(instance, $"Reify: instantiate prefab '{prefab.name}'");

                if (!string.IsNullOrEmpty(parentPath))
                {
                    var parent = GameObjectResolver.ByPath(parentPath)
                        ?? throw new InvalidOperationException($"Parent not found: {parentPath}");
                    Undo.SetTransformParent(instance.transform, parent.transform, "Reify: reparent instantiated prefab");
                }

                instance.transform.localPosition    = position;
                instance.transform.localEulerAngles = rotation;

                return new
                {
                    prefab = new { asset_path = assetPath, name = prefab.name },
                    instance = GameObjectDto.Build(instance, includeComponents: false),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- prefab-open ----------
        [ReifyTool("prefab-open")]
        public static Task<object> Open(JToken args)
        {
            var assetPath = args?.Value<string>("asset_path") ?? throw new ArgumentException("asset_path is required.");
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var stage = UnityEditor.SceneManagement.PrefabStageUtility.OpenPrefab(assetPath);
                if (stage == null)
                    throw new InvalidOperationException($"OpenPrefab returned null for {assetPath}.");
                return new
                {
                    stage = new
                    {
                        asset_path      = stage.assetPath,
                        prefab_name     = stage.prefabContentsRoot != null ? stage.prefabContentsRoot.name : null,
                        mode            = stage.mode.ToString()
                    },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- prefab-close ----------
        [ReifyTool("prefab-close")]
        public static Task<object> Close(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var prev = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                StageUtility.GoToMainStage();
                return new
                {
                    closed = prev != null ? new { asset_path = prev.assetPath } : null,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- prefab-get-overrides ----------
        [ReifyTool("prefab-get-overrides")]
        public static Task<object> GetOverrides(JToken args)
        {
            var goPath = args?.Value<string>("gameobject_path") ?? throw new ArgumentException("gameobject_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var go = GameObjectResolver.ByPath(goPath)
                    ?? throw new InvalidOperationException($"GameObject not found: {goPath}");

                if (!PrefabUtility.IsPartOfPrefabInstance(go))
                    throw new InvalidOperationException($"'{goPath}' is not part of a prefab instance.");

                var root       = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                var assetPath  = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                var mods       = PrefabUtility.GetPropertyModifications(root) ?? Array.Empty<PropertyModification>();
                var added      = PrefabUtility.GetAddedComponents(root);
                var removed    = PrefabUtility.GetRemovedComponents(root);
                var addedGOs   = PrefabUtility.GetAddedGameObjects(root);

                var modList = new List<object>(mods.Length);
                foreach (var m in mods)
                {
                    modList.Add(new
                    {
                        target_type   = m.target != null ? m.target.GetType().FullName : null,
                        property_path = m.propertyPath,
                        value         = m.value,
                        object_reference = m.objectReference != null ? new
                        {
                            type_fqn    = m.objectReference.GetType().FullName,
                            instance_id = GameObjectResolver.InstanceIdOf(m.objectReference),
                            name        = m.objectReference.name
                        } : null
                    });
                }

                var addedList = new List<object>(added.Count);
                foreach (var ac in added)
                    addedList.Add(new { type_fqn = ac.instanceComponent != null ? ac.instanceComponent.GetType().FullName : null });

                var removedList = new List<object>(removed.Count);
                foreach (var rc in removed)
                    removedList.Add(new { type_fqn = rc.assetComponent != null ? rc.assetComponent.GetType().FullName : null });

                var addedGOList = new List<object>(addedGOs.Count);
                foreach (var agx in addedGOs)
                    addedGOList.Add(new
                    {
                        name = agx.instanceGameObject != null ? agx.instanceGameObject.name : null,
                        path = agx.instanceGameObject != null ? GameObjectResolver.PathOf(agx.instanceGameObject) : null
                    });

                return new
                {
                    instance_root = new
                    {
                        path        = GameObjectResolver.PathOf(root),
                        asset_path  = assetPath
                    },
                    property_modifications = modList,
                    added_components       = addedList,
                    removed_components     = removedList,
                    added_gameobjects      = addedGOList,
                    total_overrides        = modList.Count + addedList.Count + removedList.Count + addedGOList.Count,
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- prefab-apply-overrides ----------
        [ReifyTool("prefab-apply-overrides")]
        public static Task<object> ApplyOverrides(JToken args)
        {
            var goPath = args?.Value<string>("gameobject_path") ?? throw new ArgumentException("gameobject_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var go = GameObjectResolver.ByPath(goPath)
                    ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
                if (!PrefabUtility.IsPartOfPrefabInstance(go))
                    throw new InvalidOperationException($"'{goPath}' is not part of a prefab instance.");

                var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                PrefabUtility.ApplyPrefabInstance(root, InteractionMode.UserAction);

                return new
                {
                    applied_root = new { path = GameObjectResolver.PathOf(root) },
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        // ---------- prefab-revert-overrides ----------
        [ReifyTool("prefab-revert-overrides")]
        public static Task<object> RevertOverrides(JToken args)
        {
            var goPath = args?.Value<string>("gameobject_path") ?? throw new ArgumentException("gameobject_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var go = GameObjectResolver.ByPath(goPath)
                    ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
                if (!PrefabUtility.IsPartOfPrefabInstance(go))
                    throw new InvalidOperationException($"'{goPath}' is not part of a prefab instance.");

                var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                PrefabUtility.RevertPrefabInstance(root, InteractionMode.UserAction);

                return new
                {
                    reverted_root = new { path = GameObjectResolver.PathOf(root) },
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static Vector3? ReadVec3(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            return new Vector3(
                token.Value<float?>("x") ?? 0f,
                token.Value<float?>("y") ?? 0f,
                token.Value<float?>("z") ?? 0f);
        }
    }
}
