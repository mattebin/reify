using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class AnimatorMutationTools
    {
        // ---------- animator-parameter-set ----------
        public static Task<object> ParameterSet(JToken args)
        {
            var id       = args?["animator_instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("animator_instance_id") : null;
            var goPath   = args?.Value<string>("gameobject_path");
            var paramName = args?.Value<string>("parameter_name") ?? throw new ArgumentException("parameter_name is required.");
            var value     = args?["value"] ?? throw new ArgumentException("value is required (ignored for Trigger — use true to fire).");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var animator = Resolve(id, goPath);
                if (animator.runtimeAnimatorController == null)
                    throw new InvalidOperationException(
                        "Animator has no RuntimeAnimatorController — no parameters exist. " +
                        "Assign a controller before setting parameters.");

                var p = FindParameter(animator, paramName);
                if (p == null)
                {
                    var valid = new List<string>();
                    foreach (var pp in animator.parameters ?? Array.Empty<AnimatorControllerParameter>())
                        valid.Add($"{pp.name}:{pp.type}");
                    throw new InvalidOperationException(
                        $"Parameter '{paramName}' not found on Animator. Valid: {string.Join(", ", valid)}.");
                }

                // Read BEFORE (gracefully — parameter may not be readable in edit mode yet).
                object before = null;
                try
                {
                    switch (p.type)
                    {
                        case AnimatorControllerParameterType.Bool:    before = animator.GetBool(paramName); break;
                        case AnimatorControllerParameterType.Int:     before = animator.GetInteger(paramName); break;
                        case AnimatorControllerParameterType.Float:   before = animator.GetFloat(paramName); break;
                        case AnimatorControllerParameterType.Trigger: before = animator.GetBool(paramName); break;
                    }
                }
                catch { }

                // Runtime parameter sets are not Undo-tracked by Unity — by
                // design, these are volatile runtime state. We mark the
                // owning GameObject dirty so saves capture any indirect
                // state the Animator serialises.
                Undo.RecordObject(animator, $"Reify: set animator parameter {paramName}");

                try
                {
                    switch (p.type)
                    {
                        case AnimatorControllerParameterType.Bool:
                            animator.SetBool(paramName, value.Value<bool>()); break;
                        case AnimatorControllerParameterType.Int:
                            animator.SetInteger(paramName, value.Value<int>()); break;
                        case AnimatorControllerParameterType.Float:
                            animator.SetFloat(paramName, value.Value<float>()); break;
                        case AnimatorControllerParameterType.Trigger:
                            if (value.Value<bool>()) animator.SetTrigger(paramName);
                            else animator.ResetTrigger(paramName);
                            break;
                    }
                }
                catch (FormatException)
                {
                    throw new InvalidOperationException(
                        $"Value type mismatch for parameter '{paramName}' ({p.type}). " +
                        "Pass a bool for Bool/Trigger, an int for Int, a float for Float.");
                }

                object after = null;
                try
                {
                    switch (p.type)
                    {
                        case AnimatorControllerParameterType.Bool:    after = animator.GetBool(paramName); break;
                        case AnimatorControllerParameterType.Int:     after = animator.GetInteger(paramName); break;
                        case AnimatorControllerParameterType.Float:   after = animator.GetFloat(paramName); break;
                        case AnimatorControllerParameterType.Trigger: after = animator.GetBool(paramName); break;
                    }
                }
                catch { }

                return new
                {
                    animator = new
                    {
                        instance_id     = GameObjectResolver.InstanceIdOf(animator),
                        gameobject_path = GameObjectResolver.PathOf(animator.gameObject)
                    },
                    parameter_name = paramName,
                    type           = p.type.ToString(),
                    before,
                    after,
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        // ---------- animator-crossfade ----------
        public static Task<object> CrossFade(JToken args)
        {
            var id       = args?["animator_instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("animator_instance_id") : null;
            var goPath   = args?.Value<string>("gameobject_path");
            var state    = args?.Value<string>("state_name") ?? throw new ArgumentException("state_name is required.");
            var duration = args?.Value<float?>("transition_duration") ?? 0.25f;
            var layer    = args?.Value<int?>("layer") ?? 0;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var animator = Resolve(id, goPath);
                if (animator.runtimeAnimatorController == null)
                    throw new InvalidOperationException("Animator has no RuntimeAnimatorController — CrossFade cannot resolve state.");
                if (layer < 0 || layer >= animator.layerCount)
                    throw new ArgumentException($"layer {layer} out of range [0..{animator.layerCount - 1}].");

                animator.CrossFade(state, duration, layer);
                return new
                {
                    animator = new
                    {
                        instance_id     = GameObjectResolver.InstanceIdOf(animator),
                        gameobject_path = GameObjectResolver.PathOf(animator.gameObject)
                    },
                    state_name          = state,
                    layer,
                    transition_duration = duration,
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }

        // ---------- animator-play ----------
        public static Task<object> Play(JToken args)
        {
            var id       = args?["animator_instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("animator_instance_id") : null;
            var goPath   = args?.Value<string>("gameobject_path");
            var state    = args?.Value<string>("state_name") ?? throw new ArgumentException("state_name is required.");
            var layer    = args?.Value<int?>("layer") ?? 0;
            var normTime = args?.Value<float?>("normalized_time") ?? 0f;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var animator = Resolve(id, goPath);
                if (animator.runtimeAnimatorController == null)
                    throw new InvalidOperationException("Animator has no RuntimeAnimatorController — Play cannot resolve state.");
                if (layer < 0 || layer >= animator.layerCount)
                    throw new ArgumentException($"layer {layer} out of range [0..{animator.layerCount - 1}].");

                animator.Play(state, layer, normTime);
                return new
                {
                    animator = new
                    {
                        instance_id     = GameObjectResolver.InstanceIdOf(animator),
                        gameobject_path = GameObjectResolver.PathOf(animator.gameObject)
                    },
                    state_name      = state,
                    layer,
                    normalized_time = normTime,
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static Animator Resolve(int? instanceId, string gameObjectPath)
        {
            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                return obj as Animator
                    ?? (obj as GameObject)?.GetComponent<Animator>()
                    ?? throw new InvalidOperationException(
                        $"instance_id {instanceId} does not resolve to an Animator or a GameObject with one.");
            }
            if (string.IsNullOrEmpty(gameObjectPath))
                throw new ArgumentException("Provide either animator_instance_id or gameobject_path.");
            var go = GameObjectResolver.ByPath(gameObjectPath)
                ?? throw new InvalidOperationException($"GameObject not found: {gameObjectPath}");
            return go.GetComponent<Animator>()
                ?? throw new InvalidOperationException($"GameObject '{gameObjectPath}' has no Animator component.");
        }

        private static AnimatorControllerParameter FindParameter(Animator a, string name)
        {
            var ps = a.parameters;
            if (ps == null) return null;
            foreach (var p in ps) if (p.name == name) return p;
            return null;
        }
    }
}
