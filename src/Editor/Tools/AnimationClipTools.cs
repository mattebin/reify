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
    /// AnimationClip read + authoring basics: inspect, list curves,
    /// set a single curve. Advanced curve editing (tangents, weighted
    /// modes, events) is deferred — the SerializedProperty path via
    /// component-set-property covers the granular cases.
    /// </summary>
    internal static class AnimationClipTools
    {
        // ---------- animation-clip-inspect ----------
        [ReifyTool("animation-clip-inspect")]
        public static Task<object> Inspect(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path)
                    ?? throw new InvalidOperationException($"No AnimationClip at path: {path}");
                var warnings = new List<string>();

                var bindings = AnimationUtility.GetCurveBindings(clip);
                var objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);

                if (clip.length <= 0f) warnings.Add($"Clip length is {clip.length:F3} — effectively empty.");
                if (bindings.Length == 0 && objBindings.Length == 0)
                    warnings.Add("Clip has no animation curves — will produce no output.");
                if (clip.isLooping && clip.length < 0.1f)
                    warnings.Add("Clip is looping but extremely short — may cause jitter or runaway updates.");

                var events = AnimationUtility.GetAnimationEvents(clip);

                return new
                {
                    asset_path         = path,
                    instance_id        = GameObjectResolver.InstanceIdOf(clip),
                    name               = clip.name,
                    length             = clip.length,
                    frame_rate         = clip.frameRate,
                    is_looping         = clip.isLooping,
                    legacy             = clip.legacy,
                    is_human_motion    = clip.isHumanMotion,
                    local_bounds = new
                    {
                        center = new { x = clip.localBounds.center.x, y = clip.localBounds.center.y, z = clip.localBounds.center.z },
                        size   = new { x = clip.localBounds.size.x,   y = clip.localBounds.size.y,   z = clip.localBounds.size.z   }
                    },
                    wrap_mode          = clip.wrapMode.ToString(),
                    curve_count        = bindings.Length,
                    object_ref_curve_count = objBindings.Length,
                    event_count        = events != null ? events.Length : 0,
                    warnings           = warnings.ToArray(),
                    read_at_utc        = DateTime.UtcNow.ToString("o"),
                    frame              = (long)Time.frameCount
                };
            });
        }

        // ---------- animation-clip-list-curves ----------
        [ReifyTool("animation-clip-list-curves")]
        public static Task<object> ListCurves(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");
            var limit = args?.Value<int?>("limit") ?? 500;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path)
                    ?? throw new InvalidOperationException($"No AnimationClip at path: {path}");

                var bindings = AnimationUtility.GetCurveBindings(clip);
                var truncated = bindings.Length > limit;
                var n = Math.Min(bindings.Length, limit);

                var curves = new List<object>(n);
                for (var i = 0; i < n; i++)
                {
                    var b = bindings[i];
                    var curve = AnimationUtility.GetEditorCurve(clip, b);
                    curves.Add(new
                    {
                        path           = b.path,
                        type_fqn       = b.type != null ? b.type.FullName : null,
                        property_name  = b.propertyName,
                        is_discrete    = b.isDiscreteCurve,
                        is_ppt         = b.isPPtrCurve,
                        key_count      = curve != null ? curve.length : 0,
                        duration       = curve != null && curve.length > 0 ? curve[curve.length - 1].time : 0f,
                        first_value    = curve != null && curve.length > 0 ? (float?)curve[0].value : null,
                        last_value     = curve != null && curve.length > 0 ? (float?)curve[curve.length - 1].value : null
                    });
                }

                return new
                {
                    asset_path   = path,
                    curve_count  = bindings.Length,
                    returned     = n,
                    truncated,
                    curves       = curves.ToArray(),
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        // ---------- animation-clip-set-curve ----------
        [ReifyTool("animation-clip-set-curve")]
        public static Task<object> SetCurve(JToken args)
        {
            var path         = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");
            var relativePath = args?.Value<string>("relative_path") ?? "";
            var typeName     = args?.Value<string>("type_name")
                ?? throw new ArgumentException("type_name is required (e.g. 'UnityEngine.Transform').");
            var propertyName = args?.Value<string>("property_name")
                ?? throw new ArgumentException("property_name is required (e.g. 'localPosition.x').");
            var keyframes    = args?["keyframes"] as JArray
                ?? throw new ArgumentException("keyframes[] array is required, each {time, value}.");

            if (keyframes.Count == 0)
                throw new ArgumentException("keyframes[] cannot be empty.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path)
                    ?? throw new InvalidOperationException($"No AnimationClip at path: {path}");

                var componentType = ResolveType(typeName)
                    ?? throw new InvalidOperationException($"Type '{typeName}' not found.");

                var keys = new Keyframe[keyframes.Count];
                for (var i = 0; i < keyframes.Count; i++)
                {
                    var k = keyframes[i];
                    keys[i] = new Keyframe(
                        k.Value<float?>("time")  ?? 0f,
                        k.Value<float?>("value") ?? 0f);
                }
                var curve = new AnimationCurve(keys);

                Undo.RecordObject(clip, "Reify: set animation curve");
                clip.SetCurve(relativePath, componentType, propertyName, curve);
                EditorUtility.SetDirty(clip);

                return new
                {
                    asset_path     = path,
                    relative_path  = relativePath,
                    type_name      = componentType.FullName,
                    property_name  = propertyName,
                    key_count      = keys.Length,
                    first_time     = keys[0].time,
                    last_time      = keys[keys.Length - 1].time,
                    new_curve_count = AnimationUtility.GetCurveBindings(clip).Length,
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        private static Type ResolveType(string typeName)
        {
            var t = Type.GetType(typeName);
            if (t != null) return t;
            foreach (var qualified in new[]
            {
                typeName + ", UnityEngine",
                typeName + ", UnityEngine.CoreModule",
                typeName + ", UnityEngine.AnimationModule",
                typeName + ", Assembly-CSharp"
            })
            {
                t = Type.GetType(qualified);
                if (t != null) return t;
            }
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(typeName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { /* ignore */ }
            }
            return null;
        }
    }
}
