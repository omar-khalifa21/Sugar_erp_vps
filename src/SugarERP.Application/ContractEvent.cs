using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SugarERP.Application;

public sealed record ContractEvent(
    Guid Id,
    int DeviceSequence,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAtUtc,
    string PayloadJson,
    string DependenciesJson,
    string ContentHash);

public static class ContractEventFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static ContractEvent Create(
        Guid id,
        int deviceSequence,
        string eventType,
        DateTimeOffset occurredAtUtc,
        object payload,
        IReadOnlyList<Guid>? dependencies = null)
    {
        var occurredAt = occurredAtUtc.ToUniversalTime().ToString("O");
        var payloadNode = JsonSerializer.SerializeToNode(payload, JsonOptions) ?? new JsonObject();
        var dependenciesNode = new JsonArray((dependencies ?? []).Select(value => JsonValue.Create(value.ToString())).ToArray());
        var hash = ComputeHash(id, deviceSequence, eventType, 1, occurredAt, payloadNode, dependencies ?? []);
        return new ContractEvent(
            id,
            deviceSequence,
            eventType,
            1,
            occurredAtUtc.ToUniversalTime(),
            payloadNode.ToJsonString(JsonOptions),
            dependenciesNode.ToJsonString(JsonOptions),
            hash);
    }

    public static string ComputeHash(
        Guid id,
        int deviceSequence,
        string eventType,
        int schemaVersion,
        string occurredAt,
        JsonElement payload,
        IReadOnlyList<Guid>? dependencies = null) =>
        ComputeHash(
            id,
            deviceSequence,
            eventType,
            schemaVersion,
            occurredAt,
            JsonNode.Parse(payload.GetRawText()) ?? new JsonObject(),
            dependencies ?? []);

    private static string ComputeHash(
        Guid id,
        int deviceSequence,
        string eventType,
        int schemaVersion,
        string occurredAt,
        JsonNode payload,
        IReadOnlyList<Guid> dependencies)
    {
        var root = new JsonObject
        {
            ["id"] = id.ToString(),
            ["device_sequence"] = deviceSequence,
            ["event_type"] = eventType,
            ["schema_version"] = schemaVersion,
            ["occurred_at"] = occurredAt,
            ["payload"] = payload.DeepClone(),
            ["dependencies"] = new JsonArray(dependencies.Select(value => JsonValue.Create(value.ToString())).ToArray())
        };
        var canonical = Canonicalize(root);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Canonicalize(JsonNode? node)
    {
        return node switch
        {
            null => "null",
            JsonArray array => $"[{string.Join(',', array.Select(Canonicalize))}]",
            JsonObject obj => $"{{{string.Join(',', obj.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => $"{JsonSerializer.Serialize(entry.Key, JsonOptions)}:{Canonicalize(entry.Value)}"))}}}",
            _ => node.ToJsonString(JsonOptions)
        };
    }
}
