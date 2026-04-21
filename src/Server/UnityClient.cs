using System.Net.Http.Json;
using System.Text.Json;
using Reify.Shared.Contracts;

namespace Reify.Server;

/// <summary>
/// Thin HTTP client against the Unity Editor's Reify bridge.
/// One instance per process; safe to hold as a singleton.
/// </summary>
public sealed class UnityClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public UnityClient()
    {
        var port = Environment.GetEnvironmentVariable("REIFY_BRIDGE_PORT") ?? "17777";
        var host = Environment.GetEnvironmentVariable("REIFY_BRIDGE_HOST") ?? "127.0.0.1";
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://{host}:{port}/"),
            Timeout    = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<T> CallAsync<T>(string tool, object? args, CancellationToken ct)
    {
        var envelope = new
        {
            tool,
            args = args is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(args, _json)
        };

        HttpResponseMessage http;
        try
        {
            http = await _http.PostAsJsonAsync("tool", envelope, _json, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ReifyBridgeException(
                "UNITY_UNREACHABLE",
                $"Unity Editor bridge not reachable at {_http.BaseAddress}. " +
                "Is the Unity Editor open with the Reify package installed? " +
                $"Underlying error: {ex.Message}"
            );
        }

        var body = await http.Content.ReadAsStringAsync(ct);
        var parsed = JsonSerializer.Deserialize<BridgeResponse<T>>(body, _json)
            ?? throw new ReifyBridgeException("BRIDGE_PROTOCOL", "Empty response body");

        if (!parsed.Ok || parsed.Data is null)
        {
            var err = parsed.Error ?? new BridgeError("UNKNOWN", "No error payload");
            throw new ReifyBridgeException(err.Code, err.Message);
        }

        return parsed.Data;
    }
}

public sealed class ReifyBridgeException(string code, string message)
    : Exception($"[{code}] {message}")
{
    public string Code { get; } = code;
}
