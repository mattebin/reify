using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class DomainExtensionServerTools
{
    // ---------------- Addressables ----------------
    [McpServerTool(Name = "addressables-settings"), Description(
        "Inspect AddressableAssetSettings: group_count, label_count, " +
        "active_profile_id, remote-catalog flags, settings asset path. " +
        "Package-gated on com.unity.addressables — structured error " +
        "when not installed.")]
    public static async Task<JsonElement> AddressablesSettings(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("addressables-settings", null, ct);

    [McpServerTool(Name = "addressables-group-list"), Description(
        "List every AddressableAssetGroup: name, guid, read_only, " +
        "is_default_group, entry_count, schema_types.")]
    public static async Task<JsonElement> AddressablesGroupList(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("addressables-group-list", null, ct);

    [McpServerTool(Name = "addressables-entry-list"), Description(
        "List addressable entries, optionally filtered by group_name " +
        "and/or label. Each entry: address, guid, asset_path, " +
        "main_asset_type, group_name, labels[], is_sub_asset. " +
        "limit default 500 with truncated flag.")]
    public static async Task<JsonElement> AddressablesEntryList(
        UnityClient unity,
        string? group_name = null,
        string? label = null,
        int? limit = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("addressables-entry-list", new
    {
        group_name, label, limit
    }, ct);

    [McpServerTool(Name = "addressables-entry-set"), Description(
        "Mark an asset as addressable (or remove the addressable entry " +
        "when make_addressable=false). Optional address, group_name, " +
        "label. Returns ADR-002 applied_fields with before/after for " +
        "is_addressable/address/group.")]
    public static async Task<JsonElement> AddressablesEntrySet(
        UnityClient unity,
        string asset_path,
        bool? make_addressable = null,
        string? address = null,
        string? group_name = null,
        string? label = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("addressables-entry-set", new
    {
        asset_path, make_addressable, address, group_name, label
    }, ct);

    [McpServerTool(Name = "addressables-build-job"), Description(
        "Queue an Addressables content build via the shared ReifyJobs " +
        "infrastructure. Returns a job_id immediately; poll job-status " +
        "/ job-result with the id until terminal.")]
    public static async Task<JsonElement> AddressablesBuildJob(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("addressables-build-job", null, ct);

    // ---------------- Cinemachine ----------------
    [McpServerTool(Name = "cinemachine-brain-inspect"), Description(
        "Inspect CinemachineBrain: active virtual camera, blend state, " +
        "default blend time, world up, debug flags. Works on both " +
        "Cinemachine 2.x (Cinemachine.*) and 3.x (Unity.Cinemachine.*) " +
        "via reflection. Package-gated on com.unity.cinemachine.")]
    public static async Task<JsonElement> CinemachineBrainInspect(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("cinemachine-brain-inspect", new
    {
        instance_id, gameobject_path
    }, ct);

    [McpServerTool(Name = "cinemachine-vcam-inspect"), Description(
        "Inspect a Cinemachine virtual camera: priority, follow/lookat " +
        "targets, lens (FOV, clip planes, ortho size, dutch). Returns " +
        "vcam_type_fqn so caller can distinguish CinemachineCamera " +
        "(3.x) from CinemachineVirtualCamera (2.x).")]
    public static async Task<JsonElement> CinemachineVCamInspect(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("cinemachine-vcam-inspect", new
    {
        instance_id, gameobject_path
    }, ct);

    [McpServerTool(Name = "cinemachine-vcam-set-priority"), Description(
        "Set a virtual camera's Priority. Undo-backed. Returns ADR-002 " +
        "applied_fields with before/after priority values.")]
    public static async Task<JsonElement> CinemachineVCamSetPriority(
        UnityClient unity,
        int priority,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("cinemachine-vcam-set-priority", new
    {
        instance_id, gameobject_path, priority
    }, ct);

    // ---------------- Timeline ----------------
    [McpServerTool(Name = "timeline-asset-inspect"), Description(
        "Inspect a .playable TimelineAsset: duration, frame rate, " +
        "track_count, each track's name/type/muted/clip_count. " +
        "Package-gated on com.unity.timeline.")]
    public static async Task<JsonElement> TimelineAssetInspect(
        UnityClient unity,
        string asset_path,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("timeline-asset-inspect", new { asset_path }, ct);

    [McpServerTool(Name = "timeline-director-inspect"), Description(
        "Inspect a PlayableDirector: state (Playing/Paused/Stopped), " +
        "bound asset path + guid, current time, duration, " +
        "extrapolation_mode, play_on_awake.")]
    public static async Task<JsonElement> TimelineDirectorInspect(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("timeline-director-inspect", new
    {
        instance_id, gameobject_path
    }, ct);

    [McpServerTool(Name = "timeline-director-play"), Description(
        "Call PlayableDirector.Play(). ADR-002 receipt returns " +
        "{state, time_seconds} before/after.")]
    public static async Task<JsonElement> TimelineDirectorPlay(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("timeline-director-play", new
    {
        instance_id, gameobject_path
    }, ct);

    [McpServerTool(Name = "timeline-director-pause"), Description(
        "Call PlayableDirector.Pause(). ADR-002 before/after.")]
    public static async Task<JsonElement> TimelineDirectorPause(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("timeline-director-pause", new
    {
        instance_id, gameobject_path
    }, ct);

    [McpServerTool(Name = "timeline-director-stop"), Description(
        "Call PlayableDirector.Stop(). ADR-002 before/after.")]
    public static async Task<JsonElement> TimelineDirectorStop(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("timeline-director-stop", new
    {
        instance_id, gameobject_path
    }, ct);

    [McpServerTool(Name = "timeline-director-set-time"), Description(
        "Scrub the PlayableDirector to a specific time_seconds. " +
        "Returns ADR-002 applied_fields with before/after.")]
    public static async Task<JsonElement> TimelineDirectorSetTime(
        UnityClient unity,
        double time_seconds,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("timeline-director-set-time", new
    {
        instance_id, gameobject_path, time_seconds
    }, ct);

    // ---------------- MPPM ----------------
    [McpServerTool(Name = "mppm-status"), Description(
        "Report whether Multiplayer Play Mode (com.unity.multiplayer.playmode) " +
        "is installed + filesystem fallback listing VP_* directories " +
        "under Library/. Safe to call even when the package is absent.")]
    public static async Task<JsonElement> MppmStatus(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("mppm-status", null, ct);

    [McpServerTool(Name = "mppm-clone-list"), Description(
        "List Virtual Players / clones via the MPPM reflection surface. " +
        "Package-gated. Reports api_type_fqn so drift across MPPM " +
        "versions is diagnosable.")]
    public static async Task<JsonElement> MppmCloneList(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("mppm-clone-list", null, ct);
}
