using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Timeline surface via reflection — package-gated on
    /// `com.unity.timeline`. Covers TimelineAsset inspection + the
    /// runtime PlayableDirector state/mutation.
    /// </summary>
    internal static class TimelineTools
    {
        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName, false); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        // ---------- timeline-asset-inspect ----------
        [ReifyTool("timeline-asset-inspect")]
        public static Task<object> AssetInspect(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required (a .playable file).");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var timelineT = FindType("UnityEngine.Timeline.TimelineAsset")
                    ?? throw new InvalidOperationException(
                        "UnityEngine.Timeline.TimelineAsset not found — install `com.unity.timeline`.");

                var asset = AssetDatabase.LoadAssetAtPath(path, timelineT) as UnityEngine.Object
                    ?? throw new InvalidOperationException($"No TimelineAsset at '{path}'.");
                var t = asset.GetType();

                var duration     = (double?)t.GetProperty("duration")?.GetValue(asset);
                var fixedLength  = (double?)t.GetProperty("fixedDuration")?.GetValue(asset);
                var frameRate    = (double?)t.GetProperty("editorSettings")?.GetValue(asset) is object es
                                   ? (double?)es.GetType().GetProperty("frameRate")?.GetValue(es) : null;

                var tracks = (IEnumerable)t.GetMethod("GetRootTracks")?.Invoke(asset, null);
                var trackList = new List<object>();
                if (tracks != null)
                {
                    foreach (var tr in tracks)
                    {
                        if (tr == null) continue;
                        var trT = tr.GetType();
                        var clips = (ICollection)trT.GetMethod("GetClips")?.Invoke(tr, null);
                        trackList.Add(new
                        {
                            name         = trT.GetProperty("name")?.GetValue(tr) as string,
                            type_fqn     = trT.FullName,
                            muted        = (bool?)(trT.GetProperty("muted")?.GetValue(tr)),
                            clip_count   = clips?.Count ?? 0
                        });
                    }
                }

                return new
                {
                    asset_path    = path,
                    guid          = AssetDatabase.AssetPathToGUID(path),
                    duration_sec  = duration,
                    fixed_duration_sec = fixedLength,
                    frame_rate    = frameRate,
                    track_count   = trackList.Count,
                    tracks        = trackList.ToArray(),
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }

        // ---------- timeline-director-inspect ----------
        [ReifyTool("timeline-director-inspect")]
        public static Task<object> DirectorInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var dirT = FindType("UnityEngine.Playables.PlayableDirector")
                    ?? throw new InvalidOperationException("PlayableDirector not found.");
                var go = ResolveGameObject(args);
                var dir = go.GetComponent(dirT);
                if (dir == null)
                    throw new InvalidOperationException(
                        $"GameObject '{GameObjectResolver.PathOf(go)}' has no PlayableDirector.");
                var t = dir.GetType();

                // Each reflection access wrapped to unwrap TargetInvocationException
                // so the error message actually tells us which property failed.
                var state     = Safe(() => t.GetProperty("state")?.GetValue(dir)?.ToString());
                var asset     = Safe(() => t.GetProperty("playableAsset")?.GetValue(dir) as UnityEngine.Object);
                var time      = Safe(() => (double?)t.GetProperty("time")?.GetValue(dir));
                var duration  = Safe(() => (double?)t.GetProperty("duration")?.GetValue(dir));
                var extrapol  = Safe(() => t.GetProperty("extrapolationMode")?.GetValue(dir)?.ToString());
                var wrapMode  = Safe(() => t.GetProperty("playOnAwake")?.GetValue(dir));

                return new
                {
                    instance_id        = GameObjectResolver.InstanceIdOf(dir as UnityEngine.Object),
                    gameobject_path    = GameObjectResolver.PathOf(go),
                    state              = state,
                    asset_path         = asset != null ? AssetDatabase.GetAssetPath(asset) : null,
                    asset_guid         = asset != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)) : null,
                    time_seconds       = time,
                    duration_seconds   = duration,
                    extrapolation_mode = extrapol,
                    play_on_awake      = wrapMode,
                    read_at_utc        = DateTime.UtcNow.ToString("o"),
                    frame              = (long)Time.frameCount
                };
            });
        }

        // ---------- timeline-director-play ----------
        [ReifyTool("timeline-director-play")]
        public static Task<object> DirectorPlay(JToken args)
        {
            return DirectorMethodCall(args, "Play", "state", captureTime: true);
        }

        // ---------- timeline-director-pause ----------
        [ReifyTool("timeline-director-pause")]
        public static Task<object> DirectorPause(JToken args)
        {
            return DirectorMethodCall(args, "Pause", "state", captureTime: true);
        }

        // ---------- timeline-director-stop ----------
        [ReifyTool("timeline-director-stop")]
        public static Task<object> DirectorStop(JToken args)
        {
            return DirectorMethodCall(args, "Stop", "state", captureTime: true);
        }

        // ---------- timeline-director-set-time ----------
        [ReifyTool("timeline-director-set-time")]
        public static Task<object> DirectorSetTime(JToken args)
        {
            var newTime = args?.Value<double?>("time_seconds")
                ?? throw new ArgumentException("time_seconds (double) is required.");
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var dirT = FindType("UnityEngine.Playables.PlayableDirector")
                    ?? throw new InvalidOperationException("PlayableDirector not found.");
                var go = ResolveGameObject(args);
                var dir = go.GetComponent(dirT)
                    ?? throw new InvalidOperationException("No PlayableDirector on GameObject.");
                var t = dir.GetType();
                var timeProp = t.GetProperty("time");
                var before = (double?)timeProp?.GetValue(dir);
                timeProp?.SetValue(dir, newTime);
                var after = (double?)timeProp?.GetValue(dir);
                return new
                {
                    instance_id    = GameObjectResolver.InstanceIdOf(dir as UnityEngine.Object),
                    gameobject_path = GameObjectResolver.PathOf(go),
                    applied_fields = new object[]
                    {
                        new { field = "time_seconds", before = before, after = after }
                    },
                    applied_count = 1,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static T Safe<T>(Func<T> f)
        {
            try { return f(); }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                // Swallow — these are typically "unbound director" / "no asset"
                // errors from accessing properties before the director is set up.
                return default;
            }
            catch { return default; }
        }

        private static Task<object> DirectorMethodCall(JToken args, string methodName, string stateProp, bool captureTime)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var dirT = FindType("UnityEngine.Playables.PlayableDirector")
                    ?? throw new InvalidOperationException("PlayableDirector not found.");
                var go = ResolveGameObject(args);
                var dir = go.GetComponent(dirT)
                    ?? throw new InvalidOperationException("No PlayableDirector on GameObject.");
                var t = dir.GetType();

                var beforeState = t.GetProperty(stateProp)?.GetValue(dir)?.ToString();
                var beforeTime  = captureTime ? (double?)t.GetProperty("time")?.GetValue(dir) : null;

                var m = t.GetMethod(methodName, Type.EmptyTypes);
                if (m == null)
                    throw new InvalidOperationException($"No {methodName}() method on PlayableDirector.");
                m.Invoke(dir, null);

                var afterState = t.GetProperty(stateProp)?.GetValue(dir)?.ToString();
                var afterTime  = captureTime ? (double?)t.GetProperty("time")?.GetValue(dir) : null;

                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(dir as UnityEngine.Object),
                    gameobject_path = GameObjectResolver.PathOf(go),
                    method          = methodName,
                    applied_fields  = new object[]
                    {
                        new { field = "state",        before = beforeState, after = afterState },
                        new { field = "time_seconds", before = beforeTime,  after = afterTime }
                    },
                    applied_count   = 2,
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        private static GameObject ResolveGameObject(JToken args)
        {
            var id = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var path = args?.Value<string>("gameobject_path") ?? args?.Value<string>("path");
            if (id.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(id.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {id}.");
                if (obj is GameObject g) return g;
                if (obj is Component c) return c.gameObject;
                throw new InvalidOperationException($"instance_id {id} is not a GameObject/Component.");
            }
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Provide instance_id or gameobject_path.");
            return GameObjectResolver.ByPath(path)
                ?? throw new InvalidOperationException($"GameObject not found: {path}");
        }
    }
}
