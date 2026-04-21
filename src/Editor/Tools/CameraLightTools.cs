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
    /// Camera + Light authoring. Lighting-diagnostic handles scene-wide
    /// reads; these tools cover single-object inspect/create/modify + the
    /// camera projection query helpers an LLM needs to reason about
    /// screen-space UI and world-space picking without a screenshot.
    /// </summary>
    internal static class CameraLightTools
    {
        // ---------- camera-inspect ----------
        [ReifyTool("camera-inspect")]
        public static Task<object> CameraInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var cam = ResolveCamera(args);
                var warnings = new List<string>();

                if (cam.cullingMask == 0)
                    warnings.Add("cullingMask is 0 — camera renders nothing. Set which layers to render.");
                if (cam.orthographic && cam.orthographicSize <= 0f)
                    warnings.Add($"Orthographic camera has orthographicSize {cam.orthographicSize:F3} — renders empty view.");
                if (!cam.orthographic && cam.fieldOfView <= 0.01f)
                    warnings.Add($"Perspective camera fieldOfView {cam.fieldOfView:F3} — effectively null.");
                if (cam.nearClipPlane >= cam.farClipPlane)
                    warnings.Add($"nearClipPlane ({cam.nearClipPlane}) >= farClipPlane ({cam.farClipPlane}) — nothing renders.");
                if (cam.nearClipPlane < 0.01f)
                    warnings.Add($"nearClipPlane {cam.nearClipPlane:F4} is very small — can cause depth-precision (z-fighting) artifacts.");
                if (cam == Camera.main && cam.gameObject.tag != "MainCamera")
                    warnings.Add("Camera reports as Camera.main but its GameObject tag is not 'MainCamera'. Unusual.");
                if (!cam.enabled)
                    warnings.Add("Camera component is disabled — won't render.");

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(cam),
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(cam.gameObject),
                    gameobject_path        = GameObjectResolver.PathOf(cam.gameObject),
                    enabled                = cam.enabled,
                    tag                    = cam.gameObject.tag,
                    is_main                = cam == Camera.main,
                    orthographic           = cam.orthographic,
                    orthographic_size      = cam.orthographicSize,
                    field_of_view          = cam.fieldOfView,
                    near_clip_plane        = cam.nearClipPlane,
                    far_clip_plane         = cam.farClipPlane,
                    depth                  = cam.depth,
                    culling_mask           = cam.cullingMask,
                    clear_flags            = cam.clearFlags.ToString(),
                    background_color       = new { r = cam.backgroundColor.r, g = cam.backgroundColor.g, b = cam.backgroundColor.b, a = cam.backgroundColor.a },
                    rect                   = new { x = cam.rect.x, y = cam.rect.y, w = cam.rect.width, h = cam.rect.height },
                    pixel_rect             = new { x = cam.pixelRect.x, y = cam.pixelRect.y, w = cam.pixelRect.width, h = cam.pixelRect.height },
                    pixel_width            = cam.pixelWidth,
                    pixel_height           = cam.pixelHeight,
                    aspect                 = cam.aspect,
                    world_position         = V3(cam.transform.position),
                    world_rotation_euler   = V3(cam.transform.eulerAngles),
                    world_forward          = V3(cam.transform.forward),
                    hdr                    = cam.allowHDR,
                    msaa                   = cam.allowMSAA,
                    dynamic_resolution     = cam.allowDynamicResolution,
                    target_texture         = cam.targetTexture != null ? new {
                        name        = cam.targetTexture.name,
                        asset_path  = AssetDatabase.GetAssetPath(cam.targetTexture),
                        width       = cam.targetTexture.width,
                        height      = cam.targetTexture.height,
                        format      = cam.targetTexture.format.ToString()
                    } : null,
                    target_display         = cam.targetDisplay,
                    use_occlusion_culling  = cam.useOcclusionCulling,
                    rendering_path         = cam.renderingPath.ToString(),
                    warnings               = warnings.ToArray(),
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- camera-set ----------
        [ReifyTool("camera-set")]
        public static Task<object> CameraSet(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var cam = ResolveCamera(args);
                Undo.RecordObject(cam, "Reify: modify Camera");

                var before = new {
                    field_of_view    = cam.fieldOfView,
                    near_clip_plane  = cam.nearClipPlane,
                    far_clip_plane   = cam.farClipPlane,
                    orthographic     = cam.orthographic,
                    orthographic_size= cam.orthographicSize,
                    culling_mask     = cam.cullingMask,
                    depth            = cam.depth,
                    clear_flags      = cam.clearFlags.ToString(),
                    background_color = new { r = cam.backgroundColor.r, g = cam.backgroundColor.g, b = cam.backgroundColor.b, a = cam.backgroundColor.a },
                    hdr              = cam.allowHDR,
                    target_display   = cam.targetDisplay
                };

                var applied = new List<string>();

                var fov = args?.Value<float?>("field_of_view");
                if (fov.HasValue)            { cam.fieldOfView = fov.Value; applied.Add("field_of_view"); }
                var nearC = args?.Value<float?>("near_clip_plane");
                if (nearC.HasValue)          { cam.nearClipPlane = nearC.Value; applied.Add("near_clip_plane"); }
                var farC = args?.Value<float?>("far_clip_plane");
                if (farC.HasValue)           { cam.farClipPlane = farC.Value; applied.Add("far_clip_plane"); }
                var ortho = args?.Value<bool?>("orthographic");
                if (ortho.HasValue)          { cam.orthographic = ortho.Value; applied.Add("orthographic"); }
                var orthoSize = args?.Value<float?>("orthographic_size");
                if (orthoSize.HasValue)      { cam.orthographicSize = orthoSize.Value; applied.Add("orthographic_size"); }
                var cullingMask = args?.Value<int?>("culling_mask");
                if (cullingMask.HasValue)    { cam.cullingMask = cullingMask.Value; applied.Add("culling_mask"); }
                var depth = args?.Value<float?>("depth");
                if (depth.HasValue)          { cam.depth = depth.Value; applied.Add("depth"); }
                var clearFlags = args?.Value<string>("clear_flags");
                if (!string.IsNullOrEmpty(clearFlags))
                {
                    if (!Enum.TryParse<CameraClearFlags>(clearFlags, true, out var cf))
                        throw new ArgumentException($"clear_flags '{clearFlags}' must be one of: Skybox, SolidColor, Depth, Nothing.");
                    cam.clearFlags = cf; applied.Add("clear_flags");
                }
                var bg = args?["background_color"];
                if (bg != null && bg.Type != JTokenType.Null)
                {
                    cam.backgroundColor = new Color(
                        bg.Value<float?>("r") ?? 0, bg.Value<float?>("g") ?? 0,
                        bg.Value<float?>("b") ?? 0, bg.Value<float?>("a") ?? 1);
                    applied.Add("background_color");
                }
                var hdr = args?.Value<bool?>("hdr");
                if (hdr.HasValue)            { cam.allowHDR = hdr.Value; applied.Add("hdr"); }
                var targetDisplay = args?.Value<int?>("target_display");
                if (targetDisplay.HasValue)  { cam.targetDisplay = targetDisplay.Value; applied.Add("target_display"); }

                if (applied.Count == 0)
                    throw new ArgumentException(
                        "No writable fields provided. Expected at least one of: field_of_view, near_clip_plane, far_clip_plane, orthographic, orthographic_size, culling_mask, depth, clear_flags, background_color, hdr, target_display.");

                EditorUtility.SetDirty(cam);

                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(cam),
                    gameobject_path = GameObjectResolver.PathOf(cam.gameObject),
                    applied,
                    before,
                    after = new {
                        field_of_view    = cam.fieldOfView,
                        near_clip_plane  = cam.nearClipPlane,
                        far_clip_plane   = cam.farClipPlane,
                        orthographic     = cam.orthographic,
                        orthographic_size= cam.orthographicSize,
                        culling_mask     = cam.cullingMask,
                        depth            = cam.depth,
                        clear_flags      = cam.clearFlags.ToString(),
                        background_color = new { r = cam.backgroundColor.r, g = cam.backgroundColor.g, b = cam.backgroundColor.b, a = cam.backgroundColor.a },
                        hdr              = cam.allowHDR,
                        target_display   = cam.targetDisplay
                    },
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- camera-world-to-screen-point ----------
        [ReifyTool("camera-world-to-screen-point")]
        public static Task<object> WorldToScreenPoint(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var cam = ResolveCamera(args);
                var world = ReadVec3Required(args?["world_position"], "world_position");
                var screen = cam.WorldToScreenPoint(world);
                var viewport = cam.WorldToViewportPoint(world);

                return new
                {
                    camera_instance_id = GameObjectResolver.InstanceIdOf(cam),
                    world_position     = V3(world),
                    screen_point       = V3(screen), // z = distance from camera
                    viewport_point     = V3(viewport),
                    on_screen          = screen.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1,
                    behind_camera      = screen.z < 0,
                    pixel_width        = cam.pixelWidth,
                    pixel_height       = cam.pixelHeight,
                    read_at_utc        = DateTime.UtcNow.ToString("o"),
                    frame              = (long)Time.frameCount
                };
            });
        }

        // ---------- camera-screen-to-world-ray ----------
        [ReifyTool("camera-screen-to-world-ray")]
        public static Task<object> ScreenToWorldRay(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var cam = ResolveCamera(args);
                var sx = args?.Value<float?>("screen_x") ?? throw new ArgumentException("screen_x is required.");
                var sy = args?.Value<float?>("screen_y") ?? throw new ArgumentException("screen_y is required.");
                var ray = cam.ScreenPointToRay(new Vector3(sx, sy, 0));
                return new
                {
                    camera_instance_id = GameObjectResolver.InstanceIdOf(cam),
                    screen             = new { x = sx, y = sy },
                    origin             = V3(ray.origin),
                    direction          = V3(ray.direction),
                    read_at_utc        = DateTime.UtcNow.ToString("o"),
                    frame              = (long)Time.frameCount
                };
            });
        }

        // ---------- light-create ----------
        [ReifyTool("light-create")]
        public static Task<object> LightCreate(JToken args)
        {
            var name       = args?.Value<string>("name") ?? "Light";
            var typeStr    = args?.Value<string>("light_type") ?? "Point";
            var parentPath = args?.Value<string>("parent_path");

            if (!Enum.TryParse<LightType>(typeStr, true, out var lightType))
                throw new ArgumentException(
                    $"light_type '{typeStr}' must be Directional, Point, Spot, Area, Disc, or Rectangle.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, $"Reify: create {lightType} Light");
                var light = go.AddComponent<Light>();
                light.type = lightType;

                if (!string.IsNullOrEmpty(parentPath))
                {
                    var parent = GameObjectResolver.ByPath(parentPath)
                        ?? throw new InvalidOperationException($"Parent not found: {parentPath}");
                    Undo.SetTransformParent(go.transform, parent.transform, "Reify: reparent new Light");
                }

                // Apply any optional initial values.
                var intensity = args?.Value<float?>("intensity");
                if (intensity.HasValue) light.intensity = intensity.Value;
                var range = args?.Value<float?>("range");
                if (range.HasValue && lightType != LightType.Directional) light.range = range.Value;
                var spotAngle = args?.Value<float?>("spot_angle");
                if (spotAngle.HasValue && lightType == LightType.Spot) light.spotAngle = spotAngle.Value;
                var shadowsStr = args?.Value<string>("shadows");
                if (!string.IsNullOrEmpty(shadowsStr))
                {
                    if (!Enum.TryParse<LightShadows>(shadowsStr, true, out var sh))
                        throw new ArgumentException($"shadows '{shadowsStr}' must be None, Hard, or Soft.");
                    light.shadows = sh;
                }
                var col = args?["color"];
                if (col != null && col.Type != JTokenType.Null)
                {
                    light.color = new Color(
                        col.Value<float?>("r") ?? 1, col.Value<float?>("g") ?? 1,
                        col.Value<float?>("b") ?? 1, col.Value<float?>("a") ?? 1);
                }
                var pos = args?["world_position"];
                if (pos != null && pos.Type != JTokenType.Null)
                {
                    go.transform.position = new Vector3(
                        pos.Value<float?>("x") ?? 0, pos.Value<float?>("y") ?? 0, pos.Value<float?>("z") ?? 0);
                }

                return BuildLightDto(light);
            });
        }

        // ---------- light-set ----------
        [ReifyTool("light-set")]
        public static Task<object> LightSet(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var light = ResolveLight(args);
                Undo.RecordObject(light, "Reify: modify Light");
                var applied = new List<string>();

                var typeStr = args?.Value<string>("light_type");
                if (!string.IsNullOrEmpty(typeStr))
                {
                    if (!Enum.TryParse<LightType>(typeStr, true, out var lt))
                        throw new ArgumentException($"light_type '{typeStr}' is invalid.");
                    light.type = lt; applied.Add("light_type");
                }
                var intensity = args?.Value<float?>("intensity");
                if (intensity.HasValue)   { light.intensity = intensity.Value; applied.Add("intensity"); }
                var range = args?.Value<float?>("range");
                if (range.HasValue)       { light.range = range.Value; applied.Add("range"); }
                var spotAngle = args?.Value<float?>("spot_angle");
                if (spotAngle.HasValue)   { light.spotAngle = spotAngle.Value; applied.Add("spot_angle"); }
                var shadowsStr = args?.Value<string>("shadows");
                if (!string.IsNullOrEmpty(shadowsStr))
                {
                    if (!Enum.TryParse<LightShadows>(shadowsStr, true, out var sh))
                        throw new ArgumentException($"shadows '{shadowsStr}' is invalid.");
                    light.shadows = sh; applied.Add("shadows");
                }
                var shadowStrength = args?.Value<float?>("shadow_strength");
                if (shadowStrength.HasValue) { light.shadowStrength = shadowStrength.Value; applied.Add("shadow_strength"); }
                var col = args?["color"];
                if (col != null && col.Type != JTokenType.Null)
                {
                    light.color = new Color(
                        col.Value<float?>("r") ?? 1, col.Value<float?>("g") ?? 1,
                        col.Value<float?>("b") ?? 1, col.Value<float?>("a") ?? 1);
                    applied.Add("color");
                }
                var cullingMask = args?.Value<int?>("culling_mask");
                if (cullingMask.HasValue) { light.cullingMask = cullingMask.Value; applied.Add("culling_mask"); }

                if (applied.Count == 0)
                    throw new ArgumentException(
                        "No writable fields provided. Expected at least one of: light_type, intensity, range, spot_angle, shadows, shadow_strength, color, culling_mask.");

                EditorUtility.SetDirty(light);
                return BuildLightDto(light, applied.ToArray());
            });
        }

        // ---------- light-inspect ----------
        [ReifyTool("light-inspect")]
        public static Task<object> LightInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var light = ResolveLight(args);
                return BuildLightDto(light);
            });
        }

        // ---------- helpers ----------
        private static object BuildLightDto(Light l, string[] applied = null)
        {
            var warnings = new List<string>();
            if (!l.enabled) warnings.Add("Light component is disabled — contributes nothing.");
            if (l.intensity <= 0f) warnings.Add($"Light intensity is {l.intensity:F3} — contributes nothing.");
            if (l.type != LightType.Directional && l.range <= 0f) warnings.Add($"Light range is {l.range:F3} — point/spot light has no reach.");
            if (l.type == LightType.Spot && l.spotAngle <= 0f) warnings.Add($"Spot light angle is {l.spotAngle:F3} — invisible cone.");

            return new
            {
                instance_id            = GameObjectResolver.InstanceIdOf(l),
                gameobject_instance_id = GameObjectResolver.InstanceIdOf(l.gameObject),
                gameobject_path        = GameObjectResolver.PathOf(l.gameObject),
                enabled                = l.enabled,
                light_type             = l.type.ToString(),
                mode                   = l.lightmapBakeType.ToString(),
                intensity              = l.intensity,
                range                  = l.range,
                spot_angle             = l.spotAngle,
                shadows                = l.shadows.ToString(),
                shadow_strength        = l.shadowStrength,
                color                  = new { r = l.color.r, g = l.color.g, b = l.color.b, a = l.color.a },
                culling_mask           = l.cullingMask,
                render_mode            = l.renderMode.ToString(),
                world_position         = V3(l.transform.position),
                world_rotation_euler   = V3(l.transform.eulerAngles),
                world_forward          = V3(l.transform.forward),
                applied                = applied,
                warnings               = warnings.ToArray(),
                read_at_utc            = DateTime.UtcNow.ToString("o"),
                frame                  = (long)Time.frameCount
            };
        }

        private static Camera ResolveCamera(JToken args)
        {
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var goPath     = args?.Value<string>("gameobject_path");
            var useMain    = args?.Value<bool?>("use_main") ?? false;

            if (useMain)
            {
                var main = Camera.main
                    ?? throw new InvalidOperationException("No Camera.main — no GameObject in scene tagged 'MainCamera' with an active Camera component.");
                return main;
            }
            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                return obj as Camera
                    ?? (obj as GameObject)?.GetComponent<Camera>()
                    ?? throw new InvalidOperationException($"instance_id {instanceId} does not resolve to a Camera or a GameObject with one.");
            }
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("Provide instance_id, gameobject_path, or use_main=true.");
            var go = GameObjectResolver.ByPath(goPath)
                ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
            return go.GetComponent<Camera>()
                ?? throw new InvalidOperationException($"GameObject '{goPath}' has no Camera component.");
        }

        private static Light ResolveLight(JToken args)
        {
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var goPath     = args?.Value<string>("gameobject_path");

            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                return obj as Light
                    ?? (obj as GameObject)?.GetComponent<Light>()
                    ?? throw new InvalidOperationException($"instance_id {instanceId} does not resolve to a Light or a GameObject with one.");
            }
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("Provide either instance_id or gameobject_path.");
            var go = GameObjectResolver.ByPath(goPath)
                ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
            return go.GetComponent<Light>()
                ?? throw new InvalidOperationException($"GameObject '{goPath}' has no Light component.");
        }

        private static Vector3 ReadVec3Required(JToken t, string field)
        {
            if (t == null || t.Type == JTokenType.Null)
                throw new ArgumentException($"{field} is required.");
            return new Vector3(
                t.Value<float?>("x") ?? 0, t.Value<float?>("y") ?? 0, t.Value<float?>("z") ?? 0);
        }

        private static object V3(Vector3 v) => new { x = v.x, y = v.y, z = v.z };
    }
}
