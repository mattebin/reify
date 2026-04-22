using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class ParticleServerTools
{
    [McpServerTool(Name = "particle-system-inspect"), Description(
        "Inspect a ParticleSystem: is_playing/emitting/paused/stopped, " +
        "particle_count, time, main/emission/shape module snapshots, " +
        "ParticleSystemRenderer (render_mode, material, sort). Warnings " +
        "for inactive GO, disabled emission, zero lifetime, zero " +
        "maxParticles. For deep module edits use component-set-property " +
        "with SerializedProperty paths.")]
    public static async Task<JsonElement> ParticleSystemInspect(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("particle-system-inspect", new
    {
        instance_id, gameobject_path
    }, ct);

    [McpServerTool(Name = "particle-play"), Description(
        "Start a ParticleSystem. with_children (default true) cascades. " +
        "Returns {before, after}. Play is frame-delayed — is_playing may " +
        "flip to true on the next update.")]
    public static async Task<JsonElement> ParticlePlay(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        bool? with_children = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("particle-play", new
    {
        instance_id, gameobject_path, with_children
    }, ct);

    [McpServerTool(Name = "particle-stop"), Description(
        "Stop a ParticleSystem. stop_behavior ∈ {StopEmitting (keep live " +
        "particles), StopEmittingAndClear (default — wipe)}. with_children " +
        "default true.")]
    public static async Task<JsonElement> ParticleStop(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        bool? with_children = null,
        string? stop_behavior = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("particle-stop", new
    {
        instance_id, gameobject_path, with_children, stop_behavior
    }, ct);

    [McpServerTool(Name = "particle-pause"), Description(
        "Pause a ParticleSystem (particles stay, simulation halts).")]
    public static async Task<JsonElement> ParticlePause(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        bool? with_children = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("particle-pause", new
    {
        instance_id, gameobject_path, with_children
    }, ct);

    [McpServerTool(Name = "particle-simulate"), Description(
        "Fast-forward a ParticleSystem by `time` seconds. with_children " +
        "(default true), restart (default true — begin from t=0), " +
        "fixed_time_step (default true). Useful for preview / thumbnail / " +
        "determinism.")]
    public static async Task<JsonElement> ParticleSimulate(
        UnityClient unity,
        float time,
        int? instance_id = null,
        string? gameobject_path = null,
        bool? with_children = null,
        bool? restart = null,
        bool? fixed_time_step = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("particle-simulate", new
    {
        instance_id, gameobject_path, time, with_children, restart, fixed_time_step
    }, ct);

    [McpServerTool(Name = "particle-emit"), Description(
        "Emit `count` particles in one burst. Before/after particle_count " +
        "returned. count must be > 0.")]
    public static async Task<JsonElement> ParticleEmit(
        UnityClient unity,
        int count,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("particle-emit", new
    {
        instance_id, gameobject_path, count
    }, ct);
}
