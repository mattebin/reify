using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// UI Toolkit (UIElements / UXML / USS) surface — distinct from UGUI
    /// which is covered by UITools. Inspects UIDocument hosts, the
    /// VisualElement tree, and UXML / USS asset metadata.
    /// </summary>
    internal static class UIToolkitTools
    {
        // ---------- ui-toolkit-document-inspect ----------
        [ReifyTool("ui-toolkit-document-inspect")]
        public static Task<object> DocumentInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var doc = ResolveDocument(args);
                var warnings = new List<string>();

                if (!doc.enabled) warnings.Add("UIDocument is disabled — no root VisualElement mounted.");
                if (doc.visualTreeAsset == null)
                    warnings.Add("UIDocument has no visualTreeAsset (source UXML).");
                if (doc.panelSettings == null)
                    warnings.Add("UIDocument has no panelSettings assigned — default runtime panel will be used.");

                var root = doc.rootVisualElement;
                var childCount = root != null ? root.childCount : 0;
                var descendants = root != null ? CountDescendants(root) : 0;

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(doc),
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(doc.gameObject),
                    gameobject_path        = GameObjectResolver.PathOf(doc.gameObject),
                    enabled                = doc.enabled,
                    sort_order             = doc.sortingOrder,
                    panel_settings = doc.panelSettings != null ? new
                    {
                        name       = doc.panelSettings.name,
                        asset_path = AssetDatabase.GetAssetPath(doc.panelSettings)
                    } : null,
                    visual_tree_asset = doc.visualTreeAsset != null ? new
                    {
                        name       = doc.visualTreeAsset.name,
                        asset_path = AssetDatabase.GetAssetPath(doc.visualTreeAsset)
                    } : null,
                    root_element_name = root != null ? (root.name ?? "<root>") : null,
                    child_count       = childCount,
                    descendant_count  = descendants,
                    warnings          = warnings.ToArray(),
                    read_at_utc       = DateTime.UtcNow.ToString("o"),
                    frame             = (long)Time.frameCount
                };
            });
        }

        // ---------- ui-toolkit-element-tree ----------
        [ReifyTool("ui-toolkit-element-tree")]
        public static Task<object> ElementTree(JToken args)
        {
            var limit = args?.Value<int?>("limit") ?? 500;
            var includeStyles = args?.Value<bool?>("include_styles") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var doc = ResolveDocument(args);
                var root = doc.rootVisualElement
                    ?? throw new InvalidOperationException("UIDocument has no active rootVisualElement (not mounted yet?).");

                var flat = new List<object>();
                var truncated = false;
                WalkFlat(root, 0, null, flat, limit, includeStyles, ref truncated);

                return new
                {
                    document_instance_id = GameObjectResolver.InstanceIdOf(doc),
                    gameobject_path      = GameObjectResolver.PathOf(doc.gameObject),
                    returned    = flat.Count,
                    truncated,
                    limit,
                    elements    = flat.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- ui-toolkit-uxml-inspect ----------
        [ReifyTool("ui-toolkit-uxml-inspect")]
        public static Task<object> UxmlInspect(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required (points at a .uxml asset).");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path)
                    ?? throw new InvalidOperationException($"No VisualTreeAsset at path: {path}");

                // Instantiate a temporary copy to probe the resulting element
                // count without touching any live UIDocument.
                var inst = tree.Instantiate();
                var count = inst != null ? 1 + CountDescendants(inst) : 0;
                var childCount = inst != null ? inst.childCount : 0;

                // Templates referenced by this UXML (best-effort via the
                // runtime VisualTreeAsset.templateDependencies — present as
                // asset references in the serialized graph).
                var deps = AssetDatabase.GetDependencies(path, recursive: false);

                return new
                {
                    asset_path       = path,
                    instance_id      = GameObjectResolver.InstanceIdOf(tree),
                    name             = tree.name,
                    instantiated_root_child_count = childCount,
                    instantiated_total_element_count = count,
                    dependencies     = deps,
                    read_at_utc      = DateTime.UtcNow.ToString("o"),
                    frame            = (long)Time.frameCount
                };
            });
        }

        // ---------- ui-toolkit-uss-inspect ----------
        [ReifyTool("ui-toolkit-uss-inspect")]
        public static Task<object> UssInspect(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required (points at a .uss asset).");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path)
                    ?? throw new InvalidOperationException($"No StyleSheet at path: {path}");

                // StyleSheet exposes the rule count and complexRules via
                // internal APIs; grab the public surface only.
                var deps = AssetDatabase.GetDependencies(path, recursive: false);

                return new
                {
                    asset_path   = path,
                    instance_id  = GameObjectResolver.InstanceIdOf(sheet),
                    name         = sheet.name,
                    import_glossary_size = sheet.importedWithCompilerVersion,
                    dependencies = deps,
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static UIDocument ResolveDocument(JToken args)
        {
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var goPath     = args?.Value<string>("gameobject_path");

            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                if (obj is UIDocument d) return d;
                if (obj is GameObject go)
                {
                    var d2 = go.GetComponent<UIDocument>();
                    if (d2 != null) return d2;
                }
                throw new InvalidOperationException(
                    $"instance_id {instanceId} does not resolve to a UIDocument or a GameObject with one.");
            }
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("Provide either instance_id or gameobject_path.");
            var g = GameObjectResolver.ByPath(goPath)
                ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
            return g.GetComponent<UIDocument>()
                ?? throw new InvalidOperationException($"GameObject '{goPath}' has no UIDocument component.");
        }

        private static void WalkFlat(VisualElement v, int depth, string parentPath,
            List<object> flat, int limit, bool includeStyles, ref bool truncated)
        {
            if (flat.Count >= limit) { truncated = true; return; }

            var path = string.IsNullOrEmpty(parentPath)
                ? (string.IsNullOrEmpty(v.name) ? $"#{flat.Count}" : v.name)
                : parentPath + "/" + (string.IsNullOrEmpty(v.name) ? $"#{flat.Count}" : v.name);

            var classList = new List<string>();
            foreach (var c in v.GetClasses()) classList.Add(c);

            object styles = null;
            if (includeStyles)
            {
                styles = new
                {
                    display     = v.resolvedStyle.display.ToString(),
                    visibility  = v.resolvedStyle.visibility.ToString(),
                    opacity     = v.resolvedStyle.opacity,
                    width       = v.resolvedStyle.width,
                    height      = v.resolvedStyle.height,
                    color       = new { r = v.resolvedStyle.color.r, g = v.resolvedStyle.color.g, b = v.resolvedStyle.color.b, a = v.resolvedStyle.color.a }
                };
            }

            flat.Add(new
            {
                path,
                depth,
                type_fqn     = v.GetType().FullName,
                name         = v.name,
                classes      = classList.ToArray(),
                child_count  = v.childCount,
                enabled      = v.enabledSelf,
                picking_mode = v.pickingMode.ToString(),
                layout = new
                {
                    x = v.layout.x,
                    y = v.layout.y,
                    width  = v.layout.width,
                    height = v.layout.height
                },
                styles
            });

            for (var i = 0; i < v.childCount; i++)
            {
                if (flat.Count >= limit) { truncated = true; return; }
                WalkFlat(v[i], depth + 1, path, flat, limit, includeStyles, ref truncated);
            }
        }

        private static int CountDescendants(VisualElement v)
        {
            var n = 0;
            for (var i = 0; i < v.childCount; i++)
            {
                n++;
                n += CountDescendants(v[i]);
            }
            return n;
        }
    }
}
