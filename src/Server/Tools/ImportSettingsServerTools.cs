using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class ImportSettingsServerTools
{
    [McpServerTool(Name = "texture-import-inspect"), Description(
        "Read a texture's import settings: texture_type, shape, sprite mode, " +
        "readability, sRGB, alpha settings, mipmaps, filter/wrap/aniso, " +
        "max size, compression + quality + crunch, NPOT scale. Plus the " +
        "source texture's width/height when loaded. Warnings for common " +
        "misconfigurations (max_size wastage, mipmaps on sprites).")]
    public static async Task<JsonElement> TextureImportInspect(
        UnityClient unity,
        string asset_path,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("texture-import-inspect", new
    {
        asset_path
    }, ct);

    [McpServerTool(Name = "texture-import-set"), Description(
        "Modify a texture's import settings. Any combination of: " +
        "texture_type (Default/Sprite/NormalMap/etc.), is_readable, " +
        "mipmap_enabled, filter_mode (Point/Bilinear/Trilinear), wrap_mode " +
        "(Repeat/Clamp/Mirror/MirrorOnce), max_texture_size, " +
        "texture_compression (Uncompressed/Compressed/CompressedHQ/LQ), " +
        "srgb_texture, alpha_is_transparency, crunched_compression. " +
        "Triggers reimport. Returns {applied, before, after}.")]
    public static async Task<JsonElement> TextureImportSet(
        UnityClient unity,
        string asset_path,
        string? texture_type = null,
        bool? is_readable = null,
        bool? mipmap_enabled = null,
        string? filter_mode = null,
        string? wrap_mode = null,
        int? max_texture_size = null,
        string? texture_compression = null,
        bool? srgb_texture = null,
        bool? alpha_is_transparency = null,
        bool? crunched_compression = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("texture-import-set", new
    {
        asset_path,
        texture_type,
        is_readable,
        mipmap_enabled,
        filter_mode,
        wrap_mode,
        max_texture_size,
        texture_compression,
        srgb_texture,
        alpha_is_transparency,
        crunched_compression
    }, ct);

    [McpServerTool(Name = "audio-import-inspect"), Description(
        "Read an audio clip's import settings: force_to_mono, " +
        "load_in_background, ambisonic, and default_sample_settings " +
        "(load_type, compression_format, sample_rate_setting + override, " +
        "quality, preload_audio_data). Complements audio-clip-inspect " +
        "(which reads the decoded clip metadata).")]
    public static async Task<JsonElement> AudioImportInspect(
        UnityClient unity,
        string asset_path,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("audio-import-inspect", new
    {
        asset_path
    }, ct);

    [McpServerTool(Name = "audio-import-set"), Description(
        "Modify an audio clip's import settings. Top-level: force_to_mono, " +
        "load_in_background, ambisonic. Default sample settings: load_type " +
        "(DecompressOnLoad/CompressedInMemory/Streaming), compression_format " +
        "(PCM/ADPCM/Vorbis/MP3/AAC/etc.), sample_rate_setting " +
        "(Preserve/Optimize/Override), sample_rate_override (uint), quality " +
        "(0-1 float), preload_audio_data. Triggers reimport. Returns " +
        "{applied, before, after}.")]
    public static async Task<JsonElement> AudioImportSet(
        UnityClient unity,
        string asset_path,
        bool? force_to_mono = null,
        bool? load_in_background = null,
        bool? ambisonic = null,
        string? load_type = null,
        string? compression_format = null,
        string? sample_rate_setting = null,
        uint? sample_rate_override = null,
        float? quality = null,
        bool? preload_audio_data = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("audio-import-set", new
    {
        asset_path,
        force_to_mono,
        load_in_background,
        ambisonic,
        load_type,
        compression_format,
        sample_rate_setting,
        sample_rate_override,
        quality,
        preload_audio_data
    }, ct);
}
