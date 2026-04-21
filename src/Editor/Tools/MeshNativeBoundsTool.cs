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
    /// First Phase C philosophy tool. Exposes a mesh's native (unscaled)
    /// geometric state so an LLM never has to guess scale from a screenshot.
    /// Reads from either an asset path, a scene GameObject path, or an
    /// instance id — exactly one of the three.
    /// </summary>
    internal static class MeshNativeBoundsTool
    {
        // Heuristic thresholds for the "warnings" field. These are the pains
        // that motivated the tool (Plane primitive at native 10 m vs an FBX
        // at native 0.02 m both burn an LLM — explicit warnings are the fix).
        private const float SmallMeshThresholdMeters = 0.1f;
        private const float LargeMeshThresholdMeters = 100f;

        public static Task<object> Handle(JToken args)
        {
            var assetPath      = args?.Value<string>("asset_path");
            var gameObjectPath = args?.Value<string>("gameobject_path");
            var instanceIdTok  = args?["instance_id"];
            int? instanceId    = instanceIdTok != null && instanceIdTok.Type != JTokenType.Null
                ? instanceIdTok.Value<int>()
                : (int?)null;

            var provided = 0;
            if (!string.IsNullOrEmpty(assetPath))      provided++;
            if (!string.IsNullOrEmpty(gameObjectPath)) provided++;
            if (instanceId.HasValue)                   provided++;
            if (provided != 1)
                throw new ArgumentException(
                    "Exactly one of asset_path, gameobject_path, or instance_id must be provided.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (!string.IsNullOrEmpty(assetPath))
                    return ForAsset(assetPath);
                if (!string.IsNullOrEmpty(gameObjectPath))
                    return ForGameObject(ResolveGameObjectByPath(gameObjectPath), gameObjectPath);
                return ForInstanceId(instanceId!.Value);
            });
        }

        // ---------- source resolvers ----------

        private static object ForInstanceId(int instanceId)
        {
            var obj = EditorUtility.InstanceIDToObject(instanceId)
                ?? throw new InvalidOperationException($"No object with instance_id {instanceId}");

            return obj switch
            {
                Mesh mesh            => BuildForMesh(mesh, "asset", AssetDatabase.GetAssetPath(mesh),
                                                    renderer: null, transform: null, importer: null),
                GameObject go        => ForGameObject(go, identifier: instanceId.ToString()),
                _                    => throw new InvalidOperationException(
                                          $"instance_id {instanceId} resolves to {obj.GetType().Name}, expected Mesh or GameObject.")
            };
        }

        private static object ForAsset(string assetPath)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (mesh != null)
                return BuildForMesh(
                    mesh,
                    "asset", assetPath,
                    renderer: null,
                    transform: null,
                    importer: AssetImporter.GetAtPath(assetPath) as ModelImporter);

            // FBX / OBJ / similar — loads as a GameObject prefab asset with
            // sub-asset meshes under it. Take the first MeshFilter in the
            // hierarchy; document the choice as a warning if ambiguous.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                throw new InvalidOperationException(
                    $"No Mesh or GameObject asset at {assetPath}. (Check path, extension, and that the asset finished importing.)");

            var mf = prefab.GetComponentInChildren<MeshFilter>();
            var smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
            var sharedMesh = mf != null ? mf.sharedMesh : (smr != null ? smr.sharedMesh : null);
            if (sharedMesh == null)
                throw new InvalidOperationException(
                    $"Asset {assetPath} has no MeshFilter or SkinnedMeshRenderer with a mesh.");

            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            return BuildForMesh(
                sharedMesh,
                "asset", assetPath,
                renderer: mf != null ? mf.GetComponent<Renderer>() : smr,
                transform: null, // asset-side bounds are native only; effective bounds need a scene instance
                importer: importer);
        }

        private static object ForGameObject(GameObject go, string identifier)
        {
            if (go == null)
                throw new InvalidOperationException($"GameObject not found: {identifier}");

            // Priority: MeshFilter.sharedMesh > SkinnedMeshRenderer.sharedMesh.
            // Deliberately don't walk children — keep the tool's scope to
            // the single GameObject requested. Callers that need a child
            // mesh should pass the child's path directly.
            var mf = go.GetComponent<MeshFilter>();
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            Mesh mesh = null;
            Renderer renderer = null;

            if (mf != null && mf.sharedMesh != null)
            {
                mesh = mf.sharedMesh;
                renderer = go.GetComponent<MeshRenderer>();
            }
            else if (smr != null && smr.sharedMesh != null)
            {
                mesh = smr.sharedMesh;
                renderer = smr;
            }

            if (mesh == null)
                throw new InvalidOperationException(
                    $"GameObject '{identifier}' has no MeshFilter or SkinnedMeshRenderer with a mesh.");

            var assetPath = AssetDatabase.GetAssetPath(mesh);
            var importer  = string.IsNullOrEmpty(assetPath) ? null : AssetImporter.GetAtPath(assetPath) as ModelImporter;

            return BuildForMesh(
                mesh,
                "gameobject", identifier,
                renderer: renderer,
                transform: go.transform,
                importer: importer);
        }

        // ---------- core builder ----------

        private static object BuildForMesh(
            Mesh mesh,
            string sourceType,
            string sourceIdentifier,
            Renderer renderer,
            Transform transform,
            ModelImporter importer)
        {
            var native = mesh.bounds;
            var warnings = new List<string>();

            var maxExtent = Mathf.Max(native.size.x, Mathf.Max(native.size.y, native.size.z));
            if (maxExtent < SmallMeshThresholdMeters)
                warnings.Add(
                    $"Native mesh size {maxExtent:F4}m is below {SmallMeshThresholdMeters}m threshold — " +
                    "verify import scale factor or plan for a 30–50× placement multiplier.");
            if (maxExtent > LargeMeshThresholdMeters)
                warnings.Add(
                    $"Native mesh size {maxExtent:F1}m exceeds {LargeMeshThresholdMeters}m threshold — " +
                    "verify it's not unit-mismatched (e.g., centimetres interpreted as metres).");

            if (!mesh.isReadable)
                warnings.Add(
                    "Mesh is not marked Read/Write Enabled — runtime vertex/index queries will fail. " +
                    "Editor-time bounds still authoritative.");

            if (importer != null && !Mathf.Approximately(importer.globalScale, 1f))
                warnings.Add(
                    $"ModelImporter globalScale = {importer.globalScale} (not 1.0) — native bounds above " +
                    "reflect the imported mesh AFTER scaling; upstream source file may differ.");

            if (renderer != null && transform != null)
            {
                // Sanity-check agreement between renderer.bounds and mesh.bounds × lossyScale.
                var predicted = Vector3.Scale(native.size, transform.lossyScale);
                var actual    = renderer.bounds.size;
                var delta     = (predicted - actual).magnitude;
                if (delta > 0.001f && actual.magnitude > 0.001f)
                    warnings.Add(
                        $"renderer.bounds disagrees with mesh.bounds × lossyScale by {delta:F4}m — " +
                        "likely a MaterialPropertyBlock-driven shader that inflates bounds, or a submesh override.");
            }

            SubmeshBoundsDto[] submeshes = null;
            if (mesh.subMeshCount > 1)
            {
                submeshes = new SubmeshBoundsDto[mesh.subMeshCount];
                for (var i = 0; i < mesh.subMeshCount; i++)
                {
                    var sm = mesh.GetSubMesh(i);
                    submeshes[i] = new SubmeshBoundsDto { index = i, bounds = ToBounds(sm.bounds) };
                }
            }

            var response = new
            {
                source = new { type = sourceType, identifier = sourceIdentifier },
                mesh_name       = string.IsNullOrEmpty(mesh.name) ? "<unnamed>" : mesh.name,
                submesh_count   = mesh.subMeshCount,
                native_bounds   = ToBounds(native),
                effective_bounds = renderer != null && transform != null ? ToBounds(renderer.bounds) : (object)null,
                transform       = transform != null ? new
                {
                    local_scale    = ToVec3(transform.localScale),
                    lossy_scale    = ToVec3(transform.lossyScale),
                    world_position = ToVec3(transform.position)
                } : (object)null,
                submeshes       = submeshes,
                vertex_count    = mesh.vertexCount,
                triangle_count  = mesh.triangles != null ? mesh.triangles.Length / 3 : 0,
                has_normals     = mesh.normals != null && mesh.normals.Length > 0,
                has_uvs         = mesh.uv != null && mesh.uv.Length > 0,
                has_colors      = mesh.colors != null && mesh.colors.Length > 0,
                has_skinning    = mesh.boneWeights != null && mesh.boneWeights.Length > 0,
                is_readable     = mesh.isReadable,
                import_scale_factor = importer != null ? (float?)importer.globalScale : null,
                import_global_scale = importer != null ? (float?)importer.globalScale : null,
                warnings        = warnings.ToArray(),
                read_at_utc     = DateTime.UtcNow.ToString("o"),
                frame           = (long)Time.frameCount
            };

            return response;
        }

        // ---------- helpers ----------

        private static GameObject ResolveGameObjectByPath(string path)
        {
            // GameObject.Find accepts "/Root/Child/Grandchild" or "Root/Child/Grandchild".
            var direct = GameObject.Find(path);
            if (direct != null) return direct;

            // Fall back: scan root objects by first segment, then walk.
            var slashed = path.TrimStart('/');
            var segments = slashed.Split('/');
            foreach (var scene in GetLoadedScenes())
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != segments[0]) continue;
                    var current = root.transform;
                    var matched = true;
                    for (var i = 1; i < segments.Length && matched; i++)
                    {
                        var child = current.Find(segments[i]);
                        if (child == null) { matched = false; break; }
                        current = child;
                    }
                    if (matched) return current.gameObject;
                }
            }
            return null;
        }

        private static IEnumerable<UnityEngine.SceneManagement.Scene> GetLoadedScenes()
        {
            var mgr = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (var i = 0; i < mgr; i++)
                yield return UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
        }

        private static object ToBounds(Bounds b) => new
        {
            center  = ToVec3(b.center),
            size    = ToVec3(b.size),
            extents = ToVec3(b.extents),
            min     = ToVec3(b.min),
            max     = ToVec3(b.max)
        };

        private static object ToVec3(Vector3 v) => new { x = v.x, y = v.y, z = v.z };

        private struct SubmeshBoundsDto
        {
            public int index;
            public object bounds;
        }
    }
}
