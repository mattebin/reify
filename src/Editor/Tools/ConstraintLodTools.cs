using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Constraints (Position/Rotation/Scale/Parent/Aim/LookAt) via the
    /// common IConstraint interface, plus LODGroup inspect and force-LOD.
    /// </summary>
    internal static class ConstraintLodTools
    {
        // ---------- constraint-inspect ----------
        [ReifyTool("constraint-inspect")]
        public static Task<object> ConstraintInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var (component, iface) = ResolveConstraint(args);
                var warnings = new List<string>();
                if (!iface.constraintActive) warnings.Add("constraintActive is false — constraint has no effect.");
                if (iface.weight <= 0f)      warnings.Add($"weight is {iface.weight:F3} — constraint has no effect.");
                if (iface.sourceCount == 0)  warnings.Add("No sources — constraint has nothing to follow.");

                var sources = new List<object>(iface.sourceCount);
                for (var i = 0; i < iface.sourceCount; i++)
                {
                    var src = iface.GetSource(i);
                    sources.Add(new
                    {
                        index            = i,
                        source_transform = src.sourceTransform != null ? new
                        {
                            instance_id     = GameObjectResolver.InstanceIdOf(src.sourceTransform),
                            gameobject_path = GameObjectResolver.PathOf(src.sourceTransform.gameObject)
                        } : null,
                        weight           = src.weight
                    });
                }

                // Type-specific axis flags read via reflection to keep one tool.
                var specific = ConstraintSpecific(component);

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(component),
                    type_fqn               = component.GetType().FullName,
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(component.gameObject),
                    gameobject_path        = GameObjectResolver.PathOf(component.gameObject),
                    constraint_active      = iface.constraintActive,
                    locked                 = iface.locked,
                    weight                 = iface.weight,
                    source_count           = iface.sourceCount,
                    sources                = sources.ToArray(),
                    specific,
                    warnings               = warnings.ToArray(),
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- constraint-source-add ----------
        [ReifyTool("constraint-source-add")]
        public static Task<object> SourceAdd(JToken args)
        {
            var sourcePath = args?.Value<string>("source_path")
                ?? throw new ArgumentException("source_path is required (scene path of the source Transform's GameObject).");
            var weight = args?.Value<float?>("weight") ?? 1f;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var (component, iface) = ResolveConstraint(args);
                var sourceGo = GameObjectResolver.ByPath(sourcePath)
                    ?? throw new InvalidOperationException($"source GameObject not found: {sourcePath}");

                Undo.RecordObject(component, $"Reify: add constraint source '{sourcePath}'");
                var index = iface.AddSource(new ConstraintSource
                {
                    sourceTransform = sourceGo.transform,
                    weight          = weight
                });

                EditorUtility.SetDirty(component);

                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(component),
                    gameobject_path = GameObjectResolver.PathOf(component.gameObject),
                    added_source_index = index,
                    source_path     = sourcePath,
                    weight,
                    new_source_count = iface.sourceCount,
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- constraint-source-remove ----------
        [ReifyTool("constraint-source-remove")]
        public static Task<object> SourceRemove(JToken args)
        {
            var index = args?.Value<int?>("source_index")
                ?? throw new ArgumentException("source_index is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var (component, iface) = ResolveConstraint(args);
                if (index < 0 || index >= iface.sourceCount)
                    throw new ArgumentException(
                        $"source_index {index} out of range [0..{iface.sourceCount - 1}].");

                Undo.RecordObject(component, "Reify: remove constraint source");
                var removed = iface.GetSource(index);
                iface.RemoveSource(index);
                EditorUtility.SetDirty(component);

                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(component),
                    gameobject_path = GameObjectResolver.PathOf(component.gameObject),
                    removed_index   = index,
                    removed_transform_path = removed.sourceTransform != null ?
                        GameObjectResolver.PathOf(removed.sourceTransform.gameObject) : null,
                    new_source_count = iface.sourceCount,
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- lod-group-inspect ----------
        [ReifyTool("lod-group-inspect")]
        public static Task<object> LodGroupInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var grp = ResolveLodGroup(args);
                var lods = grp.GetLODs();
                var warnings = new List<string>();
                if (lods == null || lods.Length == 0)
                    warnings.Add("LODGroup has no LODs configured — nothing will ever render.");
                if (lods != null)
                {
                    for (var i = 0; i < lods.Length; i++)
                    {
                        if (lods[i].renderers == null || lods[i].renderers.Length == 0)
                            warnings.Add($"LOD {i} has 0 renderers — blank at that distance.");
                    }
                }

                var lodList = new List<object>(lods?.Length ?? 0);
                if (lods != null)
                {
                    for (var i = 0; i < lods.Length; i++)
                    {
                        var lod = lods[i];
                        var rs = new List<object>(lod.renderers?.Length ?? 0);
                        if (lod.renderers != null)
                            foreach (var r in lod.renderers)
                                if (r != null)
                                    rs.Add(new
                                    {
                                        renderer_type   = r.GetType().FullName,
                                        instance_id     = GameObjectResolver.InstanceIdOf(r),
                                        gameobject_path = GameObjectResolver.PathOf(r.gameObject)
                                    });

                        lodList.Add(new
                        {
                            index                  = i,
                            screen_relative_height = lod.screenRelativeTransitionHeight,
                            fade_transition_width  = lod.fadeTransitionWidth,
                            renderer_count         = rs.Count,
                            renderers              = rs.ToArray()
                        });
                    }
                }

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(grp),
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(grp.gameObject),
                    gameobject_path        = GameObjectResolver.PathOf(grp.gameObject),
                    enabled                = grp.enabled,
                    lod_count              = lods?.Length ?? 0,
                    size                   = grp.size,
                    animate_cross_fading   = grp.animateCrossFading,
                    fade_mode              = grp.fadeMode.ToString(),
                    lods                   = lodList.ToArray(),
                    warnings               = warnings.ToArray(),
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- lod-group-force ----------
        [ReifyTool("lod-group-force")]
        public static Task<object> LodGroupForce(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var grp = ResolveLodGroup(args);
                var forceIdx = args?.Value<int?>("lod_index");
                if (forceIdx.HasValue)
                {
                    if (forceIdx.Value < -1 || forceIdx.Value >= grp.lodCount)
                        throw new ArgumentException(
                            $"lod_index {forceIdx} out of range [-1..{grp.lodCount - 1}] (-1 means unforce).");
                    grp.ForceLOD(forceIdx.Value);
                }
                else
                {
                    grp.ForceLOD(-1);
                }

                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(grp),
                    gameobject_path = GameObjectResolver.PathOf(grp.gameObject),
                    forced_index    = forceIdx ?? -1,
                    note            = "Pass lod_index=-1 (or omit) to unforce. ForceLOD state is not persisted.",
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static (Component component, IConstraint iface) ResolveConstraint(JToken args)
        {
            var go = ResolveGameObject(args);
            // Prefer a specific type name if provided (disambiguates GOs with
            // multiple constraint types on them).
            var typeHint = args?.Value<string>("constraint_type");
            if (!string.IsNullOrEmpty(typeHint))
            {
                foreach (var c in go.GetComponents<Component>())
                {
                    if (c is IConstraint ic && (c.GetType().FullName == typeHint || c.GetType().Name == typeHint))
                        return (c, ic);
                }
                throw new InvalidOperationException(
                    $"No constraint of type '{typeHint}' on GameObject '{GameObjectResolver.PathOf(go)}'.");
            }

            var matches = new List<Component>();
            foreach (var c in go.GetComponents<Component>())
                if (c is IConstraint) matches.Add(c);

            if (matches.Count == 0)
                throw new InvalidOperationException(
                    $"GameObject '{GameObjectResolver.PathOf(go)}' has no IConstraint-derived component.");
            if (matches.Count > 1)
            {
                var names = new List<string>();
                foreach (var c in matches) names.Add(c.GetType().FullName);
                throw new InvalidOperationException(
                    $"Multiple constraints on '{GameObjectResolver.PathOf(go)}': {string.Join(", ", names)}. Pass constraint_type to disambiguate.");
            }
            return (matches[0], (IConstraint)matches[0]);
        }

        private static object ConstraintSpecific(Component c)
        {
            switch (c)
            {
                case PositionConstraint pc: return new { kind = "Position", translation_axis = pc.translationAxis.ToString(), translation_offset = new { x = pc.translationOffset.x, y = pc.translationOffset.y, z = pc.translationOffset.z }, translation_at_rest = new { x = pc.translationAtRest.x, y = pc.translationAtRest.y, z = pc.translationAtRest.z } };
                case RotationConstraint rc: return new { kind = "Rotation", rotation_axis = rc.rotationAxis.ToString(), rotation_offset = new { x = rc.rotationOffset.x, y = rc.rotationOffset.y, z = rc.rotationOffset.z }, rotation_at_rest = new { x = rc.rotationAtRest.x, y = rc.rotationAtRest.y, z = rc.rotationAtRest.z } };
                case ScaleConstraint sc:    return new { kind = "Scale",    scaling_axis = sc.scalingAxis.ToString(), scale_offset = new { x = sc.scaleOffset.x, y = sc.scaleOffset.y, z = sc.scaleOffset.z }, scale_at_rest = new { x = sc.scaleAtRest.x, y = sc.scaleAtRest.y, z = sc.scaleAtRest.z } };
                case ParentConstraint pc:   return new { kind = "Parent",   translation_axis = pc.translationAxis.ToString(), rotation_axis = pc.rotationAxis.ToString(), translation_offsets_count = pc.translationOffsets?.Length ?? 0, rotation_offsets_count = pc.rotationOffsets?.Length ?? 0 };
                case AimConstraint ac:      return new { kind = "Aim",      aim_vector = new { x = ac.aimVector.x, y = ac.aimVector.y, z = ac.aimVector.z }, up_vector = new { x = ac.upVector.x, y = ac.upVector.y, z = ac.upVector.z }, world_up_type = ac.worldUpType.ToString(), rotation_axis = ac.rotationAxis.ToString() };
                case LookAtConstraint lc:   return new { kind = "LookAt",   roll = lc.roll, use_up_object = lc.useUpObject, world_up_object_path = lc.worldUpObject != null ? GameObjectResolver.PathOf(lc.worldUpObject.gameObject) : null };
                default:                    return new { kind = c.GetType().Name };
            }
        }

        private static LODGroup ResolveLodGroup(JToken args)
        {
            var go = ResolveGameObject(args);
            return go.GetComponent<LODGroup>()
                ?? throw new InvalidOperationException($"GameObject '{GameObjectResolver.PathOf(go)}' has no LODGroup.");
        }

        private static GameObject ResolveGameObject(JToken args)
        {
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var goPath     = args?.Value<string>("gameobject_path");

            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                if (obj is GameObject go) return go;
                if (obj is Component c)   return c.gameObject;
                throw new InvalidOperationException(
                    $"instance_id {instanceId} is neither a GameObject nor a Component.");
            }
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("Provide either instance_id or gameobject_path.");
            return GameObjectResolver.ByPath(goPath)
                ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
        }
    }
}
