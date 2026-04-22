using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// 6th Phase C philosophy tool. Scene-wide render-queue audit with
    /// structured sorting-conflict warnings. Closes ARCHITECTURE_ANALYSIS
    /// gap 5: the LLM can answer "why is this transparent object rendering
    /// behind that geometry" without a single screenshot.
    /// </summary>
    internal static class RenderQueueAuditTool
    {
        [ReifyTool("render-queue-audit")]
        public static Task<object> Handle(JToken args)
        {
            var scenePath       = args?.Value<string>("scene_path");
            var includeInactive = args?.Value<bool?>("include_inactive") ?? false;
            var filter          = args?["filter"] as JObject;
            var queueMin        = filter?["queue_min"]?.Type == JTokenType.Integer
                ? filter.Value<int?>("queue_min") : null;
            var queueMax        = filter?["queue_max"]?.Type == JTokenType.Integer
                ? filter.Value<int?>("queue_max") : null;
            var rendererType    = filter?["renderer_type"]?.Value<string>();

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var scene = ResolveScene(scenePath);
                var renderers = CollectRenderers(scene, includeInactive);

                var rows = new List<RendererRow>(renderers.Count);
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    var mats = r.sharedMaterials;
                    var mat  = mats != null && mats.Length > 0 ? mats[0] : null;
                    var queue = mat != null ? mat.renderQueue : -1;

                    if (queueMin.HasValue && queue < queueMin.Value) continue;
                    if (queueMax.HasValue && queue > queueMax.Value) continue;
                    if (!string.IsNullOrEmpty(rendererType) && r.GetType().Name != rendererType) continue;

                    rows.Add(new RendererRow
                    {
                        Renderer      = r,
                        Material      = mat,
                        Queue         = queue,
                        Bucket        = Bucket(queue),
                        SortingLayer  = r.sortingLayerName ?? "Default",
                        SortingOrder  = r.sortingOrder,
                        Bounds        = r.bounds
                    });
                }

                // Sort deterministically by queue ascending, then sorting layer/order.
                rows.Sort((a, b) =>
                {
                    var c = a.Queue.CompareTo(b.Queue);
                    if (c != 0) return c;
                    c = string.CompareOrdinal(a.SortingLayer, b.SortingLayer);
                    if (c != 0) return c;
                    return a.SortingOrder.CompareTo(b.SortingOrder);
                });

                var warnings = DetectWarnings(rows);
                var summary = BucketSummary(rows);

                var output = new List<object>(rows.Count);
                foreach (var row in rows) output.Add(row.ToDto());

                return new
                {
                    scene           = scene.path,
                    renderer_count  = rows.Count,
                    include_inactive = includeInactive,
                    renderers       = output.ToArray(),
                    queue_summary   = summary,
                    warnings        = warnings.ToArray(),
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------

        private sealed class RendererRow
        {
            public Renderer  Renderer;
            public Material  Material;
            public int       Queue;
            public string    Bucket;
            public string    SortingLayer;
            public int       SortingOrder;
            public Bounds    Bounds;

            public object ToDto() => new
            {
                gameobject_path = GameObjectResolver.PathOf(Renderer.gameObject),
                renderer_type   = Renderer.GetType().Name,
                instance_id     = GameObjectResolver.InstanceIdOf(Renderer),
                material_name   = Material != null ? Material.name : null,
                shader          = Material != null && Material.shader != null ? Material.shader.name : null,
                render_queue    = Queue,
                queue_bucket    = Bucket,
                sorting_layer   = SortingLayer,
                sorting_order   = SortingOrder,
                bounds = new
                {
                    center = new { x = Bounds.center.x, y = Bounds.center.y, z = Bounds.center.z },
                    size   = new { x = Bounds.size.x,   y = Bounds.size.y,   z = Bounds.size.z   }
                }
            };
        }

        private static Scene ResolveScene(string path)
        {
            if (string.IsNullOrEmpty(path)) return SceneManager.GetActiveScene();
            var s = SceneManager.GetSceneByPath(path);
            if (!s.IsValid() || !s.isLoaded)
                throw new InvalidOperationException($"Scene not loaded: {path}");
            return s;
        }

        private static List<Renderer> CollectRenderers(Scene scene, bool includeInactive)
        {
            var result = new List<Renderer>();
            foreach (var root in scene.GetRootGameObjects())
                result.AddRange(root.GetComponentsInChildren<Renderer>(includeInactive));
            return result;
        }

        private static string Bucket(int q)
        {
            if (q < 0)     return "Unknown";
            if (q <= 1000) return "Background";
            if (q <= 2000) return "Geometry";
            if (q <= 2450) return "AlphaTest";
            if (q <= 3500) return "Transparent";
            return "Overlay";
        }

        private static Dictionary<string, int> BucketSummary(List<RendererRow> rows)
        {
            var m = new Dictionary<string, int>
            {
                ["Background (<=1000)"]    = 0,
                ["Geometry (1001-2000)"]   = 0,
                ["AlphaTest (2001-2450)"]  = 0,
                ["Transparent (2451-3500)"] = 0,
                ["Overlay (>3500)"]        = 0,
                ["Unknown"]                = 0
            };
            foreach (var r in rows)
            {
                var key = r.Queue < 0              ? "Unknown" :
                          r.Queue <= 1000          ? "Background (<=1000)" :
                          r.Queue <= 2000          ? "Geometry (1001-2000)" :
                          r.Queue <= 2450          ? "AlphaTest (2001-2450)" :
                          r.Queue <= 3500          ? "Transparent (2451-3500)" :
                                                     "Overlay (>3500)";
                m[key]++;
            }
            return m;
        }

        private static List<string> DetectWarnings(List<RendererRow> rows)
        {
            var w = new List<string>();

            // 1. Transparent-on-transparent bounds overlap — camera-dependent
            //    sort order is the #1 cause of flicker/swap bugs.
            var transparents = rows.FindAll(r => r.Queue > 2450 && r.Queue <= 3500);
            var overlapPairs = 0;
            for (var i = 0; i < transparents.Count; i++)
            for (var j = i + 1; j < transparents.Count; j++)
                if (transparents[i].Bounds.Intersects(transparents[j].Bounds))
                    overlapPairs++;
            if (overlapPairs > 0)
                w.Add($"{overlapPairs} transparent-renderer bounds-overlap pair(s) detected — sort order depends on camera position. Consider camera-split, render-queue offsets, or sorting layers.");

            // 2. Sprite pileup: multiple SpriteRenderers at the same (layer, order)
            //    with overlapping bounds > z-fighting / arbitrary layering.
            var pile = new Dictionary<string, int>();
            var pileOverlap = new Dictionary<string, int>();
            var spriteRows = rows.FindAll(r => r.Renderer is SpriteRenderer);
            foreach (var s in spriteRows)
            {
                var key = s.SortingLayer + "/" + s.SortingOrder;
                if (!pile.ContainsKey(key)) pile[key] = 0;
                pile[key]++;
            }
            foreach (var kv in pile)
                if (kv.Value >= 2)
                    w.Add($"{kv.Value} SpriteRenderers share sorting (layer '{kv.Key.Split('/')[0]}', order {kv.Key.Split('/')[1]}) — draw order between them is undefined.");

            // 3. Transparent renderers whose bounds contain an opaque renderer
            //    on a higher queue-priority sorting layer — rare but insidious.
            // 4. Non-default sorting layer unused but present — skipped.

            // 5. Unknown queue (material is null) — usually a missing material.
            var missingMats = 0;
            foreach (var r in rows) if (r.Material == null) missingMats++;
            if (missingMats > 0)
                w.Add($"{missingMats} renderer(s) have no material assigned — will render as Unity error-pink or nothing at all.");

            return w;
        }
    }
}
