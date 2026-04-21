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
    public static async Task<JsonElement> ParticleSystemInspect(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("particle-system-inspect", args, ct);

    [McpServerTool(Name = "particle-play"), Description(
        "Start a ParticleSystem. with_children (default true) cascades. " +
        "Returns {before, after}. Play is frame-delayed — is_playing may " +
        "flip to true on the next update.")]
    public static async Task<JsonElement> ParticlePlay(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("particle-play", args, ct);

    [McpServerTool(Name = "particle-stop"), Description(
        "Stop a ParticleSystem. stop_behavior ∈ {StopEmitting (keep live " +
        "particles), StopEmittingAndClear (default — wipe)}. with_children " +
        "default true.")]
    public static async Task<JsonElement> ParticleStop(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("particle-stop", args, ct);

    [McpServerTool(Name = "particle-pause"), Description(
        "Pause a ParticleSystem (particles stay, simulation halts).")]
    public static async Task<JsonElement> ParticlePause(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("particle-pause", args, ct);

    [McpServerTool(Name = "particle-simulate"), Description(
        "Fast-forward a ParticleSystem by `time` seconds. Args: time " +
        "(required), with_children (default true), restart (default " +
        "true — begin from t=0), fixed_time_step (default true). Useful " +
        "for preview / thumbnail / determinism.")]
    public static async Task<JsonElement> ParticleSimulate(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("particle-simulate", args, ct);

    [McpServerTool(Name = "particle-emit"), Description(
        "Emit `count` particles in one burst. Before/after particle_count " +
        "returned. count must be > 0.")]
    public static async Task<JsonElement> ParticleEmit(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("particle-emit", args, ct);
}
