using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record RenderQueueAuditFilter(
    [property: JsonPropertyName("queue_min")]     int? QueueMin,
    [property: JsonPropertyName("queue_max")]     int? QueueMax,
    [property: JsonPropertyName("renderer_type")] string? RendererType
);

public sealed record RenderQueueAuditArgs(
    [property: JsonPropertyName("scene_path")]       string? ScenePath,
    [property: JsonPropertyName("include_inactive")] bool? IncludeInactive,
    [property: JsonPropertyName("filter")]           RenderQueueAuditFilter? Filter
);
