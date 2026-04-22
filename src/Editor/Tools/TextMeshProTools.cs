using System;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// TextMesh Pro surface via reflection so reify stays free of a direct
    /// com.unity.textmeshpro dependency. Covers TMP_Text (the common base
    /// for TextMeshProUGUI + TextMeshPro 3D) + TMP_FontAsset. When the
    /// package isn't loaded, each tool returns a structured error instead
    /// of failing to compile.
    /// </summary>
    internal static class TextMeshProTools
    {
        private const string TmpAsm = "Unity.TextMeshPro";

        // ---------- tmp-text-inspect ----------
        [ReifyTool("tmp-text-inspect")]
        public static Task<object> TextInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var tmpTextType = Type.GetType($"TMPro.TMP_Text, {TmpAsm}")
                    ?? throw new InvalidOperationException(
                        "TMPro.TMP_Text not loaded — install com.unity.textmeshpro to use this tool.");

                var go = ResolveGameObject(args);
                var tmp = go.GetComponent(tmpTextType)
                    ?? throw new InvalidOperationException(
                        $"GameObject '{GameObjectResolver.PathOf(go)}' has no TMP_Text-derived component.");
                var t = tmp.GetType();

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(tmp as UnityEngine.Object),
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(go),
                    gameobject_path        = GameObjectResolver.PathOf(go),
                    type_fqn               = t.FullName,
                    text                   = t.GetProperty("text")?.GetValue(tmp) as string,
                    font_size              = Get<float>(t, tmp, "fontSize"),
                    auto_size_text_container = Get<bool>(t, tmp, "autoSizeTextContainer"),
                    enable_auto_sizing     = Get<bool>(t, tmp, "enableAutoSizing"),
                    font_size_min          = Get<float>(t, tmp, "fontSizeMin"),
                    font_size_max          = Get<float>(t, tmp, "fontSizeMax"),
                    color                  = ColorDto(Get<Color>(t, tmp, "color")),
                    alignment              = t.GetProperty("alignment")?.GetValue(tmp)?.ToString(),
                    rich_text              = Get<bool>(t, tmp, "richText"),
                    raycast_target         = Get<bool>(t, tmp, "raycastTarget"),
                    character_count        = Get<int>(t, tmp, "textInfo") != 0 ? 0 : 0, // placeholder; real count below
                    outline_width          = Get<float>(t, tmp, "outlineWidth"),
                    outline_color          = ColorDto(Get<Color>(t, tmp, "outlineColor")),
                    font_asset_path        = AssetPathOf(t.GetProperty("font")?.GetValue(tmp) as UnityEngine.Object),
                    material_asset_path    = AssetPathOf(t.GetProperty("fontSharedMaterial")?.GetValue(tmp) as UnityEngine.Object),
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- tmp-text-set ----------
        [ReifyTool("tmp-text-set")]
        public static Task<object> TextSet(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var tmpTextType = Type.GetType($"TMPro.TMP_Text, {TmpAsm}")
                    ?? throw new InvalidOperationException(
                        "TMPro.TMP_Text not loaded — install com.unity.textmeshpro to use this tool.");

                var go = ResolveGameObject(args);
                var tmp = go.GetComponent(tmpTextType)
                    ?? throw new InvalidOperationException(
                        $"GameObject '{GameObjectResolver.PathOf(go)}' has no TMP_Text-derived component.");
                var t = tmp.GetType();

                Undo.RecordObject(tmp as UnityEngine.Object, "Reify: set TMP_Text");

                var applied = new System.Collections.Generic.List<string>();

                var text = args?.Value<string>("text");
                if (text != null) { t.GetProperty("text")?.SetValue(tmp, text); applied.Add("text"); }

                var fontSize = args?.Value<float?>("font_size");
                if (fontSize.HasValue) { t.GetProperty("fontSize")?.SetValue(tmp, fontSize.Value); applied.Add("font_size"); }

                var col = args?["color"];
                if (col != null && col.Type != JTokenType.Null)
                {
                    var c = new Color(
                        col.Value<float?>("r") ?? 1, col.Value<float?>("g") ?? 1,
                        col.Value<float?>("b") ?? 1, col.Value<float?>("a") ?? 1);
                    t.GetProperty("color")?.SetValue(tmp, c);
                    applied.Add("color");
                }

                var alignment = args?.Value<string>("alignment");
                if (!string.IsNullOrEmpty(alignment))
                {
                    var alignmentEnumType = Type.GetType($"TMPro.TextAlignmentOptions, {TmpAsm}");
                    if (alignmentEnumType == null)
                        throw new InvalidOperationException("TMPro.TextAlignmentOptions not loaded.");
                    if (!Enum.TryParse(alignmentEnumType, alignment, true, out var v))
                        throw new ArgumentException($"alignment '{alignment}' is not a valid TMPro.TextAlignmentOptions value.");
                    t.GetProperty("alignment")?.SetValue(tmp, v);
                    applied.Add("alignment");
                }

                var richText = args?.Value<bool?>("rich_text");
                if (richText.HasValue) { t.GetProperty("richText")?.SetValue(tmp, richText.Value); applied.Add("rich_text"); }

                var raycast = args?.Value<bool?>("raycast_target");
                if (raycast.HasValue) { t.GetProperty("raycastTarget")?.SetValue(tmp, raycast.Value); applied.Add("raycast_target"); }

                if (applied.Count == 0)
                    throw new ArgumentException(
                        "No writable fields provided. Expected at least one of: text, font_size, color, alignment, rich_text, raycast_target.");

                EditorUtility.SetDirty(tmp as UnityEngine.Object);

                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(tmp as UnityEngine.Object),
                    gameobject_path = GameObjectResolver.PathOf(go),
                    applied         = applied.ToArray(),
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- tmp-font-asset-inspect ----------
        [ReifyTool("tmp-font-asset-inspect")]
        public static Task<object> FontAssetInspect(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var faType = Type.GetType($"TMPro.TMP_FontAsset, {TmpAsm}")
                    ?? throw new InvalidOperationException(
                        "TMPro.TMP_FontAsset not loaded — install com.unity.textmeshpro to use this tool.");

                var asset = AssetDatabase.LoadAssetAtPath(path, faType);
                if (asset == null)
                    throw new InvalidOperationException($"No TMP_FontAsset at path: {path}");

                var t = asset.GetType();
                var sourceFont = t.GetProperty("sourceFontFile")?.GetValue(asset) as UnityEngine.Object;
                var faceInfoObj = t.GetProperty("faceInfo")?.GetValue(asset);
                var faceType = faceInfoObj?.GetType();

                return new
                {
                    asset_path          = path,
                    instance_id         = GameObjectResolver.InstanceIdOf(asset),
                    name                = asset.name,
                    source_font_path    = AssetPathOf(sourceFont),
                    creation_settings   = t.GetProperty("creationSettings")?.GetValue(asset)?.ToString(),
                    atlas_width         = Get<int>(t, asset, "atlasWidth"),
                    atlas_height        = Get<int>(t, asset, "atlasHeight"),
                    atlas_padding       = Get<int>(t, asset, "atlasPadding"),
                    face_family         = faceType?.GetProperty("familyName")?.GetValue(faceInfoObj) as string,
                    face_style          = faceType?.GetProperty("styleName")?.GetValue(faceInfoObj) as string,
                    face_point_size     = faceType != null ? Get<float>(faceType, faceInfoObj, "pointSize") : 0f,
                    character_count     = (t.GetProperty("characterTable")?.GetValue(asset) as System.Collections.ICollection)?.Count ?? 0,
                    glyph_count         = (t.GetProperty("glyphTable")?.GetValue(asset) as System.Collections.ICollection)?.Count ?? 0,
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
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

        private static T Get<T>(Type type, object instance, string name)
        {
            var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) try { return (T)Convert.ChangeType(p.GetValue(instance), typeof(T)); } catch { }
            var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) try { return (T)Convert.ChangeType(f.GetValue(instance), typeof(T)); } catch { }
            return default;
        }

        private static string AssetPathOf(UnityEngine.Object o) =>
            o != null ? AssetDatabase.GetAssetPath(o) : null;

        private static object ColorDto(Color c) => new { r = c.r, g = c.g, b = c.b, a = c.a };
    }
}
