using System;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Cinemachine surface via reflection — package-gated on
    /// `com.unity.cinemachine`. Covers the brain, virtual cameras, and
    /// priority/lens writes. Cinemachine 2.x and 3.x have different
    /// namespaces (Cinemachine.* vs Unity.Cinemachine.*); reflection
    /// handles both.
    /// </summary>
    internal static class CinemachineTools
    {
        private static Type FindType(params string[] candidates)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                foreach (var name in candidates)
                {
                    Type t = null;
                    try { t = asm.GetType(name, false); } catch { }
                    if (t != null) return t;
                }
            return null;
        }

        private static Type BrainType() => FindType(
            "Unity.Cinemachine.CinemachineBrain",  // 3.x
            "Cinemachine.CinemachineBrain");       // 2.x

        private static Type VCamType() => FindType(
            "Unity.Cinemachine.CinemachineCamera",         // 3.x
            "Unity.Cinemachine.CinemachineVirtualCamera",  // fallback
            "Cinemachine.CinemachineVirtualCameraBase",
            "Cinemachine.CinemachineVirtualCamera");       // 2.x

        // ---------- cinemachine-brain-inspect ----------
        [ReifyTool("cinemachine-brain-inspect")]
        public static Task<object> BrainInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var brainT = BrainType()
                    ?? throw new InvalidOperationException(
                        "CinemachineBrain not found — install `com.unity.cinemachine` to use this tool.");

                var go = ResolveGameObject(args);
                var brain = go.GetComponent(brainT)
                    ?? throw new InvalidOperationException(
                        $"GameObject '{GameObjectResolver.PathOf(go)}' has no CinemachineBrain.");

                var active = brainT.GetProperty("ActiveVirtualCamera")?.GetValue(brain);
                var blend  = brainT.GetProperty("ActiveBlend")?.GetValue(brain);
                var activeName = ActiveVCamName(active);

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(brain as UnityEngine.Object),
                    gameobject_path        = GameObjectResolver.PathOf(go),
                    brain_type_fqn         = brainT.FullName,
                    active_virtual_camera  = activeName,
                    is_blending            = blend != null,
                    blend_type_fqn         = blend?.GetType().FullName,
                    default_blend_time     = (float?)(brainT.GetProperty("DefaultBlend")?.GetValue(brain) is object db
                                             ? db.GetType().GetField("m_Time")?.GetValue(db) : null),
                    show_debug_text        = (bool?)(brainT.GetProperty("ShowDebugText")?.GetValue(brain)),
                    world_up               = VecDto(brainT.GetProperty("DefaultWorldUp")?.GetValue(brain)),
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- cinemachine-vcam-inspect ----------
        [ReifyTool("cinemachine-vcam-inspect")]
        public static Task<object> VCamInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var vcamT = VCamType()
                    ?? throw new InvalidOperationException(
                        "Cinemachine VCam type not found — install `com.unity.cinemachine`.");

                var go = ResolveGameObject(args);
                var vcam = go.GetComponent(vcamT)
                    ?? throw new InvalidOperationException(
                        $"GameObject '{GameObjectResolver.PathOf(go)}' has no CinemachineCamera/CinemachineVirtualCamera.");

                var t = vcam.GetType();
                var lens = t.GetProperty("Lens")?.GetValue(vcam)
                        ?? t.GetField("m_Lens", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(vcam);
                var follow = t.GetProperty("Follow")?.GetValue(vcam) as Transform;
                var lookAt = t.GetProperty("LookAt")?.GetValue(vcam) as Transform;

                return new
                {
                    instance_id       = GameObjectResolver.InstanceIdOf(vcam as UnityEngine.Object),
                    gameobject_path   = GameObjectResolver.PathOf(go),
                    vcam_type_fqn     = t.FullName,
                    priority          = (int?)(t.GetProperty("Priority")?.GetValue(vcam))
                                      ?? (int?)(t.GetField("m_Priority", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(vcam)),
                    enabled           = (vcam as Behaviour)?.enabled,
                    follow_target     = follow != null ? GameObjectResolver.PathOf(follow.gameObject) : null,
                    lookat_target     = lookAt != null ? GameObjectResolver.PathOf(lookAt.gameObject) : null,
                    lens              = LensDto(lens),
                    read_at_utc       = DateTime.UtcNow.ToString("o"),
                    frame             = (long)Time.frameCount
                };
            });
        }

        // ---------- cinemachine-vcam-set-priority ----------
        [ReifyTool("cinemachine-vcam-set-priority")]
        public static Task<object> VCamSetPriority(JToken args)
        {
            var newPriority = args?.Value<int?>("priority")
                ?? throw new ArgumentException("priority (int) is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var vcamT = VCamType()
                    ?? throw new InvalidOperationException(
                        "Cinemachine VCam type not found — install `com.unity.cinemachine`.");
                var go = ResolveGameObject(args);
                var vcam = go.GetComponent(vcamT)
                    ?? throw new InvalidOperationException(
                        $"GameObject '{GameObjectResolver.PathOf(go)}' has no virtual camera.");
                var t = vcam.GetType();

                var prop  = t.GetProperty("Priority", BindingFlags.Instance | BindingFlags.Public);
                var field = t.GetField("m_Priority", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                int? before = (int?)(prop?.GetValue(vcam) ?? field?.GetValue(vcam));

                UnityEditor.Undo.RecordObject(vcam as UnityEngine.Object, "Reify: set Cinemachine priority");
                if (prop != null && prop.CanWrite) prop.SetValue(vcam, newPriority);
                else if (field != null) field.SetValue(vcam, newPriority);
                else throw new InvalidOperationException("Could not find writable Priority on vcam.");

                int? after = (int?)(prop?.GetValue(vcam) ?? field?.GetValue(vcam));

                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(vcam as UnityEngine.Object),
                    gameobject_path = GameObjectResolver.PathOf(go),
                    applied_fields  = new object[]
                    {
                        new { field = "priority", before = before, after = after }
                    },
                    applied_count   = 1,
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static string ActiveVCamName(object active)
        {
            if (active == null) return null;
            // ICinemachineCamera has a 'Name' or 'Priority/VirtualCameraGameObject'
            var t = active.GetType();
            var name = t.GetProperty("Name")?.GetValue(active) as string;
            if (!string.IsNullOrEmpty(name)) return name;
            var vgo = t.GetProperty("VirtualCameraGameObject")?.GetValue(active) as GameObject;
            return vgo != null ? vgo.name : null;
        }

        private static object LensDto(object lens)
        {
            if (lens == null) return null;
            var t = lens.GetType();
            float? F(string n) => (float?)t.GetField(n)?.GetValue(lens)
                              ?? (float?)t.GetProperty(n)?.GetValue(lens);
            return new
            {
                field_of_view   = F("FieldOfView") ?? F("m_FieldOfView") ?? F("fieldOfView"),
                near_clip_plane = F("NearClipPlane") ?? F("m_NearClipPlane"),
                far_clip_plane  = F("FarClipPlane") ?? F("m_FarClipPlane"),
                orthographic_size = F("OrthographicSize") ?? F("m_OrthographicSize"),
                dutch           = F("Dutch") ?? F("m_Dutch"),
                lens_type_fqn   = t.FullName
            };
        }

        private static object VecDto(object v)
        {
            if (v is Vector3 v3) return new { x = v3.x, y = v3.y, z = v3.z };
            return null;
        }

        private static GameObject ResolveGameObject(JToken args)
        {
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var goPath = args?.Value<string>("gameobject_path") ?? args?.Value<string>("path");
            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                if (obj is GameObject g) return g;
                if (obj is Component c) return c.gameObject;
                throw new InvalidOperationException($"instance_id {instanceId} is not a GameObject/Component.");
            }
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("Provide instance_id or gameobject_path.");
            return GameObjectResolver.ByPath(goPath)
                ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
        }
    }
}
