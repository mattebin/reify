using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// The "opt-in vision" philosophy tool. Renders a camera to a
    /// RenderTexture, saves a PNG alongside the project, AND returns the
    /// structured scene-state for that same frame — camera config + a
    /// viewport query of every GameObject with a Renderer whose bounds
    /// overlap the camera's frustum.
    ///
    /// The promise: screenshots exist as an escape hatch, NOT the default
    /// path. When the caller explicitly invokes this, they also get code
    /// identifiers for everything in the frame so the LLM never has to
    /// guess what it's looking at.
    /// </summary>
    internal static class StructuredScreenshotTool
    {
        [ReifyTool("structured-screenshot")]
        public static Task<object> Capture(JToken args)
        {
            var outputPath = args?.Value<string>("output_path")
                ?? "Assets/ReifyScreenshots/capture.png";
            var width      = args?.Value<int?>("width")  ?? 1280;
            var height     = args?.Value<int?>("height") ?? 720;
            var includeSceneState = args?.Value<bool?>("include_scene_state") ?? true;
            var maxRenderersInFrame = args?.Value<int?>("max_renderers_in_frame") ?? 200;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var cam = ResolveCamera(args);

                // Render the camera to an offscreen RT and pull PNG bytes.
                var prevTarget = cam.targetTexture;
                var prevActive = RenderTexture.active;
                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);

                byte[] png;
                try
                {
                    cam.targetTexture = rt;
                    cam.Render();

                    RenderTexture.active = rt;
                    var tex = new Texture2D(width, height, TextureFormat.RGB24, mipChain: false);
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();
                    png = tex.EncodeToPNG();
                    UnityEngine.Object.DestroyImmediate(tex);
                }
                finally
                {
                    cam.targetTexture = prevTarget;
                    RenderTexture.active = prevActive;
                    UnityEngine.Object.DestroyImmediate(rt);
                }

                // Save to disk — under Assets/ so AssetDatabase picks it up.
                if (!outputPath.StartsWith("Assets/", StringComparison.Ordinal))
                    throw new ArgumentException($"output_path must start with 'Assets/': {outputPath}");
                var dir = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                {
                    var parts = dir.Split('/');
                    var accum = parts[0];
                    for (var i = 1; i < parts.Length; i++)
                    {
                        var next = accum + "/" + parts[i];
                        if (!AssetDatabase.IsValidFolder(next))
                            AssetDatabase.CreateFolder(accum, parts[i]);
                        accum = next;
                    }
                }
                File.WriteAllBytes(outputPath, png);
                AssetDatabase.ImportAsset(outputPath);

                object sceneState = null;
                if (includeSceneState)
                {
                    // Find every Renderer whose bounds intersect the camera
                    // frustum. This is the "what's actually in the frame"
                    // structured answer the screenshot alone doesn't give.
                    var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                    #pragma warning disable CS0618
                    var allRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
                    #pragma warning restore CS0618

                    var visible = new List<object>();
                    var truncated = false;
                    for (var i = 0; i < allRenderers.Length; i++)
                    {
                        var r = allRenderers[i];
                        if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                        if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;

                        if (visible.Count >= maxRenderersInFrame) { truncated = true; break; }

                        var mat = r.sharedMaterial;
                        visible.Add(new
                        {
                            gameobject_path = GameObjectResolver.PathOf(r.gameObject),
                            gameobject_instance_id = GameObjectResolver.InstanceIdOf(r.gameObject),
                            renderer_type   = r.GetType().Name,
                            material_name   = mat != null ? mat.name : null,
                            material_path   = mat != null ? AssetDatabase.GetAssetPath(mat) : null,
                            bounds_center   = new { x = r.bounds.center.x, y = r.bounds.center.y, z = r.bounds.center.z },
                            bounds_size     = new { x = r.bounds.size.x,   y = r.bounds.size.y,   z = r.bounds.size.z   },
                            sorting_layer   = r.sortingLayerName,
                            sorting_order   = r.sortingOrder
                        });
                    }

                    sceneState = new
                    {
                        camera = new
                        {
                            instance_id    = GameObjectResolver.InstanceIdOf(cam),
                            gameobject_path = GameObjectResolver.PathOf(cam.gameObject),
                            world_position = new { x = cam.transform.position.x, y = cam.transform.position.y, z = cam.transform.position.z },
                            world_forward  = new { x = cam.transform.forward.x,  y = cam.transform.forward.y,  z = cam.transform.forward.z  },
                            fov            = cam.fieldOfView,
                            orthographic   = cam.orthographic,
                            near_clip      = cam.nearClipPlane,
                            far_clip       = cam.farClipPlane
                        },
                        visible_renderer_count = visible.Count,
                        visible_renderers      = visible.ToArray(),
                        truncated
                    };
                }

                return new
                {
                    screenshot = new
                    {
                        output_path  = outputPath,
                        absolute_path = Path.GetFullPath(outputPath),
                        width,
                        height,
                        byte_count   = png.Length
                    },
                    scene_state = sceneState,
                    philosophy_note = "This tool is an opt-in escape hatch. Prefer structured-state reads (scene-query, material-inspect, render-queue-audit, etc.) when possible — they're faster, cheaper, and diff cleanly across frames.",
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        private static Camera ResolveCamera(JToken args)
        {
            var instanceId = args?["camera_instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("camera_instance_id") : null;
            var goPath     = args?.Value<string>("camera_gameobject_path");

            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                return obj as Camera
                    ?? (obj as GameObject)?.GetComponent<Camera>()
                    ?? throw new InvalidOperationException(
                        $"instance_id {instanceId} does not resolve to a Camera or a GameObject with one.");
            }
            if (!string.IsNullOrEmpty(goPath))
            {
                var go = GameObjectResolver.ByPath(goPath)
                    ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
                return go.GetComponent<Camera>()
                    ?? throw new InvalidOperationException($"GameObject '{goPath}' has no Camera component.");
            }
            return Camera.main
                ?? throw new InvalidOperationException(
                    "No Camera.main and no camera_instance_id / camera_gameobject_path provided.");
        }
    }
}
