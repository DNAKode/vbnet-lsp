// JSON-RPC 2.0 message types for LSP communication
// See: https://www.jsonrpc.org/specification

using System.Text.Json;
using System.Text.Json.Serialization;

namespace VbNet.LanguageServer.Protocol;

/// <summary>
/// Base class for all JSON-RPC messages.
/// </summary>
public abstract class JsonRpcMessage
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";
}

/// <summary>
/// JSON-RPC request message (has an id, expects a response).
/// </summary>
public class JsonRpcRequest : JsonRpcMessage
{
    [JsonPropertyName("id")]
    public JsonRpcId Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Params { get; set; }
}

/// <summary>
/// JSON-RPC notification message (no id, no response expected).
/// </summary>
public class JsonRpcNotification : JsonRpcMessage
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Params { get; set; }
}

/// <summary>
/// JSON-RPC response message.
/// </summary>
public class JsonRpcResponse : JsonRpcMessage
{
    [JsonPropertyName("id")]
    public JsonRpcId Id { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; set; }

    public static JsonRpcResponse Success(JsonRpcId id, object? result)
    {
        return new JsonRpcResponse
        {
            Id = id,
            Result = result != null
                ? JsonSerializer.SerializeToElement(result, JsonSerializerOptionsProvider.Options)
                : null
        };
    }

    public static JsonRpcResponse CreateError(JsonRpcId id, int code, string message, object? data = null)
    {
        return new JsonRpcResponse
        {
            Id = id,
            Error = new JsonRpcError
            {
                Code = code,
                Message = message,
                Data = data != null
                    ? JsonSerializer.SerializeToElement(data, JsonSerializerOptionsProvider.Options)
                    : null
            }
        };
    }
}

/// <summary>
/// JSON-RPC error object.
/// </summary>
public class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Data { get; set; }
}

/// <summary>
/// JSON-RPC error codes as defined in the specification.
/// </summary>
public static class JsonRpcErrorCodes
{
    // JSON-RPC defined errors
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;

    // LSP defined errors (range: -32099 to -32000)
    public const int ServerNotInitialized = -32002;
    public const int UnknownErrorCode = -32001;
    public const int RequestFailed = -32803;
    public const int ServerCancelled = -32802;
    public const int ContentModified = -32801;
    public const int RequestCancelled = -32800;
}

/// <summary>
/// Represents a JSON-RPC id which can be a string, number, or null.
/// </summary>
[JsonConverter(typeof(JsonRpcIdConverter))]
public readonly struct JsonRpcId : IEquatable<JsonRpcId>
{
    private readonly object? _value;

    public JsonRpcId(string value) => _value = value;
    public JsonRpcId(int value) => _value = value;
    public JsonRpcId(long value) => _value = value;

    public bool IsString => _value is string;
    public bool IsNumber => _value is int or long;
    public bool IsNull => _value is null;

    public string? StringValue => _value as string;
    public long? NumberValue => _value switch
    {
        int i => i,
        long l => l,
        _ => null
    };

    public override string ToString() => _value?.ToString() ?? "null";

    public bool Equals(JsonRpcId other) => Equals(_value, other._value);
    public override bool Equals(object? obj) => obj is JsonRpcId other && Equals(other);
    public override int GetHashCode() => _value?.GetHashCode() ?? 0;

    public static bool operator ==(JsonRpcId left, JsonRpcId right) => left.Equals(right);
    public static bool operator !=(JsonRpcId left, JsonRpcId right) => !left.Equals(right);
}

/// <summary>
/// JSON converter for JsonRpcId to handle string/number/null polymorphism.
/// </summary>
public class JsonRpcIdConverter : JsonConverter<JsonRpcId>
{
    public override JsonRpcId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => new JsonRpcId(reader.GetString()!),
            JsonTokenType.Number when reader.TryGetInt64(out var l) => new JsonRpcId(l),
            JsonTokenType.Null => default,
            _ => throw new JsonException($"Unexpected token type for id: {reader.TokenType}")
        };
    }

    public override void Write(Utf8JsonWriter writer, JsonRpcId value, JsonSerializerOptions options)
    {
        if (value.IsNull)
            writer.WriteNullValue();
        else if (value.IsString)
            writer.WriteStringValue(value.StringValue);
        else if (value.IsNumber)
            writer.WriteNumberValue(value.NumberValue!.Value);
    }
}

/// <summary>
/// Provides configured JsonSerializerOptions for LSP serialization.
/// </summary>
public static class JsonSerializerOptionsProvider
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonRpcIdConverter() }
    };
}
