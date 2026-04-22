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
    /// AnimationEvent[] read / replace on an AnimationClip. Complements
    /// <see cref="AnimationClipTools"/> which already reports event_count
    /// but doesn't expose the events themselves.
    /// </summary>
    internal static class AnimationEventTools
    {
        // ---------- animation-clip-events-read ----------
        [ReifyTool("animation-clip-events-read")]
        public static Task<object> EventsRead(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path)
                    ?? throw new InvalidOperationException($"No AnimationClip at path: {path}");

                var events = AnimationUtility.GetAnimationEvents(clip) ?? Array.Empty<AnimationEvent>();
                var dtos = new object[events.Length];
                for (var i = 0; i < events.Length; i++)
                {
                    var e = events[i];
                    dtos[i] = new
                    {
                        index              = i,
                        time               = e.time,
                        function_name      = e.functionName,
                        string_parameter   = e.stringParameter,
                        float_parameter    = e.floatParameter,
                        int_parameter      = e.intParameter,
                        object_parameter   = e.objectReferenceParameter != null ? new
                        {
                            name        = e.objectReferenceParameter.name,
                            type_fqn    = e.objectReferenceParameter.GetType().FullName,
                            asset_path  = AssetDatabase.GetAssetPath(e.objectReferenceParameter),
                            instance_id = GameObjectResolver.InstanceIdOf(e.objectReferenceParameter)
                        } : null,
                        send_message_options = e.messageOptions.ToString()
                    };
                }

                return new
                {
                    asset_path  = path,
                    event_count = events.Length,
                    events      = dtos,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- animation-clip-events-set ----------
        [ReifyTool("animation-clip-events-set")]
        public static Task<object> EventsSet(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");
            var eventsArr = args?["events"] as JArray
                ?? throw new ArgumentException("events[] array is required. Pass [] to clear all events.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path)
                    ?? throw new InvalidOperationException($"No AnimationClip at path: {path}");

                var newEvents = new AnimationEvent[eventsArr.Count];
                for (var i = 0; i < eventsArr.Count; i++)
                {
                    var src = eventsArr[i];
                    var funcName = src.Value<string>("function_name");
                    if (string.IsNullOrEmpty(funcName))
                        throw new ArgumentException($"events[{i}].function_name is required.");

                    var e = new AnimationEvent
                    {
                        time            = src.Value<float?>("time") ?? 0f,
                        functionName    = funcName,
                        stringParameter = src.Value<string>("string_parameter") ?? string.Empty,
                        floatParameter  = src.Value<float?>("float_parameter") ?? 0f,
                        intParameter    = src.Value<int?>("int_parameter") ?? 0
                    };

                    var sendOpts = src.Value<string>("send_message_options");
                    if (!string.IsNullOrEmpty(sendOpts))
                    {
                        if (!Enum.TryParse<SendMessageOptions>(sendOpts, true, out var opts))
                            throw new ArgumentException(
                                $"events[{i}].send_message_options '{sendOpts}' must be RequireReceiver or DontRequireReceiver.");
                        e.messageOptions = opts;
                    }

                    var objRef = src["object_parameter"];
                    if (objRef != null && objRef.Type != JTokenType.Null)
                    {
                        var objAssetPath = objRef.Value<string>("asset_path");
                        if (!string.IsNullOrEmpty(objAssetPath))
                        {
                            e.objectReferenceParameter = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(objAssetPath)
                                ?? throw new InvalidOperationException(
                                    $"events[{i}].object_parameter.asset_path not found: {objAssetPath}");
                        }
                    }

                    newEvents[i] = e;
                }

                Undo.RecordObject(clip, "Reify: set AnimationEvents");
                AnimationUtility.SetAnimationEvents(clip, newEvents);
                EditorUtility.SetDirty(clip);

                return new
                {
                    asset_path    = path,
                    event_count   = newEvents.Length,
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }
    }
}
