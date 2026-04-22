using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Intrinsic (unscaled, at localScale=1) dimensions of every
    /// GameObject.CreatePrimitive type. Hardcoded constants — these have
    /// been stable since Unity 4 and won't drift.
    ///
    /// Without this, callers have to Google "what are the default
    /// dimensions of Unity's Sphere primitive". The answer is now on the
    /// receipt.
    /// </summary>
    internal static class PrimitiveDefaults
    {
        internal static object For(string primitive)
        {
            if (string.IsNullOrEmpty(primitive)) return null;
            switch (primitive.ToLowerInvariant())
            {
                case "cube":
                    return new
                    {
                        kind          = "Cube",
                        size          = new { x = 1f, y = 1f, z = 1f },
                        half_extents  = new { x = 0.5f, y = 0.5f, z = 0.5f },
                        note          = "1m x 1m x 1m axis-aligned box centred at origin."
                    };
                case "sphere":
                    return new
                    {
                        kind          = "Sphere",
                        radius        = 0.5f,
                        diameter      = 1f,
                        note          = "Unit sphere, centred at origin. scale=1 gives 1m diameter."
                    };
                case "capsule":
                    return new
                    {
                        kind          = "Capsule",
                        height        = 2f,
                        radius        = 0.5f,
                        axis          = "Y",
                        note          = "2m tall (axis Y), 0.5m radius, centred at origin. " +
                                        "scale.y compresses total height including caps."
                    };
                case "cylinder":
                    return new
                    {
                        kind          = "Cylinder",
                        height        = 2f,
                        radius        = 0.5f,
                        axis          = "Y",
                        note          = "2m tall, 0.5m radius. Flat-capped — unlike Capsule."
                    };
                case "plane":
                    return new
                    {
                        kind          = "Plane",
                        size          = new { x = 10f, y = 0f, z = 10f },
                        up_axis       = "Y",
                        note          = "10m x 10m flat quad in the XZ plane, centred at origin, " +
                                        "facing +Y. Single-sided — invisible from below."
                    };
                case "quad":
                    return new
                    {
                        kind          = "Quad",
                        size          = new { x = 1f, y = 1f, z = 0f },
                        up_axis       = "Z",
                        note          = "1m x 1m flat quad in the XY plane, centred at origin, " +
                                        "facing -Z (camera-ward). Single-sided."
                    };
                default:
                    return null;
            }
        }

        [ReifyTool("primitive-defaults")]
        public static Task<object> Inspect(JToken args)
        {
            var primitive = args?.Value<string>("primitive");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (string.IsNullOrEmpty(primitive))
                {
                    // Dump the whole table.
                    return new
                    {
                        primitives = new[]
                        {
                            For("Cube"), For("Sphere"), For("Capsule"),
                            For("Cylinder"), For("Plane"), For("Quad")
                        },
                        read_at_utc = DateTime.UtcNow.ToString("o"),
                        frame       = (long)Time.frameCount
                    };
                }

                var dims = For(primitive)
                    ?? throw new ArgumentException(
                        $"Unknown primitive '{primitive}'. Valid: Cube, Sphere, Capsule, " +
                        "Cylinder, Plane, Quad.");

                return new
                {
                    primitive   = dims,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }
    }
}
