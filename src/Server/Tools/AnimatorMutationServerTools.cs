using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class AnimatorMutationServerTools
{
    [McpServerTool(Name = "animator-parameter-set"), Description(
        "Set an Animator parameter by name. Resolve via animator_instance_id " +
        "or gameobject_path. value is auto-coerced to the parameter's type " +
        "(Bool/Int/Float/Trigger). For Trigger, pass true to fire, false to " +
        "reset. Returns {before, after, type} for verification. If the " +
        "parameter name is unknown, the error lists every valid parameter " +
        "name:type so the caller can self-correct.")]
    public static async Task<JsonElement> AnimatorParameterSet(
        UnityClient unity,
        string parameter_name,
        JsonElement value,
        int? animator_instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("animator-parameter-set", new
    {
        animator_instance_id, gameobject_path, parameter_name, value
    }, ct);

    [McpServerTool(Name = "animator-crossfade"), Description(
        "Trigger Animator.CrossFade to a state by name. transition_duration " +
        "default 0.25s. layer default 0. Fails structurally if the Animator " +
        "has no controller or the layer is out of range.")]
    public static async Task<JsonElement> AnimatorCrossFade(
        UnityClient unity,
        string state_name,
        int? animator_instance_id = null,
        string? gameobject_path = null,
        float? transition_duration = null,
        int? layer = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("animator-crossfade", new
    {
        animator_instance_id, gameobject_path, state_name, transition_duration, layer
    }, ct);

    [McpServerTool(Name = "animator-play"), Description(
        "Force-play a state via Animator.Play. layer default 0. " +
        "normalized_time default 0. Unlike animator-crossfade this skips " +
        "the transition entirely.")]
    public static async Task<JsonElement> AnimatorPlay(
        UnityClient unity,
        string state_name,
        int? animator_instance_id = null,
        string? gameobject_path = null,
        int? layer = null,
        float? normalized_time = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("animator-play", new
    {
        animator_instance_id, gameobject_path, state_name, layer, normalized_time
    }, ct);
}
