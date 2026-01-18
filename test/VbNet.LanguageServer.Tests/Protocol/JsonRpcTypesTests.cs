using System.Text.Json;
using VbNet.LanguageServer.Protocol;
using Xunit;

namespace VbNet.LanguageServer.Tests.Protocol;

public class JsonRpcTypesTests
{
    [Fact]
    public void JsonRpcId_StringValue_ExposesStringValue()
    {
        var id = new JsonRpcId("test-123");
        Assert.True(id.IsString);
        Assert.False(id.IsNumber);
        Assert.Equal("test-123", id.StringValue);
    }

    [Fact]
    public void JsonRpcId_NumberValue_ExposesNumberValue()
    {
        var id = new JsonRpcId(42);
        Assert.True(id.IsNumber);
        Assert.False(id.IsString);
        Assert.Equal(42L, id.NumberValue);
    }

    [Fact]
    public void JsonRpcRequest_SerializesWithMethod()
    {
        var request = new JsonRpcRequest
        {
            Id = 1,
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
