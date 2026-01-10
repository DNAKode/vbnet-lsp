using System.Text.Json;
using VbNet.LanguageServer.Protocol;
using Xunit;

namespace VbNet.LanguageServer.Tests.Protocol;

public class JsonRpcTypesTests
{
    [Fact]
    public void JsonRpcId_StringValue_SerializesCorrectly()
    {
        var id = new JsonRpcId("test-123");
        var json = JsonSerializer.Serialize(id, JsonSerializerOptionsProvider.Options);
        Assert.Equal("\"test-123\"", json);
    }

    [Fact]
    public void JsonRpcId_NumberValue_SerializesCorrectly()
    {
        var id = new JsonRpcId(42);
        var json = JsonSerializer.Serialize(id, JsonSerializerOptionsProvider.Options);
        Assert.Equal("42", json);
    }

    [Fact]
    public void JsonRpcId_StringValue_DeserializesCorrectly()
    {
        var json = "\"test-456\"";
        var id = JsonSerializer.Deserialize<JsonRpcId>(json, JsonSerializerOptionsProvider.Options);
        Assert.True(id.IsString);
        Assert.Equal("test-456", id.StringValue);
    }

    [Fact]
    public void JsonRpcId_NumberValue_DeserializesCorrectly()
    {
        var json = "99";
        var id = JsonSerializer.Deserialize<JsonRpcId>(json, JsonSerializerOptionsProvider.Options);
        Assert.True(id.IsNumber);
        Assert.Equal(99L, id.NumberValue);
    }

    [Fact]
    public void JsonRpcRequest_SerializesWithMethod()
    {
        var request = new JsonRpcRequest
        {
            Id = new JsonRpcId(1),
            Method = "initialize"
        };

        var json = JsonSerializer.Serialize(request, JsonSerializerOptionsProvider.Options);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal("initialize", doc.RootElement.GetProperty("method").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public void JsonRpcResponse_Success_SerializesCorrectly()
    {
        var response = JsonRpcResponse.Success(new JsonRpcId(1), new { foo = "bar" });
        var json = JsonSerializer.Serialize(response, JsonSerializerOptionsProvider.Options);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("bar", doc.RootElement.GetProperty("result").GetProperty("foo").GetString());
        Assert.False(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void JsonRpcResponse_Error_SerializesCorrectly()
    {
        var response = JsonRpcResponse.CreateError(
            new JsonRpcId(2),
            JsonRpcErrorCodes.MethodNotFound,
            "Method not found");

        var json = JsonSerializer.Serialize(response, JsonSerializerOptionsProvider.Options);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("id").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("result", out _));

        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, error.GetProperty("code").GetInt32());
        Assert.Equal("Method not found", error.GetProperty("message").GetString());
    }

    [Fact]
    public void JsonRpcNotification_SerializesWithoutId()
    {
        var notification = new JsonRpcNotification
        {
            Method = "initialized"
        };

        var json = JsonSerializer.Serialize(notification, JsonSerializerOptionsProvider.Options);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal("initialized", doc.RootElement.GetProperty("method").GetString());
        Assert.False(doc.RootElement.TryGetProperty("id", out _));
    }
}
