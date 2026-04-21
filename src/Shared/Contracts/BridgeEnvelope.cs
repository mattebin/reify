using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record BridgeRequest(
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("args")] System.Text.Json.JsonElement? Args
);

public sealed record BridgeError(
    [property: JsonPropertyName("code")]    string Code,
    [property: JsonPropertyName("message")] string Message
);

public sealed record BridgeResponse<T>(
    [property: JsonPropertyName("ok")]    bool Ok,
    [property: JsonPropertyName("data")]  T? Data,
    [property: JsonPropertyName("error")] BridgeError? Error
);
