using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class DomainReloadStatusServerTool
{
    [McpServerTool(Name = "domain-reload-status"), Description(
        "9th Phase C philosophy tool. 'Is Unity ready for another tool call " +
        "right now?' Reports is_compiling, is_updating, is_playing, is_paused, " +
        "is_transitioning_play_mode, application_state (edit/enter_play/play/" +
        "exit_play/pause), has_focus, plus an aggregate is_busy flag — the " +
        "one boolean an LLM should gate on before high-stakes operations. " +
        "\n\n" +
        "Tracks last_compile_started_utc, last_compile_finished_utc, and " +
        "last_domain_reload_utc via SessionState (survives domain reloads). " +
        "Warnings flag: active compile, active update, play-mode transition, " +
        "recent reload (< 2s — invalidates cached instance_ids).")]
    public static async Task<JsonElement> DomainReloadStatus(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("domain-reload-status", null, ct);
}
