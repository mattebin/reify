using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// UGUI + UI Events domain. UI bugs are classically opaque without
    /// structured state — anchor/pivot weirdness, canvas sort conflicts,
    /// raycast blockers. These tools surface the state explicitly.
    /// </summary>
    internal static class UITools
    {
        // ---------- ui-canvas-inspect ----------
        [ReifyTool("ui-canvas-inspect")]
        public static Task<object> CanvasInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var canvas = ResolveCanvas(args);
                var warnings = new List<string>();

                var hasRaycaster = canvas.GetComponent<GraphicRaycaster>() != null;
                if (!hasRaycaster)
                    warnings.Add("Canvas has no GraphicRaycaster — UI elements will NOT receive pointer events. Add a GraphicRaycaster component.");

                #pragma warning disable CS0618
                var eventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
                #pragma warning restore CS0618
                if (eventSystems.Length == 0)
                    warnings.Add("No EventSystem in any loaded scene — UI input will not dispatch. Create one via GameObject > UI > Event System.");
                if (eventSystems.Length > 1)
                    warnings.Add($"{eventSystems.Length} EventSystems found — Unity expects one. Extras may conflict.");

                if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
                    warnings.Add("RenderMode is ScreenSpaceCamera but worldCamera is null — canvas will behave as Overlay and z-sort unexpectedly.");
                if (canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
                    warnings.Add("RenderMode is WorldSpace but worldCamera is null — raycaster needs a camera to resolve screen-space input.");

                var scaler = canvas.GetComponent<CanvasScaler>();

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(canvas),
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(canvas.gameObject),
                    gameobject_path        = GameObjectResolver.PathOf(canvas.gameObject),
                    render_mode            = canvas.renderMode.ToString(),
                    is_root_canvas         = canvas.isRootCanvas,
                    sorting_layer_name     = canvas.sortingLayerName,
                    sorting_layer_id       = canvas.sortingLayerID,
                    sorting_order          = canvas.sortingOrder,
                    world_camera           = canvas.worldCamera != null ? new {
                        name        = canvas.worldCamera.name,
                        instance_id = GameObjectResolver.InstanceIdOf(canvas.worldCamera)
                    } : null,
                    plane_distance         = canvas.planeDistance,
                    pixel_perfect          = canvas.pixelPerfect,
                    reference_pixels_per_unit = canvas.referencePixelsPerUnit,
                    override_sorting       = canvas.overrideSorting,
                    scale_factor           = canvas.scaleFactor,
                    has_graphic_raycaster  = hasRaycaster,
                    canvas_scaler = scaler != null ? new {
                        ui_scale_mode       = scaler.uiScaleMode.ToString(),
                        reference_resolution = new { x = scaler.referenceResolution.x, y = scaler.referenceResolution.y },
                        match_width_or_height = scaler.matchWidthOrHeight,
                        screen_match_mode   = scaler.screenMatchMode.ToString(),
                        reference_pixels_per_unit = scaler.referencePixelsPerUnit
                    } : null,
                    event_system_count     = eventSystems.Length,
                    warnings               = warnings.ToArray(),
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- ui-rect-transform-inspect ----------
        [ReifyTool("ui-rect-transform-inspect")]
        public static Task<object> RectTransformInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var rt = ResolveRectTransform(args);
                var warnings = new List<string>();

                // Anchor-pivot sanity: stretch anchors (min != max) with a
                // non-matching pivot often indicates a layout bug.
                var isStretchHoriz = !Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x);
                var isStretchVert  = !Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y);

                if (isStretchHoriz && (rt.pivot.x < 0 || rt.pivot.x > 1))
                    warnings.Add($"Horizontal stretch anchors with pivot.x={rt.pivot.x:F2} outside [0,1] — unusual.");
                if (isStretchVert && (rt.pivot.y < 0 || rt.pivot.y > 1))
                    warnings.Add($"Vertical stretch anchors with pivot.y={rt.pivot.y:F2} outside [0,1] — unusual.");

                var parentCanvas = rt.GetComponentInParent<Canvas>();
                if (parentCanvas == null)
                    warnings.Add("RectTransform has no parent Canvas — won't render as UI. Intended for runtime reparenting? Otherwise an error.");

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(rt),
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(rt.gameObject),
                    gameobject_path        = GameObjectResolver.PathOf(rt.gameObject),
                    anchor_min             = V2(rt.anchorMin),
                    anchor_max             = V2(rt.anchorMax),
                    pivot                  = V2(rt.pivot),
                    anchored_position      = V2(rt.anchoredPosition),
                    size_delta             = V2(rt.sizeDelta),
                    offset_min             = V2(rt.offsetMin),
                    offset_max             = V2(rt.offsetMax),
                    local_rotation_euler   = V3(rt.localEulerAngles),
                    local_scale            = V3(rt.localScale),
                    rect                   = new { x = rt.rect.x, y = rt.rect.y, width = rt.rect.width, height = rt.rect.height },
                    world_corners          = WorldCorners(rt),
                    parent_canvas          = parentCanvas != null ? new {
                        name        = parentCanvas.name,
                        instance_id = GameObjectResolver.InstanceIdOf(parentCanvas),
                        render_mode = parentCanvas.renderMode.ToString()
                    } : null,
                    is_stretch_horizontal  = isStretchHoriz,
                    is_stretch_vertical    = isStretchVert,
                    warnings               = warnings.ToArray(),
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- ui-rect-transform-set ----------
        [ReifyTool("ui-rect-transform-set")]
        public static Task<object> RectTransformSet(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var rt = ResolveRectTransform(args);
                Undo.RecordObject(rt, "Reify: set RectTransform");

                var before = new {
                    anchor_min        = V2(rt.anchorMin),
                    anchor_max        = V2(rt.anchorMax),
                    pivot             = V2(rt.pivot),
                    anchored_position = V2(rt.anchoredPosition),
                    size_delta        = V2(rt.sizeDelta)
                };

                var applied = new List<string>();

                var aMin = args?["anchor_min"];
                if (aMin != null && aMin.Type != JTokenType.Null) { rt.anchorMin = ReadVec2(aMin); applied.Add("anchor_min"); }
                var aMax = args?["anchor_max"];
                if (aMax != null && aMax.Type != JTokenType.Null) { rt.anchorMax = ReadVec2(aMax); applied.Add("anchor_max"); }
                var pivot = args?["pivot"];
                if (pivot != null && pivot.Type != JTokenType.Null) { rt.pivot = ReadVec2(pivot); applied.Add("pivot"); }
                var anchored = args?["anchored_position"];
                if (anchored != null && anchored.Type != JTokenType.Null) { rt.anchoredPosition = ReadVec2(anchored); applied.Add("anchored_position"); }
                var sizeDelta = args?["size_delta"];
                if (sizeDelta != null && sizeDelta.Type != JTokenType.Null) { rt.sizeDelta = ReadVec2(sizeDelta); applied.Add("size_delta"); }

                if (applied.Count == 0)
                    throw new ArgumentException(
                        "No writable fields provided. Expected at least one of: anchor_min, anchor_max, pivot, anchored_position, size_delta.");

                EditorUtility.SetDirty(rt);

                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(rt),
                    gameobject_path = GameObjectResolver.PathOf(rt.gameObject),
                    applied,
                    before,
                    after = new {
                        anchor_min        = V2(rt.anchorMin),
                        anchor_max        = V2(rt.anchorMax),
                        pivot             = V2(rt.pivot),
                        anchored_position = V2(rt.anchoredPosition),
                        size_delta        = V2(rt.sizeDelta)
                    },
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- ui-graphic-inspect ----------
        [ReifyTool("ui-graphic-inspect")]
        public static Task<object> GraphicInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var go = ResolveGameObject(args);
                var graphic = go.GetComponent<Graphic>()
                    ?? throw new InvalidOperationException(
                        $"GameObject '{GameObjectResolver.PathOf(go)}' has no UI Graphic component (Image/Text/RawImage/TextMeshProUGUI etc.).");

                var warnings = new List<string>();
                if (!graphic.enabled)
                    warnings.Add("Graphic component is disabled — won't render.");
                if (graphic.color.a <= 0f)
                    warnings.Add($"Graphic color alpha is {graphic.color.a:F3} — invisible.");
                if (!graphic.raycastTarget)
                    warnings.Add("raycastTarget is false — won't block or receive pointer events.");

                var subtype = SubtypeInfo(graphic, warnings);

                var canvas = graphic.canvas;

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(graphic),
                    type_fqn               = graphic.GetType().FullName,
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(graphic.gameObject),
                    gameobject_path        = GameObjectResolver.PathOf(graphic.gameObject),
                    enabled                = graphic.enabled,
                    color                  = new { r = graphic.color.r, g = graphic.color.g, b = graphic.color.b, a = graphic.color.a },
                    material_asset_path    = graphic.material != null ? AssetDatabase.GetAssetPath(graphic.material) : null,
                    material_name          = graphic.material != null ? graphic.material.name : null,
                    raycast_target         = graphic.raycastTarget,
                    main_texture           = graphic.mainTexture != null ? new {
                        name        = graphic.mainTexture.name,
                        asset_path  = AssetDatabase.GetAssetPath(graphic.mainTexture),
                        width       = graphic.mainTexture.width,
                        height      = graphic.mainTexture.height
                    } : null,
                    parent_canvas = canvas != null ? new {
                        name        = canvas.name,
                        instance_id = GameObjectResolver.InstanceIdOf(canvas),
                        render_mode = canvas.renderMode.ToString()
                    } : null,
                    depth                  = graphic.depth,
                    subtype,
                    warnings               = warnings.ToArray(),
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- ui-selectable-inspect ----------
        [ReifyTool("ui-selectable-inspect")]
        public static Task<object> SelectableInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var go = ResolveGameObject(args);
                var sel = go.GetComponent<Selectable>()
                    ?? throw new InvalidOperationException(
                        $"GameObject '{GameObjectResolver.PathOf(go)}' has no Selectable (Button/Toggle/Slider/InputField/Dropdown/Scrollbar).");

                var warnings = new List<string>();
                if (!sel.interactable) warnings.Add("Selectable.interactable is false — pointer events ignored.");
                if (sel.targetGraphic == null) warnings.Add("Selectable.targetGraphic is null — visual state transitions won't render.");

                // Subtype-specific state via reflection-free casting.
                object subtype = null;
                switch (sel)
                {
                    case Button btn:
                        subtype = new {
                            kind = "Button",
                            on_click_persistent_count = btn.onClick.GetPersistentEventCount()
                        };
                        break;
                    case Toggle tg:
                        subtype = new {
                            kind = "Toggle",
                            is_on = tg.isOn,
                            group = tg.group != null ? tg.group.name : null
                        };
                        break;
                    case Slider sl:
                        subtype = new {
                            kind = "Slider",
                            value = sl.value,
                            min_value = sl.minValue,
                            max_value = sl.maxValue,
                            whole_numbers = sl.wholeNumbers,
                            direction = sl.direction.ToString()
                        };
                        break;
                    case Scrollbar sb:
                        subtype = new {
                            kind = "Scrollbar",
                            value = sb.value,
                            size = sb.size,
                            steps = sb.numberOfSteps,
                            direction = sb.direction.ToString()
                        };
                        break;
                    case Dropdown dd:
                        subtype = new {
                            kind = "Dropdown",
                            value = dd.value,
                            option_count = dd.options != null ? dd.options.Count : 0
                        };
                        break;
                    case InputField inp:
                        subtype = new {
                            kind = "InputField",
                            text = inp.text,
                            content_type = inp.contentType.ToString(),
                            character_limit = inp.characterLimit,
                            is_focused = inp.isFocused
                        };
                        break;
                    default:
                        subtype = new { kind = sel.GetType().Name };
                        break;
                }

                // Selectable.currentSelectionState is protected in Unity 6.
                // Reach it via reflection; null out gracefully on failure.
                string selectionState = null;
                try
                {
                    var prop = typeof(Selectable).GetProperty("currentSelectionState",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);
                    if (prop != null)
                        selectionState = prop.GetValue(sel)?.ToString();
                } catch { /* reflection layout drift — leave null */ }

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(sel),
                    type_fqn               = sel.GetType().FullName,
                    gameobject_path        = GameObjectResolver.PathOf(sel.gameObject),
                    interactable           = sel.interactable,
                    current_selection_state = selectionState,
                    transition             = sel.transition.ToString(),
                    target_graphic         = sel.targetGraphic != null ? new {
                        type_fqn    = sel.targetGraphic.GetType().FullName,
                        instance_id = GameObjectResolver.InstanceIdOf(sel.targetGraphic)
                    } : null,
                    subtype,
                    warnings               = warnings.ToArray(),
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- ui-event-system-state ----------
        [ReifyTool("ui-event-system-state")]
        public static Task<object> EventSystemState(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                #pragma warning disable CS0618
                var systems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
                var raycasters = UnityEngine.Object.FindObjectsByType<BaseRaycaster>(FindObjectsSortMode.None);
                #pragma warning restore CS0618

                var warnings = new List<string>();
                if (systems.Length == 0) warnings.Add("No EventSystem in any loaded scene — no UI input will dispatch.");
                if (systems.Length > 1)  warnings.Add($"{systems.Length} EventSystems — Unity expects one.");
                if (raycasters.Length == 0) warnings.Add("No BaseRaycaster (GraphicRaycaster / Physics2DRaycaster / PhysicsRaycaster) — no UI pointer hits.");

                var current = EventSystem.current;
                object currentInfo = null;
                if (current != null)
                {
                    currentInfo = new {
                        instance_id        = GameObjectResolver.InstanceIdOf(current),
                        gameobject_path    = GameObjectResolver.PathOf(current.gameObject),
                        enabled            = current.enabled,
                        sends_navigation_events = current.sendNavigationEvents,
                        pixel_drag_threshold    = current.pixelDragThreshold,
                        current_selected        = current.currentSelectedGameObject != null ? new {
                            instance_id = GameObjectResolver.InstanceIdOf(current.currentSelectedGameObject),
                            path        = GameObjectResolver.PathOf(current.currentSelectedGameObject)
                        } : null,
                        first_selected        = current.firstSelectedGameObject != null ? new {
                            instance_id = GameObjectResolver.InstanceIdOf(current.firstSelectedGameObject),
                            path        = GameObjectResolver.PathOf(current.firstSelectedGameObject)
                        } : null,
                        is_focused            = current.isFocused,
                        already_selecting     = current.alreadySelecting
                    };
                }

                var raycasterList = new List<object>(raycasters.Length);
                foreach (var r in raycasters)
                {
                    raycasterList.Add(new {
                        type_fqn        = r.GetType().FullName,
                        instance_id     = GameObjectResolver.InstanceIdOf(r),
                        gameobject_path = GameObjectResolver.PathOf(r.gameObject),
                        enabled         = r.enabled,
                        sort_order_priority = r.sortOrderPriority,
                        render_order_priority = r.renderOrderPriority
                    });
                }

                return new
                {
                    event_system_count = systems.Length,
                    raycaster_count    = raycasters.Length,
                    raycasters         = raycasterList.ToArray(),
                    current            = currentInfo,
                    warnings           = warnings.ToArray(),
                    read_at_utc        = DateTime.UtcNow.ToString("o"),
                    frame              = (long)Time.frameCount
                };
            });
        }

        // ---------- ui-raycast-at-point ----------
        [ReifyTool("ui-raycast-at-point")]
        public static Task<object> RaycastAtPoint(JToken args)
        {
            var x = args?.Value<float?>("screen_x") ?? throw new ArgumentException("screen_x is required.");
            var y = args?.Value<float?>("screen_y") ?? throw new ArgumentException("screen_y is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                // EventSystem.current is the runtime-set singleton; it's null
                // in edit mode until the component's Awake fires. Fall back
                // to scanning the scene for any EventSystem so the tool works
                // without entering play mode.
                var es = EventSystem.current;
                if (es == null)
                {
                    #pragma warning disable CS0618
                    var all = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
                    #pragma warning restore CS0618
                    if (all.Length == 0)
                        throw new InvalidOperationException(
                            "No EventSystem found in any loaded scene. Create one via GameObject > UI > Event System, or add the EventSystem component to a GameObject.");
                    es = all[0];
                }

                var pointer = new PointerEventData(es) { position = new Vector2(x, y) };
                var results = new List<RaycastResult>();
                es.RaycastAll(pointer, results);

                var dtos = new object[results.Count];
                for (var i = 0; i < results.Count; i++)
                {
                    var r = results[i];
                    dtos[i] = new {
                        index            = r.index,
                        gameobject_path  = r.gameObject != null ? GameObjectResolver.PathOf(r.gameObject) : null,
                        gameobject_instance_id = r.gameObject != null ? GameObjectResolver.InstanceIdOf(r.gameObject) : 0,
                        distance         = r.distance,
                        screen_position  = new { x = r.screenPosition.x, y = r.screenPosition.y },
                        world_position   = new { x = r.worldPosition.x, y = r.worldPosition.y, z = r.worldPosition.z },
                        sorting_layer    = r.sortingLayer,
                        sorting_order    = r.sortingOrder,
                        depth            = r.depth,
                        raycaster_type   = r.module != null ? r.module.GetType().FullName : null
                    };
                }

                return new
                {
                    query       = new { screen_x = x, screen_y = y },
                    hit_count   = results.Count,
                    hits        = dtos,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- subtype info for Graphic ----------
        private static object SubtypeInfo(Graphic g, List<string> warnings)
        {
            switch (g)
            {
                case Image img:
                    if (img.sprite == null) warnings.Add("Image has no sprite — renders as a flat color rect.");
                    return new {
                        kind       = "Image",
                        sprite     = img.sprite != null ? new {
                            name       = img.sprite.name,
                            asset_path = AssetDatabase.GetAssetPath(img.sprite)
                        } : null,
                        image_type = img.type.ToString(),
                        fill_amount = img.fillAmount,
                        preserve_aspect = img.preserveAspect
                    };
                case RawImage raw:
                    return new {
                        kind       = "RawImage",
                        texture    = raw.texture != null ? new {
                            name       = raw.texture.name,
                            asset_path = AssetDatabase.GetAssetPath(raw.texture)
                        } : null,
                        uv_rect    = new { x = raw.uvRect.x, y = raw.uvRect.y, w = raw.uvRect.width, h = raw.uvRect.height }
                    };
                case Text t:
                    if (string.IsNullOrEmpty(t.text))
                        warnings.Add("Text.text is empty — nothing will render.");
                    return new {
                        kind       = "Text",
                        text       = t.text,
                        font       = t.font != null ? t.font.name : null,
                        font_size  = t.fontSize,
                        alignment  = t.alignment.ToString(),
                        rich_text  = t.supportRichText
                    };
                default:
                    // TextMeshProUGUI / custom Graphic subclasses land here.
                    return new { kind = g.GetType().Name };
            }
        }

        // ---------- helpers ----------
        private static Canvas ResolveCanvas(JToken args)
        {
            var go = ResolveGameObject(args);
            return go.GetComponent<Canvas>()
                ?? throw new InvalidOperationException(
                    $"GameObject '{GameObjectResolver.PathOf(go)}' has no Canvas component.");
        }

        private static RectTransform ResolveRectTransform(JToken args)
        {
            var go = ResolveGameObject(args);
            return go.GetComponent<RectTransform>()
                ?? throw new InvalidOperationException(
                    $"GameObject '{GameObjectResolver.PathOf(go)}' has no RectTransform (not a UI element).");
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
                if (obj is Component c) return c.gameObject;
                throw new InvalidOperationException(
                    $"instance_id {instanceId} is neither a GameObject nor a Component.");
            }
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("Provide either instance_id or gameobject_path.");
            return GameObjectResolver.ByPath(goPath)
                ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
        }

        private static Vector2 ReadVec2(JToken t) => new Vector2(
            t.Value<float?>("x") ?? 0,
            t.Value<float?>("y") ?? 0);
        private static object V2(Vector2 v) => new { x = v.x, y = v.y };
        private static object V3(Vector3 v) => new { x = v.x, y = v.y, z = v.z };

        private static object[] WorldCorners(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var r = new object[4];
            for (var i = 0; i < 4; i++) r[i] = V3(corners[i]);
            return r;
        }
    }
}
