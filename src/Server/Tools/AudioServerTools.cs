using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class AudioServerTools
{
    [McpServerTool(Name = "audio-source-inspect"), Description(
        "Read full state of an AudioSource: enabled, is_playing, time, clip " +
        "(with length/channels/frequency/load_type), output_mixer_group, " +
        "mute/bypass flags, volume, pitch, spatialBlend, 3D rolloff config. " +
        "Resolve by instance_id or gameobject_path. Warnings flag: no clip, " +
        "muted, zero volume, no mixer group (effects won't apply), partial " +
        "spatialBlend, disabled component.")]
    public static async Task<JsonElement> AudioSourceInspect(UnityClient unity,
        int? instance_id, string? gameobject_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("audio-source-inspect",
        new AudioSourceRefArgs(instance_id, gameobject_path), ct);

    [McpServerTool(Name = "audio-source-play"), Description(
        "Play an AudioSource. Modes: default (mode='play', uses AudioSource.Play) " +
        "or one_shot=true (AudioSource.PlayOneShot, supports volume_scale, " +
        "doesn't replace the assigned clip). Optional clip_asset_path overrides " +
        "the clip for this call. Returns {before, after, clip_used}. isPlaying " +
        "may flip on the next frame — Unity's Play is async.")]
    public static async Task<JsonElement> AudioSourcePlay(UnityClient unity,
        int? instance_id, string? gameobject_path, bool? one_shot,
        float? volume_scale, string? clip_asset_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("audio-source-play",
        new AudioSourcePlayArgs(instance_id, gameobject_path, one_shot, volume_scale, clip_asset_path), ct);

    [McpServerTool(Name = "audio-source-stop"), Description(
        "Stop an AudioSource. Returns {was_playing, is_playing} for read-back.")]
    public static async Task<JsonElement> AudioSourceStop(UnityClient unity,
        int? instance_id, string? gameobject_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("audio-source-stop",
        new AudioSourceRefArgs(instance_id, gameobject_path), ct);

    [McpServerTool(Name = "audio-listener-state"), Description(
        "Structured state of every AudioListener in the loaded scenes + " +
        "global listener settings (volume, pause). Warnings: zero listeners " +
        "(nothing heard), multiple listeners (Unity picks one), global mute, " +
        "global pause. Fast one-call answer to 'why can't I hear anything?'.")]
    public static async Task<JsonElement> AudioListenerState(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("audio-listener-state", null, ct);

    [McpServerTool(Name = "audio-mixer-inspect"), Description(
        "Inspect an AudioMixer: groups (name + instance_id), exposed " +
        "parameters (with current values), output group, update mode. " +
        "Resolve by asset_path or instance_id. Exposed-parameter list is " +
        "read via SerializedObject; flagged as <error> if Unity's internal " +
        "mixer layout differs from the expected shape.")]
    public static async Task<JsonElement> AudioMixerInspect(UnityClient unity,
        string? asset_path, int? instance_id, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("audio-mixer-inspect",
        new AudioMixerRefArgs(asset_path, instance_id), ct);

    [McpServerTool(Name = "audio-mixer-set-exposed"), Description(
        "Set (or clear) an exposed AudioMixer parameter. Args: mixer reference " +
        "(asset_path or instance_id), parameter_name, value (required unless " +
        "clear=true), optional clear=true to revert to the mixer's baked value. " +
        "Returns {before, after} for verification. Fails structurally if the " +
        "parameter isn't exposed — the error points the caller at the Mixer " +
        "window.")]
    public static async Task<JsonElement> AudioMixerSetExposed(UnityClient unity,
        string? asset_path, int? instance_id, string parameter_name,
        float? value, bool? clear, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("audio-mixer-set-exposed",
        new AudioMixerSetExposedArgs(asset_path, instance_id, parameter_name, value, clear), ct);

    [McpServerTool(Name = "audio-clip-inspect"), Description(
        "Read an AudioClip's metadata (length, channels, frequency, samples, " +
        "load_type, load_state) + the AudioImporter's default sample settings " +
        "(load_type, compression_format, sample_rate_setting, quality, " +
        "preload_audio_data). Warnings: zero length (import issue), " +
        "DecompressOnLoad on long clips (memory), Streaming on short clips " +
        "(overhead). Evidence-first — no raw audio samples returned.")]
    public static async Task<JsonElement> AudioClipInspect(UnityClient unity,
        string asset_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("audio-clip-inspect",
        new AudioClipInspectArgs(asset_path), ct);
}
