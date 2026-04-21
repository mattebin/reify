using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// 4th Phase C philosophy tool. Full structured runtime state of an
    /// Animator — current state per layer, parameters with live values,
    /// active transitions, clip progress. Plus warnings for the common
    /// misconfigurations that cause T-poses and "why doesn't it animate"
    /// debugging loops.
    /// </summary>
    internal static class AnimatorStateTool
    {
        public static Task<object> Handle(JToken args)
        {
            var id     = args?["animator_instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("animator_instance_id") : null;
            var goPath = args?.Value<string>("gameobject_path");

            if (!id.HasValue && string.IsNullOrEmpty(goPath))
                throw new ArgumentException("Provide either animator_instance_id or gameobject_path.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                Animator animator;
                string   sourceType;
                string   sourceId;

                if (id.HasValue)
                {
                    var obj = GameObjectResolver.ByInstanceId(id.Value)
                        ?? throw new InvalidOperationException($"No object with instance_id {id}.");
                    animator = obj as Animator
                        ?? (obj as GameObject)?.GetComponent<Animator>()
                        ?? throw new InvalidOperationException(
                            $"instance_id {id} does not resolve to an Animator or a GameObject with one.");
                    sourceType = "animator";
                    sourceId   = id.Value.ToString();
                }
                else
                {
                    var go = GameObjectResolver.ByPath(goPath)
                        ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
                    animator = go.GetComponent<Animator>()
                        ?? throw new InvalidOperationException($"GameObject '{goPath}' has no Animator component.");
                    sourceType = "gameobject";
                    sourceId   = goPath;
                }

                return Build(animator, sourceType, sourceId);
            });
        }

        private static object Build(Animator animator, string sourceType, string sourceId)
        {
            var warnings = new List<string>();
            var runtimeController = animator.runtimeAnimatorController;

            // Editor-only resolution of the underlying AnimatorController for
            // state-name lookup. In play mode this is the same object; in
            // edit mode it's the authoring asset.
            var controller = runtimeController as AnimatorController;
            string controllerPath = runtimeController != null ? AssetDatabase.GetAssetPath(runtimeController) : null;

            if (runtimeController == null)
                warnings.Add("Animator has no RuntimeAnimatorController assigned. Nothing will animate.");
            else if (controller == null && runtimeController is AnimatorOverrideController)
                warnings.Add("Animator uses an AnimatorOverrideController — state-name lookups fall back to hash only.");

            if (animator.avatar == null)
                warnings.Add("Animator has no Avatar assigned — humanoid retargeting cannot resolve. Classic T-pose cause when the controller expects humanoid clips.");

            if (controller != null)
            {
                // Dead-parameter + unreachable-state analysis. Only runs when
                // we have the editor-side AnimatorController asset.
                AnalyseController(controller, warnings);
            }

            var layers = new List<object>(animator.layerCount);
            for (var i = 0; i < animator.layerCount; i++)
            {
                var layerName   = animator.GetLayerName(i);
                var layerWeight = animator.GetLayerWeight(i);
                var stateInfo   = animator.GetCurrentAnimatorStateInfo(i);
                var inTransit   = animator.IsInTransition(i);
                var nextInfo    = inTransit ? animator.GetNextAnimatorStateInfo(i) : default;
                var transitInfo = inTransit ? animator.GetAnimatorTransitionInfo(i) : default;
                var clipInfos   = animator.GetCurrentAnimatorClipInfo(i);
                var nextClips   = inTransit ? animator.GetNextAnimatorClipInfo(i) : null;

                string currentStateName = controller != null
                    ? FindStateName(controller, i, stateInfo.fullPathHash) : null;
                string nextStateName = controller != null && inTransit
                    ? FindStateName(controller, i, nextInfo.fullPathHash) : null;

                if (layerWeight == 0f && i > 0)
                    warnings.Add($"Layer {i} '{layerName}' has weight 0 — it contributes nothing to the pose. Set weight > 0 via animator.SetLayerWeight.");
                if (clipInfos.Length == 0)
                    warnings.Add($"Layer {i} '{layerName}' current state has no clip assigned (or Animator not yet initialised — common in edit mode before first Update).");

                layers.Add(new
                {
                    index  = i,
                    name   = layerName,
                    weight = layerWeight,
                    current_state = new
                    {
                        name            = currentStateName,
                        full_path_hash  = stateInfo.fullPathHash,
                        short_name_hash = stateInfo.shortNameHash,
                        normalized_time = stateInfo.normalizedTime,
                        length          = stateInfo.length,
                        speed           = stateInfo.speed,
                        is_looping      = stateInfo.loop,
                        clip            = PrimaryClip(clipInfos)
                    },
                    in_transition = inTransit,
                    next_state    = inTransit ? (object)new
                    {
                        name            = nextStateName,
                        full_path_hash  = nextInfo.fullPathHash,
                        normalized_time = nextInfo.normalizedTime,
                        clip            = PrimaryClip(nextClips)
                    } : null,
                    transition = inTransit ? (object)new
                    {
                        normalized_time = transitInfo.normalizedTime,
                        duration        = transitInfo.duration,
                        user_name       = transitInfo.name
                    } : null
                });
            }

            var parameters = new List<object>(animator.parameters?.Length ?? 0);
            if (animator.parameters != null)
            {
                foreach (var p in animator.parameters)
                {
                    object value = null;
                    try
                    {
                        switch (p.type)
                        {
                            case AnimatorControllerParameterType.Bool:    value = animator.GetBool(p.name); break;
                            case AnimatorControllerParameterType.Int:     value = animator.GetInteger(p.name); break;
                            case AnimatorControllerParameterType.Float:   value = animator.GetFloat(p.name); break;
                            case AnimatorControllerParameterType.Trigger: value = animator.GetBool(p.name); break;
                        }
                    }
                    catch { /* parameter not readable yet in edit mode */ }
                    parameters.Add(new
                    {
                        name    = p.name,
                        type    = p.type.ToString(),
                        value,
                        default_bool  = p.defaultBool,
                        default_int   = p.defaultInt,
                        default_float = p.defaultFloat,
                        name_hash     = p.nameHash
                    });
                }
            }

            return new
            {
                source          = new { type = sourceType, identifier = sourceId },
                gameobject_path = GameObjectResolver.PathOf(animator.gameObject),
                animator_instance_id = GameObjectResolver.InstanceIdOf(animator),
                controller_name = runtimeController != null ? runtimeController.name : null,
                controller_path = controllerPath,
                controller_is_override = runtimeController is AnimatorOverrideController,
                avatar = animator.avatar != null ? new
                {
                    name          = animator.avatar.name,
                    is_human      = animator.avatar.isHuman,
                    is_valid      = animator.avatar.isValid,
                    asset_path    = AssetDatabase.GetAssetPath(animator.avatar)
                } : null,
                apply_root_motion = animator.applyRootMotion,
                update_mode       = animator.updateMode.ToString(),
                culling_mode      = animator.cullingMode.ToString(),
                speed             = animator.speed,
                layer_count       = animator.layerCount,
                parameter_count   = animator.parameters?.Length ?? 0,
                layers            = layers.ToArray(),
                parameters        = parameters.ToArray(),
                warnings          = warnings.ToArray(),
                read_at_utc       = DateTime.UtcNow.ToString("o"),
                frame             = (long)Time.frameCount
            };
        }

        // ---------- controller analysis ----------

        private static string FindStateName(AnimatorController controller, int layerIndex, int fullPathHash)
        {
            if (layerIndex < 0 || layerIndex >= controller.layers.Length) return null;
            var sm = controller.layers[layerIndex].stateMachine;
            return WalkFindState(sm, layerPrefix: controller.layers[layerIndex].name, fullPathHash);
        }

        private static string WalkFindState(AnimatorStateMachine sm, string layerPrefix, int fullPathHash)
        {
            if (sm == null) return null;
            foreach (var cs in sm.states)
            {
                // Unity's fullPathHash is Animator.StringToHash(layer.name + "." + state.path)
                var path = layerPrefix + "." + cs.state.name;
                if (Animator.StringToHash(path) == fullPathHash) return cs.state.name;
            }
            foreach (var sub in sm.stateMachines)
            {
                var hit = WalkFindState(sub.stateMachine, layerPrefix + "." + sub.stateMachine.name, fullPathHash);
                if (hit != null) return hit;
            }
            return null;
        }

        private static void AnalyseController(AnimatorController controller, List<string> warnings)
        {
            // Collect every transition condition parameter and every state's
            // incoming transition map.
            var referencedParams = new HashSet<string>();
            var statesWithIncoming = new HashSet<int>();   // state.GetInstanceID()
            var allStates = new List<AnimatorState>();

            foreach (var layer in controller.layers)
                GatherFromStateMachine(layer.stateMachine, referencedParams, statesWithIncoming, allStates);

            foreach (var p in controller.parameters)
                if (!referencedParams.Contains(p.name))
                    warnings.Add($"Parameter '{p.name}' ({p.type}) is never read by any transition condition — dead parameter.");

            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;
                var defaultState = layer.stateMachine.defaultState;
                if (defaultState == null && layer.stateMachine.states.Length > 0)
                    warnings.Add($"Layer '{layer.name}' has states but no default state set — transitions may never fire.");

                foreach (var cs in layer.stateMachine.states)
                {
                    var id = GameObjectResolver.InstanceIdOf(cs.state);
                    if (cs.state == defaultState) continue;
                    if (statesWithIncoming.Contains(id)) continue;
                    warnings.Add($"State '{cs.state.name}' in layer '{layer.name}' has no incoming transitions and is not the default — unreachable without scripted CrossFade.");
                }

                foreach (var cs in layer.stateMachine.states)
                    if (cs.state.motion == null)
                        warnings.Add($"State '{cs.state.name}' in layer '{layer.name}' has no motion (clip) assigned — will produce no animation.");
            }
        }

        private static void GatherFromStateMachine(AnimatorStateMachine sm,
            HashSet<string> referencedParams, HashSet<int> statesWithIncoming, List<AnimatorState> allStates)
        {
            if (sm == null) return;
            foreach (var cs in sm.states) allStates.Add(cs.state);

            foreach (var cs in sm.states)
                foreach (var tr in cs.state.transitions)
                {
                    foreach (var cond in tr.conditions) referencedParams.Add(cond.parameter);
                    if (tr.destinationState != null)
                        statesWithIncoming.Add(GameObjectResolver.InstanceIdOf(tr.destinationState));
                }

            foreach (var anyTr in sm.anyStateTransitions)
            {
                foreach (var cond in anyTr.conditions) referencedParams.Add(cond.parameter);
                if (anyTr.destinationState != null)
                    statesWithIncoming.Add(GameObjectResolver.InstanceIdOf(anyTr.destinationState));
            }

            foreach (var sub in sm.stateMachines)
                GatherFromStateMachine(sub.stateMachine, referencedParams, statesWithIncoming, allStates);
        }

        private static object PrimaryClip(AnimatorClipInfo[] infos)
        {
            if (infos == null || infos.Length == 0 || infos[0].clip == null) return null;
            var c = infos[0].clip;
            return new
            {
                name      = c.name,
                length    = c.length,
                frame_rate = c.frameRate,
                is_looping = c.isLooping,
                weight    = infos[0].weight,
                asset_path = AssetDatabase.GetAssetPath(c)
            };
        }
    }
}
