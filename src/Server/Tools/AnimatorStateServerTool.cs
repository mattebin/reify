using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class AnimatorStateServerTool
{
    [McpServerTool(Name = "animator-state"), Description(
        "4th Phase C philosophy tool. Full structured runtime state of an " +
        "Animator: controller + avatar, every layer with its current + next " +
        "state (name, hash, normalized_time, length, clip), active transition " +
        "info, every parameter with its live value AND default, plus warnings." +
        "\n\n" +
        "Resolve by animator_instance_id or gameobject_path (one or the " +
        "other). Works in edit mode AND play mode — edit mode returns the " +
        "initial/default snapshot; play mode returns live values. " +
        "\n\n" +
        "Warnings fire on: missing avatar (classic T-pose cause), missing " +
        "controller, states with no incoming transitions (unreachable), " +
        "states with no motion (silent), parameters never read by any " +
        "condition (dead), layer weight zero, AnimatorOverrideController " +
        "fallback. The 'why is my character stuck in T-pose' diagnostic in " +
        "a single call."
    )]
    public static async Task<JsonElement> AnimatorState(UnityClient unity,
        [Description("Animator component instance_id, OR pass gameobject_path.")]
        int? animator_instance_id,
        [Description("Scene path to a GameObject carrying an Animator.")]
        string? gameobject_path,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("animator-state",
        new AnimatorStateArgs(animator_instance_id, gameobject_path), ct);
}
