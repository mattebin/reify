using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Audio domain — AudioSource / AudioListener / AudioMixer / AudioClip.
    /// Read-first with targeted mutations (source play/stop, exposed mixer
    /// param set). Every response carries read_at_utc + frame and resolves
    /// subjects by stable instance_id or asset_path. Warnings surface the
    /// common "why no sound" misconfigurations.
    /// </summary>
    internal static class AudioTools
    {
        // ---------- audio-source-inspect ----------
        [ReifyTool("audio-source-inspect")]
        public static Task<object> SourceInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var src = ResolveSource(args);
                var warnings = new List<string>();

                if (src.clip == null)
                    warnings.Add("AudioSource has no clip assigned — Play/PlayOneShot will be no-ops unless a clip is passed explicitly.");
                if (src.mute)
                    warnings.Add("AudioSource is muted — no output regardless of volume.");
                if (src.volume <= 0f)
                    warnings.Add($"AudioSource volume is {src.volume:F3} (<=0) — silent.");
                if (src.outputAudioMixerGroup == null)
                    warnings.Add("AudioSource has no output mixer group — routes to master; mixer effects won't apply.");
                if (src.spatialBlend > 0.01f && src.spatialBlend < 0.99f)
                    warnings.Add($"spatialBlend is {src.spatialBlend:F2} — partial 2D/3D blend. Intentional? Usually 0 (2D) or 1 (3D).");
                if (!src.enabled)
                    warnings.Add("AudioSource component is disabled — will not play until re-enabled.");

                var clipInfo = src.clip == null ? null : new
                {
                    name        = src.clip.name,
                    asset_path  = AssetDatabase.GetAssetPath(src.clip),
                    instance_id = GameObjectResolver.InstanceIdOf(src.clip),
                    length      = src.clip.length,
                    channels    = src.clip.channels,
                    frequency   = src.clip.frequency,
                    samples     = src.clip.samples,
                    load_type   = src.clip.loadType.ToString(),
                    is_loading  = src.clip.loadState.ToString()
                };

                var mixerGroup = src.outputAudioMixerGroup;

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(src),
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(src.gameObject),
                    gameobject_path        = GameObjectResolver.PathOf(src.gameObject),
                    enabled                = src.enabled,
                    is_playing             = src.isPlaying,
                    time                   = src.time,
                    time_samples           = src.timeSamples,
                    clip                   = clipInfo,
                    output_mixer_group     = mixerGroup == null ? null : new
                    {
                        name             = mixerGroup.name,
                        mixer_asset_path = mixerGroup.audioMixer != null ? AssetDatabase.GetAssetPath(mixerGroup.audioMixer) : null,
                        mixer_name       = mixerGroup.audioMixer != null ? mixerGroup.audioMixer.name : null
                    },
                    mute             = src.mute,
                    bypass_effects   = src.bypassEffects,
                    bypass_listener_effects = src.bypassListenerEffects,
                    bypass_reverb_zones     = src.bypassReverbZones,
                    play_on_awake    = src.playOnAwake,
                    loop             = src.loop,
                    priority         = src.priority,
                    volume           = src.volume,
                    pitch            = src.pitch,
                    stereo_pan       = src.panStereo,
                    spatial_blend    = src.spatialBlend,
                    reverb_zone_mix  = src.reverbZoneMix,
                    doppler_level    = src.dopplerLevel,
                    spread           = src.spread,
                    rolloff_mode     = src.rolloffMode.ToString(),
                    min_distance     = src.minDistance,
                    max_distance     = src.maxDistance,
                    spatialize       = src.spatialize,
                    spatialize_post_effects = src.spatializePostEffects,
                    warnings         = warnings.ToArray(),
                    read_at_utc      = DateTime.UtcNow.ToString("o"),
                    frame            = (long)Time.frameCount
                };
            });
        }

        // ---------- audio-source-play ----------
        [ReifyTool("audio-source-play")]
        public static Task<object> SourcePlay(JToken args)
        {
            var oneShot   = args?.Value<bool?>("one_shot") ?? false;
            var volume    = args?.Value<float?>("volume_scale");        // PlayOneShot scalar
            var clipPath  = args?.Value<string>("clip_asset_path");     // optional override

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var src = ResolveSource(args);

                // Optional clip override — resolve BEFORE the play call.
                AudioClip overrideClip = null;
                if (!string.IsNullOrEmpty(clipPath))
                {
                    overrideClip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath)
                        ?? throw new InvalidOperationException($"AudioClip not found at asset_path: {clipPath}");
                }

                var before = new { is_playing = src.isPlaying, time = src.time, clip = src.clip != null ? src.clip.name : null };

                if (oneShot)
                {
                    var clip = overrideClip ?? src.clip
                        ?? throw new InvalidOperationException("PlayOneShot requires a clip — AudioSource has none and no clip_asset_path was provided.");
                    src.PlayOneShot(clip, volume ?? 1f);
                }
                else
                {
                    if (overrideClip != null) src.clip = overrideClip;
                    if (src.clip == null)
                        throw new InvalidOperationException("AudioSource has no clip assigned and no clip_asset_path was provided.");
                    src.Play();
                }

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(src),
                    gameobject_path        = GameObjectResolver.PathOf(src.gameObject),
                    mode                   = oneShot ? "one_shot" : "play",
                    clip_used              = (overrideClip ?? src.clip)?.name,
                    volume_scale           = volume,
                    before,
                    after                  = new { is_playing = src.isPlaying, time = src.time, clip = src.clip != null ? src.clip.name : null },
                    note                   = "Play is async — isPlaying may flip to true on the next frame, not immediately.",
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- audio-source-stop ----------
        [ReifyTool("audio-source-stop")]
        public static Task<object> SourceStop(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var src = ResolveSource(args);
                var wasPlaying = src.isPlaying;
                src.Stop();
                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(src),
                    gameobject_path = GameObjectResolver.PathOf(src.gameObject),
                    was_playing     = wasPlaying,
                    is_playing      = src.isPlaying,
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- audio-listener-state ----------
        [ReifyTool("audio-listener-state")]
        public static Task<object> ListenerState(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                #pragma warning disable CS0618
                var listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
                #pragma warning restore CS0618

                var warnings = new List<string>();
                if (listeners.Length == 0) warnings.Add("No AudioListener in any loaded scene — nothing will be heard.");
                if (listeners.Length > 1)  warnings.Add($"{listeners.Length} AudioListeners found — Unity expects exactly one. Extras get ignored but may shift.");
                if (AudioListener.pause)   warnings.Add("AudioListener.pause is true — all audio is globally paused.");
                if (AudioListener.volume <= 0f) warnings.Add($"AudioListener.volume is {AudioListener.volume:F3} — silent globally.");

                var list = new List<object>(listeners.Length);
                foreach (var l in listeners)
                {
                    list.Add(new
                    {
                        instance_id            = GameObjectResolver.InstanceIdOf(l),
                        gameobject_instance_id = GameObjectResolver.InstanceIdOf(l.gameObject),
                        gameobject_path        = GameObjectResolver.PathOf(l.gameObject),
                        enabled                = l.enabled,
                        active_in_hierarchy    = l.gameObject.activeInHierarchy,
                        world_position         = V(l.transform.position),
                        world_rotation_euler   = V(l.transform.eulerAngles),
                        velocity_update_mode   = l.velocityUpdateMode.ToString()
                    });
                }

                return new
                {
                    listener_count = listeners.Length,
                    listeners      = list.ToArray(),
                    global_volume  = AudioListener.volume,
                    global_paused  = AudioListener.pause,
                    warnings       = warnings.ToArray(),
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        // ---------- audio-mixer-inspect ----------
        [ReifyTool("audio-mixer-inspect")]
        public static Task<object> MixerInspect(JToken args)
        {
            var assetPath  = args?.Value<string>("asset_path");
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var mixer = ResolveMixer(assetPath, instanceId);

                // AudioMixer exposes FindMatchingGroups(path) — pass empty
                // string to get every group.
                var groups = mixer.FindMatchingGroups(string.Empty);
                var groupList = new List<object>(groups.Length);
                foreach (var g in groups)
                    groupList.Add(new
                    {
                        name        = g.name,
                        instance_id = GameObjectResolver.InstanceIdOf(g)
                    });

                // Exposed parameter names — not on the public API, read via
                // the internal SerializedObject property 'm_ExposedParameters'.
                var exposed = new List<object>();
                try
                {
                    using var so = new SerializedObject(mixer);
                    var arr = so.FindProperty("m_ExposedParameters");
                    if (arr != null && arr.isArray)
                    {
                        for (var i = 0; i < arr.arraySize; i++)
                        {
                            var element = arr.GetArrayElementAtIndex(i);
                            var nameProp = element.FindPropertyRelative("name");
                            var name = nameProp != null ? nameProp.stringValue : null;
                            if (string.IsNullOrEmpty(name)) continue;
                            float current = 0f;
                            var hasVal = mixer.GetFloat(name, out current);
                            exposed.Add(new
                            {
                                name          = name,
                                current_value = hasVal ? (float?)current : null,
                                readable      = hasVal
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Internal layout mismatch across Unity versions — log
                    // but don't fail the whole tool.
                    exposed.Add(new { name = "<error>", reason = ex.Message });
                }

                return new
                {
                    instance_id    = GameObjectResolver.InstanceIdOf(mixer),
                    asset_path     = AssetDatabase.GetAssetPath(mixer),
                    name           = mixer.name,
                    output_group   = mixer.outputAudioMixerGroup != null ? new
                    {
                        name        = mixer.outputAudioMixerGroup.name,
                        instance_id = GameObjectResolver.InstanceIdOf(mixer.outputAudioMixerGroup)
                    } : null,
                    update_mode    = mixer.updateMode.ToString(),
                    group_count    = groupList.Count,
                    groups         = groupList.ToArray(),
                    exposed_count  = exposed.Count,
                    exposed_parameters = exposed.ToArray(),
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        // ---------- audio-mixer-set-exposed ----------
        [ReifyTool("audio-mixer-set-exposed")]
        public static Task<object> MixerSetExposed(JToken args)
        {
            var assetPath  = args?.Value<string>("asset_path");
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var paramName  = args?.Value<string>("parameter_name")
                ?? throw new ArgumentException("parameter_name is required.");
            var valueToken = args?["value"];
            var clear      = args?.Value<bool?>("clear") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var mixer = ResolveMixer(assetPath, instanceId);

                float before = 0f;
                var hadBefore = mixer.GetFloat(paramName, out before);

                if (clear)
                {
                    if (!mixer.ClearFloat(paramName))
                        throw new InvalidOperationException(
                            $"ClearFloat refused for '{paramName}'. Parameter may not be exposed on this mixer.");
                }
                else
                {
                    if (valueToken == null || valueToken.Type == JTokenType.Null)
                        throw new ArgumentException("value is required when clear=false.");
                    var value = valueToken.Value<float>();
                    if (!mixer.SetFloat(paramName, value))
                        throw new InvalidOperationException(
                            $"SetFloat refused for '{paramName}' — not exposed on mixer '{mixer.name}'. " +
                            "Expose it via the Mixer window or check spelling.");
                }

                float after = 0f;
                var hasAfter = mixer.GetFloat(paramName, out after);

                return new
                {
                    asset_path     = AssetDatabase.GetAssetPath(mixer),
                    mixer_name     = mixer.name,
                    parameter_name = paramName,
                    cleared        = clear,
                    before         = hadBefore ? (float?)before : null,
                    after          = hasAfter  ? (float?)after  : null,
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        // ---------- audio-clip-inspect ----------
        [ReifyTool("audio-clip-inspect")]
        public static Task<object> ClipInspect(JToken args)
        {
            var assetPath = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath)
                    ?? throw new InvalidOperationException($"AudioClip not found at asset_path: {assetPath}");

                var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
                var warnings = new List<string>();

                if (clip.length <= 0f)
                    warnings.Add("AudioClip length is 0 — file may be corrupt or still importing.");
                if (clip.loadType == AudioClipLoadType.DecompressOnLoad && clip.length > 10f)
                    warnings.Add($"DecompressOnLoad on a {clip.length:F1}s clip — memory-heavy. Consider CompressedInMemory or Streaming.");
                if (clip.loadType == AudioClipLoadType.Streaming && clip.length < 2f)
                    warnings.Add($"Streaming on a short {clip.length:F2}s clip — streaming overhead exceeds the benefit. Use DecompressOnLoad.");

                object importerInfo = null;
                if (importer != null)
                {
                    var defaultSample = importer.defaultSampleSettings;
                    importerInfo = new
                    {
                        importer_type              = importer.GetType().FullName,
                        force_to_mono              = importer.forceToMono,
                        load_in_background         = importer.loadInBackground,
                        preload_audio_data         = importer.preloadAudioData,
                        ambisonic                  = importer.ambisonic,
                        default_sample_settings    = new
                        {
                            load_type            = defaultSample.loadType.ToString(),
                            compression_format   = defaultSample.compressionFormat.ToString(),
                            sample_rate_setting  = defaultSample.sampleRateSetting.ToString(),
                            sample_rate_override = defaultSample.sampleRateOverride,
                            quality              = defaultSample.quality,
                            preload_audio_data   = defaultSample.preloadAudioData
                        }
                    };
                }

                return new
                {
                    asset_path   = assetPath,
                    instance_id  = GameObjectResolver.InstanceIdOf(clip),
                    name         = clip.name,
                    length       = clip.length,
                    channels     = clip.channels,
                    frequency    = clip.frequency,
                    samples      = clip.samples,
                    load_type    = clip.loadType.ToString(),
                    load_state   = clip.loadState.ToString(),
                    preload_audio_data = clip.preloadAudioData,
                    ambisonic    = clip.ambisonic,
                    importer     = importerInfo,
                    warnings     = warnings.ToArray(),
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        // ---------- resolvers ----------
        private static AudioSource ResolveSource(JToken args)
        {
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var goPath     = args?.Value<string>("gameobject_path");

            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                return obj as AudioSource
                    ?? (obj as GameObject)?.GetComponent<AudioSource>()
                    ?? throw new InvalidOperationException(
                        $"instance_id {instanceId} does not resolve to an AudioSource or a GameObject with one.");
            }
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("Provide either instance_id or gameobject_path.");
            var go = GameObjectResolver.ByPath(goPath)
                ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
            return go.GetComponent<AudioSource>()
                ?? throw new InvalidOperationException($"GameObject '{goPath}' has no AudioSource component.");
        }

        private static AudioMixer ResolveMixer(string assetPath, int? instanceId)
        {
            if (!string.IsNullOrEmpty(assetPath))
            {
                var m = AssetDatabase.LoadAssetAtPath<AudioMixer>(assetPath)
                    ?? throw new InvalidOperationException($"AudioMixer asset not found: {assetPath}");
                return m;
            }
            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value);
                if (obj is AudioMixer m) return m;
                throw new InvalidOperationException(
                    $"instance_id {instanceId} does not resolve to an AudioMixer.");
            }
            throw new ArgumentException("Provide either asset_path or instance_id.");
        }

        private static object V(Vector3 v) => new { x = v.x, y = v.y, z = v.z };
    }
}
