Imports System.Text.Json
Imports VbNet.LanguageServer.Protocol
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Protocol

    Public Class JsonRpcTypesTests
        <Fact>
        Public Sub JsonRpcId_StringValue_ExposesStringValue()
            Dim id = New JsonRpcId("test-123")
            Assert.True(id.IsString)
            Assert.False(id.IsNumber)
            Assert.Equal("test-123", id.StringValue)
        End Sub

        <Fact>
        Public Sub JsonRpcId_NumberValue_ExposesNumberValue()
            Dim id = New JsonRpcId(42)
            Assert.True(id.IsNumber)
            Assert.False(id.IsString)
            Assert.Equal(42L, id.NumberValue)
        End Sub

        <Fact>
        Public Sub JsonRpcRequest_SerializesWithMethod()
            Dim request = New JsonRpcRequest With {
                .Id = 1,
                .Method = "initialize"
            }

            Dim json = JsonSerializer.Serialize(request, JsonSerializerOptionsProvider.Options)
            Dim doc = JsonDocument.Parse(json)

            Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString())
            Assert.Equal("initialize", doc.RootElement.GetProperty("method").GetString())
            Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32())
        End Sub

        <Fact>
        Public Sub JsonRpcResponse_Success_SerializesCorrectly()
            Dim response = JsonRpcResponse.Success(New JsonRpcId(1), New With {.foo = "bar"})
            Dim json = JsonSerializer.Serialize(response, JsonSerializerOptionsProvider.Options)
            Dim doc = JsonDocument.Parse(json)

            Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString())
            Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32())
            Assert.Equal("bar", doc.RootElement.GetProperty("result").GetProperty("foo").GetString())
            Assert.False(doc.RootElement.TryGetProperty("error", Nothing))
        End Sub

        <Fact>
        Public Sub JsonRpcResponse_Error_SerializesCorrectly()
            Dim response = JsonRpcResponse.CreateError(New JsonRpcId(2), JsonRpcErrorCodes.MethodNotFound, "Method not found")
            Dim json = JsonSerializer.Serialize(response, JsonSerializerOptionsProvider.Options)
            Dim doc = JsonDocument.Parse(json)

            Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString())
            Assert.Equal(2, doc.RootElement.GetProperty("id").GetInt32())
            Assert.False(doc.RootElement.TryGetProperty("result", Nothing))

            Dim err = doc.RootElement.GetProperty("error")
            Assert.Equal(JsonRpcErrorCodes.MethodNotFound, err.GetProperty("code").GetInt32())
            Assert.Equal("Method not found", err.GetProperty("message").GetString())
        End Sub

        <Fact>
        Public Sub JsonRpcNotification_SerializesWithoutId()
            Dim notification = New JsonRpcNotification With {
                .Method = "initialized"
            }

            Dim json = JsonSerializer.Serialize(notification, JsonSerializerOptionsProvider.Options)
            Dim doc = JsonDocument.Parse(json)

            Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString())
            Assert.Equal("initialized", doc.RootElement.GetProperty("method").GetString())
            Assert.False(doc.RootElement.TryGetProperty("id", Nothing))
        End Sub
    End Class

End Namespace
