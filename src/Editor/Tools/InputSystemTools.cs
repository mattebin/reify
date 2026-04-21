using System;
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
    /// Input System (new) surface via reflection so reify stays free of a
    /// com.unity.inputsystem package dependency. When the package isn't
    /// installed the tools emit a structured error rather than failing to
    /// compile.
    /// </summary>
    internal static class InputSystemTools
    {
        // ---------- input-actions-asset-inspect ----------
        [ReifyTool("input-actions-asset-inspect")]
        public static Task<object> InputActionsAssetInspect(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var iaaType = Type.GetType("UnityEngine.InputSystem.InputActionAsset, Unity.InputSystem");
                if (iaaType == null)
                    throw new InvalidOperationException(
                        "Unity.InputSystem not loaded — install com.unity.inputsystem to use this tool.");

                var asset = AssetDatabase.LoadAssetAtPath(path, iaaType);
                if (asset == null)
                    throw new InvalidOperationException($"No InputActionAsset at path: {path}");

                // asset.actionMaps (IReadOnlyList<InputActionMap>)
                var actionMapsProp = iaaType.GetProperty("actionMaps", BindingFlags.Instance | BindingFlags.Public);
                var actionMaps     = actionMapsProp?.GetValue(asset) as System.Collections.IEnumerable;
                var controlSchemesProp = iaaType.GetProperty("controlSchemes", BindingFlags.Instance | BindingFlags.Public);
                var controlSchemes     = controlSchemesProp?.GetValue(asset) as System.Collections.IEnumerable;

                var mapDtos = new List<object>();
                var mapCount = 0;
                var totalActions = 0;
                var totalBindings = 0;
                if (actionMaps != null)
                {
                    foreach (var map in actionMaps)
                    {
                        mapCount++;
                        var mapName = (string)map.GetType().GetProperty("name")?.GetValue(map);
                        var actionsProp = map.GetType().GetProperty("actions");
                        var bindingsProp = map.GetType().GetProperty("bindings");
                        var actions      = actionsProp?.GetValue(map) as System.Collections.IEnumerable;
                        var bindings     = bindingsProp?.GetValue(map) as System.Collections.IEnumerable;

                        var actionList = new List<object>();
                        var aCount = 0;
                        if (actions != null)
                        {
                            foreach (var a in actions)
                            {
                                aCount++;
                                var at = a.GetType();
                                actionList.Add(new
                                {
                                    name         = (string)at.GetProperty("name")?.GetValue(a),
                                    action_type  = at.GetProperty("type")?.GetValue(a)?.ToString(),
                                    expected_control_type = (string)at.GetProperty("expectedControlType")?.GetValue(a),
                                    id           = at.GetProperty("id")?.GetValue(a)?.ToString(),
                                    interactions = (string)at.GetProperty("interactions")?.GetValue(a),
                                    processors   = (string)at.GetProperty("processors")?.GetValue(a)
                                });
                            }
                        }

                        var bCount = 0;
                        if (bindings != null) foreach (var _ in bindings) bCount++;

                        totalActions += aCount;
                        totalBindings += bCount;

                        mapDtos.Add(new
                        {
                            name          = mapName,
                            action_count  = aCount,
                            binding_count = bCount,
                            actions       = actionList.ToArray()
                        });
                    }
                }

                var schemeList = new List<object>();
                if (controlSchemes != null)
                {
                    foreach (var cs in controlSchemes)
                    {
                        var csT = cs.GetType();
                        schemeList.Add(new
                        {
                            name           = (string)csT.GetProperty("name")?.GetValue(cs),
                            binding_group  = (string)csT.GetProperty("bindingGroup")?.GetValue(cs)
                        });
                    }
                }

                return new
                {
                    asset_path          = path,
                    action_map_count    = mapCount,
                    total_action_count  = totalActions,
                    total_binding_count = totalBindings,
                    action_maps         = mapDtos.ToArray(),
                    control_schemes     = schemeList.ToArray(),
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }

        // ---------- input-player-input-inspect ----------
        [ReifyTool("input-player-input-inspect")]
        public static Task<object> PlayerInputInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var piType = Type.GetType("UnityEngine.InputSystem.PlayerInput, Unity.InputSystem");
                if (piType == null)
                    throw new InvalidOperationException(
                        "Unity.InputSystem not loaded — install com.unity.inputsystem to use this tool.");

                var go = ResolveGameObject(args);
                var pi = go.GetComponent(piType)
                    ?? throw new InvalidOperationException(
                        $"GameObject '{GameObjectResolver.PathOf(go)}' has no PlayerInput component.");

                var t = piType;
                var actions = t.GetProperty("actions")?.GetValue(pi);
                var assetPath = actions != null ? AssetDatabase.GetAssetPath(actions as UnityEngine.Object) : null;

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(pi as UnityEngine.Object),
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(go),
                    gameobject_path        = GameObjectResolver.PathOf(go),
                    current_action_map     = (string)t.GetProperty("currentActionMap")?.GetValue(pi)?.GetType().GetProperty("name")?.GetValue(t.GetProperty("currentActionMap")?.GetValue(pi)),
                    default_action_map     = (string)t.GetProperty("defaultActionMap")?.GetValue(pi),
                    notification_behavior  = t.GetProperty("notificationBehavior")?.GetValue(pi)?.ToString(),
                    player_index           = (int?)t.GetProperty("playerIndex")?.GetValue(pi),
                    split_screen_index     = (int?)t.GetProperty("splitScreenIndex")?.GetValue(pi),
                    input_is_active        = (bool?)t.GetProperty("inputIsActive")?.GetValue(pi),
                    actions_asset_path     = assetPath,
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- input-devices ----------
        [ReifyTool("input-devices")]
        public static Task<object> Devices(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var inputSystemType = Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem");
                if (inputSystemType == null)
                    throw new InvalidOperationException(
                        "Unity.InputSystem not loaded — install com.unity.inputsystem to use this tool.");

                var devicesProp = inputSystemType.GetProperty("devices", BindingFlags.Static | BindingFlags.Public);
                var devices = devicesProp?.GetValue(null) as System.Collections.IEnumerable;

                var list = new List<object>();
                var count = 0;
                if (devices != null)
                {
                    foreach (var d in devices)
                    {
                        count++;
                        var dt = d.GetType();
                        list.Add(new
                        {
                            type_fqn          = dt.FullName,
                            name              = (string)dt.GetProperty("name")?.GetValue(d),
                            display_name      = (string)dt.GetProperty("displayName")?.GetValue(d),
                            short_display_name = (string)dt.GetProperty("shortDisplayName")?.GetValue(d),
                            device_id         = (int?)dt.GetProperty("deviceId")?.GetValue(d),
                            enabled           = (bool?)dt.GetProperty("enabled")?.GetValue(d),
                            native            = (bool?)dt.GetProperty("native")?.GetValue(d)
                        });
                    }
                }

                return new
                {
                    device_count = count,
                    devices      = list.ToArray(),
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        private static GameObject ResolveGameObject(JToken args)
        {
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var goPath     = args?.Value<string>("gameobject_path");

            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                if (obj is GameObject go) return go;
                if (obj is Component c)   return c.gameObject;
                throw new InvalidOperationException(
                    $"instance_id {instanceId} is neither a GameObject nor a Component.");
            }
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("Provide either instance_id or gameobject_path.");
            return GameObjectResolver.ByPath(goPath)
                ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
        }
    }
}
