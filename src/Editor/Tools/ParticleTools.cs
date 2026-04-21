using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// ParticleSystem inspection + control. Read-only summaries of the
    /// main/emission/shape modules + imperative play/stop/pause/simulate/
    /// emit actions. Deep module editing is deferred — SerializedProperty
    /// access via component-modify/component-set-property already covers
    /// every module field by path (e.g. "main.startSpeed.scalar").
    /// </summary>
    internal static class ParticleTools
    {
        // ---------- particle-system-inspect ----------
        [ReifyTool("particle-system-inspect")]
        public static Task<object> Inspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var ps = ResolvePS(args);
                var warnings = new List<string>();

                if (!ps.gameObject.activeInHierarchy)
                    warnings.Add("GameObject is inactive — ParticleSystem won't simulate.");
                var emission = ps.emission;
                if (!emission.enabled)
                    warnings.Add("emission module is disabled — no particles will be born.");
                var main = ps.main;
                if (main.startLifetime.constant <= 0f && main.startLifetime.mode == ParticleSystemCurveMode.Constant)
                    warnings.Add($"startLifetime is {main.startLifetime.constant:F3} — particles die instantly.");
                if (main.maxParticles <= 0)
                    warnings.Add($"maxParticles is {main.maxParticles} — capacity is zero.");

                var shape = ps.shape;
                var renderer = ps.GetComponent<ParticleSystemRenderer>();

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(ps),
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(ps.gameObject),
                    gameobject_path        = GameObjectResolver.PathOf(ps.gameObject),
                    is_playing             = ps.isPlaying,
                    is_emitting            = ps.isEmitting,
                    is_paused              = ps.isPaused,
                    is_stopped             = ps.isStopped,
                    particle_count         = ps.particleCount,
                    time                   = ps.time,
                    total_time             = ps.totalTime,
                    main_module = new
                    {
                        duration                = main.duration,
                        loop                    = main.loop,
                        prewarm                 = main.prewarm,
                        start_delay_constant    = main.startDelay.constant,
                        start_lifetime_mode     = main.startLifetime.mode.ToString(),
                        start_lifetime_constant = main.startLifetime.constant,
                        start_speed_mode        = main.startSpeed.mode.ToString(),
                        start_speed_constant    = main.startSpeed.constant,
                        start_size_mode         = main.startSize.mode.ToString(),
                        start_size_constant     = main.startSize.constant,
                        start_color_mode        = main.startColor.mode.ToString(),
                        gravity_modifier_const  = main.gravityModifier.constant,
                        simulation_space        = main.simulationSpace.ToString(),
                        play_on_awake           = main.playOnAwake,
                        max_particles           = main.maxParticles,
                        stop_action             = main.stopAction.ToString(),
                        culling_mode            = main.cullingMode.ToString()
                    },
                    emission_module = new
                    {
                        enabled             = emission.enabled,
                        rate_over_time_mode = emission.rateOverTime.mode.ToString(),
                        rate_over_time_const = emission.rateOverTime.constant,
                        rate_over_distance_const = emission.rateOverDistance.constant,
                        burst_count         = emission.burstCount
                    },
                    shape_module = new
                    {
                        enabled   = shape.enabled,
                        shape     = shape.shapeType.ToString(),
                        radius    = shape.radius,
                        angle     = shape.angle,
                        arc       = shape.arc,
                        position  = new { x = shape.position.x, y = shape.position.y, z = shape.position.z },
                        rotation  = new { x = shape.rotation.x, y = shape.rotation.y, z = shape.rotation.z }
                    },
                    renderer = renderer != null ? new
                    {
                        render_mode    = renderer.renderMode.ToString(),
                        material_name  = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : null,
                        material_path  = renderer.sharedMaterial != null ? AssetDatabase.GetAssetPath(renderer.sharedMaterial) : null,
                        sort_mode      = renderer.sortMode.ToString(),
                        sorting_layer  = renderer.sortingLayerName,
                        sorting_order  = renderer.sortingOrder,
                        enabled        = renderer.enabled
                    } : null,
                    warnings    = warnings.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- particle-play ----------
        [ReifyTool("particle-play")]
        public static Task<object> Play(JToken args)
        {
            var withChildren = args?.Value<bool?>("with_children") ?? true;
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var ps = ResolvePS(args);
                var before = new { is_playing = ps.isPlaying, is_paused = ps.isPaused, particle_count = ps.particleCount };
                ps.Play(withChildren);
                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(ps),
                    gameobject_path = GameObjectResolver.PathOf(ps.gameObject),
                    with_children   = withChildren,
                    before,
                    after           = new { is_playing = ps.isPlaying, is_paused = ps.isPaused, particle_count = ps.particleCount },
                    note            = "Play is frame-delayed — is_playing may flip to true on the next update.",
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- particle-stop ----------
        [ReifyTool("particle-stop")]
        public static Task<object> Stop(JToken args)
        {
            var withChildren = args?.Value<bool?>("with_children") ?? true;
            var behaviorStr  = args?.Value<string>("stop_behavior") ?? "StopEmittingAndClear";

            if (!Enum.TryParse<ParticleSystemStopBehavior>(behaviorStr, true, out var behavior))
                throw new ArgumentException(
                    $"stop_behavior '{behaviorStr}' must be StopEmitting or StopEmittingAndClear.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var ps = ResolvePS(args);
                var before = new { is_playing = ps.isPlaying, is_stopped = ps.isStopped, particle_count = ps.particleCount };
                ps.Stop(withChildren, behavior);
                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(ps),
                    gameobject_path = GameObjectResolver.PathOf(ps.gameObject),
                    with_children   = withChildren,
                    stop_behavior   = behavior.ToString(),
                    before,
                    after           = new { is_playing = ps.isPlaying, is_stopped = ps.isStopped, particle_count = ps.particleCount },
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- particle-pause ----------
        [ReifyTool("particle-pause")]
        public static Task<object> Pause(JToken args)
        {
            var withChildren = args?.Value<bool?>("with_children") ?? true;
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var ps = ResolvePS(args);
                var before = new { is_playing = ps.isPlaying, is_paused = ps.isPaused };
                ps.Pause(withChildren);
                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(ps),
                    gameobject_path = GameObjectResolver.PathOf(ps.gameObject),
                    with_children   = withChildren,
                    before,
                    after           = new { is_playing = ps.isPlaying, is_paused = ps.isPaused },
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- particle-simulate ----------
        [ReifyTool("particle-simulate")]
        public static Task<object> Simulate(JToken args)
        {
            var t = args?.Value<float?>("time") ?? throw new ArgumentException("time is required (seconds).");
            var withChildren = args?.Value<bool?>("with_children") ?? true;
            var restart      = args?.Value<bool?>("restart") ?? true;
            var fixedTime    = args?.Value<bool?>("fixed_time_step") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var ps = ResolvePS(args);
                ps.Simulate(t, withChildren, restart, fixedTime);
                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(ps),
                    gameobject_path = GameObjectResolver.PathOf(ps.gameObject),
                    simulated_time  = t,
                    with_children   = withChildren,
                    restart,
                    fixed_time_step = fixedTime,
                    after           = new {
                        time           = ps.time,
                        particle_count = ps.particleCount,
                        is_playing     = ps.isPlaying
                    },
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- particle-emit ----------
        [ReifyTool("particle-emit")]
        public static Task<object> Emit(JToken args)
        {
            var count = args?.Value<int?>("count") ?? throw new ArgumentException("count is required.");
            if (count <= 0) throw new ArgumentException($"count must be > 0, got {count}.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var ps = ResolvePS(args);
                var before = ps.particleCount;
                ps.Emit(count);
                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(ps),
                    gameobject_path = GameObjectResolver.PathOf(ps.gameObject),
                    requested_count = count,
                    before_particle_count = before,
                    after_particle_count  = ps.particleCount,
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static ParticleSystem ResolvePS(JToken args)
        {
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var goPath     = args?.Value<string>("gameobject_path");

            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                return obj as ParticleSystem
                    ?? (obj as GameObject)?.GetComponent<ParticleSystem>()
                    ?? throw new InvalidOperationException(
                        $"instance_id {instanceId} does not resolve to a ParticleSystem or a GameObject with one.");
            }
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("Provide either instance_id or gameobject_path.");
            var go = GameObjectResolver.ByPath(goPath)
                ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
            return go.GetComponent<ParticleSystem>()
                ?? throw new InvalidOperationException($"GameObject '{goPath}' has no ParticleSystem component.");
        }
    }
}
