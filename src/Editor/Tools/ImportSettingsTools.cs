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
    /// Per-asset import settings for Texture and Audio. Model importer
    /// is deferred — its surface is huge (rig, animation, LOD, materials,
    /// blendshapes) and deserves its own batch.
    /// </summary>
    internal static class ImportSettingsTools
    {
        // ---------- texture-import-inspect ----------
        [ReifyTool("texture-import-inspect")]
        public static Task<object> TextureInspect(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var ti = AssetImporter.GetAtPath(path) as TextureImporter
                    ?? throw new InvalidOperationException($"No TextureImporter at path: {path}");
                var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);

                var warnings = new List<string>();
                if (ti.textureType == TextureImporterType.Default && ti.alphaIsTransparency)
                    warnings.Add("alphaIsTransparency true with textureType=Default — typically set only for Sprites. Verify intent.");
                if (ti.maxTextureSize > 2048 && tex != null && (tex.width <= 512 || tex.height <= 512))
                    warnings.Add($"maxTextureSize={ti.maxTextureSize} but source is only {tex?.width}x{tex?.height} — wasted budget.");
                if (ti.mipmapEnabled && ti.textureType == TextureImporterType.Sprite)
                    warnings.Add("Mipmaps enabled for a Sprite — unusual. Most UI/sprite setups disable mipmaps.");

                return new
                {
                    asset_path               = path,
                    texture_type             = ti.textureType.ToString(),
                    texture_shape            = ti.textureShape.ToString(),
                    sprite_import_mode       = ti.spriteImportMode.ToString(),
                    sprite_pixels_per_unit   = ti.spritePixelsPerUnit,
                    is_readable              = ti.isReadable,
                    srgb_texture             = ti.sRGBTexture,
                    alpha_is_transparency    = ti.alphaIsTransparency,
                    alpha_source             = ti.alphaSource.ToString(),
                    mipmap_enabled           = ti.mipmapEnabled,
                    filter_mode              = ti.filterMode.ToString(),
                    wrap_mode                = ti.wrapMode.ToString(),
                    aniso_level              = ti.anisoLevel,
                    max_texture_size         = ti.maxTextureSize,
                    texture_compression      = ti.textureCompression.ToString(),
                    compression_quality      = ti.compressionQuality,
                    crunched_compression     = ti.crunchedCompression,
                    npot_scale               = ti.npotScale.ToString(),
                    source_width             = tex != null ? (int?)tex.width  : null,
                    source_height            = tex != null ? (int?)tex.height : null,
                    warnings                 = warnings.ToArray(),
                    read_at_utc              = DateTime.UtcNow.ToString("o"),
                    frame                    = (long)Time.frameCount
                };
            });
        }

        // ---------- texture-import-set ----------
        [ReifyTool("texture-import-set")]
        public static Task<object> TextureSet(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var ti = AssetImporter.GetAtPath(path) as TextureImporter
                    ?? throw new InvalidOperationException($"No TextureImporter at path: {path}");

                var before = new
                {
                    texture_type     = ti.textureType.ToString(),
                    is_readable      = ti.isReadable,
                    mipmap_enabled   = ti.mipmapEnabled,
                    filter_mode      = ti.filterMode.ToString(),
                    wrap_mode        = ti.wrapMode.ToString(),
                    max_texture_size = ti.maxTextureSize,
                    texture_compression = ti.textureCompression.ToString(),
                    srgb_texture     = ti.sRGBTexture,
                    alpha_is_transparency = ti.alphaIsTransparency,
                    crunched_compression  = ti.crunchedCompression
                };

                var applied = new List<string>();

                var tt = args?.Value<string>("texture_type");
                if (!string.IsNullOrEmpty(tt))
                {
                    if (!Enum.TryParse<TextureImporterType>(tt, true, out var e))
                        throw new ArgumentException($"texture_type '{tt}' is invalid.");
                    ti.textureType = e; applied.Add("texture_type");
                }
                var isReadable = args?.Value<bool?>("is_readable");
                if (isReadable.HasValue) { ti.isReadable = isReadable.Value; applied.Add("is_readable"); }
                var mipmap = args?.Value<bool?>("mipmap_enabled");
                if (mipmap.HasValue) { ti.mipmapEnabled = mipmap.Value; applied.Add("mipmap_enabled"); }
                var filter = args?.Value<string>("filter_mode");
                if (!string.IsNullOrEmpty(filter))
                {
                    if (!Enum.TryParse<FilterMode>(filter, true, out var e))
                        throw new ArgumentException($"filter_mode '{filter}' must be Point, Bilinear, or Trilinear.");
                    ti.filterMode = e; applied.Add("filter_mode");
                }
                var wrap = args?.Value<string>("wrap_mode");
                if (!string.IsNullOrEmpty(wrap))
                {
                    if (!Enum.TryParse<TextureWrapMode>(wrap, true, out var e))
                        throw new ArgumentException($"wrap_mode '{wrap}' must be Repeat, Clamp, Mirror, or MirrorOnce.");
                    ti.wrapMode = e; applied.Add("wrap_mode");
                }
                var maxSize = args?.Value<int?>("max_texture_size");
                if (maxSize.HasValue) { ti.maxTextureSize = maxSize.Value; applied.Add("max_texture_size"); }
                var compression = args?.Value<string>("texture_compression");
                if (!string.IsNullOrEmpty(compression))
                {
                    if (!Enum.TryParse<TextureImporterCompression>(compression, true, out var e))
                        throw new ArgumentException($"texture_compression '{compression}' must be Uncompressed, Compressed, CompressedHQ, or CompressedLQ.");
                    ti.textureCompression = e; applied.Add("texture_compression");
                }
                var srgb = args?.Value<bool?>("srgb_texture");
                if (srgb.HasValue) { ti.sRGBTexture = srgb.Value; applied.Add("srgb_texture"); }
                var alphaTransparency = args?.Value<bool?>("alpha_is_transparency");
                if (alphaTransparency.HasValue) { ti.alphaIsTransparency = alphaTransparency.Value; applied.Add("alpha_is_transparency"); }
                var crunch = args?.Value<bool?>("crunched_compression");
                if (crunch.HasValue) { ti.crunchedCompression = crunch.Value; applied.Add("crunched_compression"); }

                if (applied.Count == 0)
                    throw new ArgumentException(
                        "No writable fields provided. Expected at least one of: texture_type, is_readable, mipmap_enabled, filter_mode, wrap_mode, max_texture_size, texture_compression, srgb_texture, alpha_is_transparency, crunched_compression.");

                ti.SaveAndReimport();

                return new
                {
                    asset_path = path,
                    applied,
                    before,
                    after = new
                    {
                        texture_type     = ti.textureType.ToString(),
                        is_readable      = ti.isReadable,
                        mipmap_enabled   = ti.mipmapEnabled,
                        filter_mode      = ti.filterMode.ToString(),
                        wrap_mode        = ti.wrapMode.ToString(),
                        max_texture_size = ti.maxTextureSize,
                        texture_compression = ti.textureCompression.ToString(),
                        srgb_texture     = ti.sRGBTexture,
                        alpha_is_transparency = ti.alphaIsTransparency,
                        crunched_compression  = ti.crunchedCompression
                    },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- audio-import-inspect ----------
        [ReifyTool("audio-import-inspect")]
        public static Task<object> AudioImportInspect(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var ai = AssetImporter.GetAtPath(path) as AudioImporter
                    ?? throw new InvalidOperationException($"No AudioImporter at path: {path}");

                var dss = ai.defaultSampleSettings;
                return new
                {
                    asset_path          = path,
                    force_to_mono       = ai.forceToMono,
                    load_in_background  = ai.loadInBackground,
                    ambisonic           = ai.ambisonic,
                    default_sample_settings = new
                    {
                        load_type            = dss.loadType.ToString(),
                        compression_format   = dss.compressionFormat.ToString(),
                        sample_rate_setting  = dss.sampleRateSetting.ToString(),
                        sample_rate_override = dss.sampleRateOverride,
                        quality              = dss.quality,
                        preload_audio_data   = dss.preloadAudioData
                    },
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }

        // ---------- audio-import-set ----------
        [ReifyTool("audio-import-set")]
        public static Task<object> AudioImportSet(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var ai = AssetImporter.GetAtPath(path) as AudioImporter
                    ?? throw new InvalidOperationException($"No AudioImporter at path: {path}");

                var before = new
                {
                    force_to_mono      = ai.forceToMono,
                    load_in_background = ai.loadInBackground,
                    ambisonic          = ai.ambisonic,
                    default_sample_settings = new
                    {
                        load_type            = ai.defaultSampleSettings.loadType.ToString(),
                        compression_format   = ai.defaultSampleSettings.compressionFormat.ToString(),
                        sample_rate_setting  = ai.defaultSampleSettings.sampleRateSetting.ToString(),
                        sample_rate_override = ai.defaultSampleSettings.sampleRateOverride,
                        quality              = ai.defaultSampleSettings.quality,
                        preload_audio_data   = ai.defaultSampleSettings.preloadAudioData
                    }
                };

                var applied = new List<string>();

                var mono = args?.Value<bool?>("force_to_mono");
                if (mono.HasValue) { ai.forceToMono = mono.Value; applied.Add("force_to_mono"); }
                var lb = args?.Value<bool?>("load_in_background");
                if (lb.HasValue)   { ai.loadInBackground = lb.Value; applied.Add("load_in_background"); }
                var amb = args?.Value<bool?>("ambisonic");
                if (amb.HasValue)  { ai.ambisonic = amb.Value; applied.Add("ambisonic"); }

                // Sample settings are a struct — read, modify, write-back.
                var dss = ai.defaultSampleSettings;
                var ssChanged = false;

                var lt = args?.Value<string>("load_type");
                if (!string.IsNullOrEmpty(lt))
                {
                    if (!Enum.TryParse<AudioClipLoadType>(lt, true, out var e))
                        throw new ArgumentException($"load_type '{lt}' must be DecompressOnLoad, CompressedInMemory, or Streaming.");
                    dss.loadType = e; ssChanged = true; applied.Add("load_type");
                }
                var cf = args?.Value<string>("compression_format");
                if (!string.IsNullOrEmpty(cf))
                {
                    if (!Enum.TryParse<AudioCompressionFormat>(cf, true, out var e))
                        throw new ArgumentException($"compression_format '{cf}' is invalid (PCM/ADPCM/Vorbis/MP3/AAC/etc.).");
                    dss.compressionFormat = e; ssChanged = true; applied.Add("compression_format");
                }
                var srs = args?.Value<string>("sample_rate_setting");
                if (!string.IsNullOrEmpty(srs))
                {
                    if (!Enum.TryParse<AudioSampleRateSetting>(srs, true, out var e))
                        throw new ArgumentException($"sample_rate_setting '{srs}' must be PreserveSampleRate, OptimizeSampleRate, or OverrideSampleRate.");
                    dss.sampleRateSetting = e; ssChanged = true; applied.Add("sample_rate_setting");
                }
                var sro = args?.Value<uint?>("sample_rate_override");
                if (sro.HasValue) { dss.sampleRateOverride = sro.Value; ssChanged = true; applied.Add("sample_rate_override"); }
                var quality = args?.Value<float?>("quality");
                if (quality.HasValue) { dss.quality = quality.Value; ssChanged = true; applied.Add("quality"); }
                var preload = args?.Value<bool?>("preload_audio_data");
                if (preload.HasValue) { dss.preloadAudioData = preload.Value; ssChanged = true; applied.Add("preload_audio_data"); }

                if (ssChanged) ai.defaultSampleSettings = dss;

                if (applied.Count == 0)
                    throw new ArgumentException(
                        "No writable fields provided. Expected at least one of: force_to_mono, load_in_background, ambisonic, load_type, compression_format, sample_rate_setting, sample_rate_override, quality, preload_audio_data.");

                ai.SaveAndReimport();

                return new
                {
                    asset_path = path,
                    applied,
                    before,
                    after = new
                    {
                        force_to_mono      = ai.forceToMono,
                        load_in_background = ai.loadInBackground,
                        ambisonic          = ai.ambisonic,
                        default_sample_settings = new
                        {
                            load_type            = ai.defaultSampleSettings.loadType.ToString(),
                            compression_format   = ai.defaultSampleSettings.compressionFormat.ToString(),
                            sample_rate_setting  = ai.defaultSampleSettings.sampleRateSetting.ToString(),
                            sample_rate_override = ai.defaultSampleSettings.sampleRateOverride,
                            quality              = ai.defaultSampleSettings.quality,
                            preload_audio_data   = ai.defaultSampleSettings.preloadAudioData
                        }
                    },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }
    }
}
