' JSON-RPC 2.0 message types for LSP communication
' See: https://www.jsonrpc.org/specification

Imports System.Text.Json
Imports System.Text.Json.Serialization

Namespace Protocol

    ''' <summary>
    ''' Base class for all JSON-RPC messages.
    ''' </summary>
    Public MustInherit Class JsonRpcMessage
        <JsonPropertyName("jsonrpc")>
        Public Property JsonRpc As String = "2.0"
    End Class

    ''' <summary>
    ''' JSON-RPC request message (has an id, expects a response).
    ''' </summary>
    Public Class JsonRpcRequest
        Inherits JsonRpcMessage

        <JsonPropertyName("id")>
        Public Property Id As Object

        <JsonPropertyName("method")>
        Public Property Method As String = String.Empty

        <JsonPropertyName("params")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property Params As JsonElement?
    End Class

    ''' <summary>
    ''' JSON-RPC notification message (no id, no response expected).
    ''' </summary>
    Public Class JsonRpcNotification
        Inherits JsonRpcMessage

        <JsonPropertyName("method")>
        Public Property Method As String = String.Empty

        <JsonPropertyName("params")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property Params As JsonElement?
    End Class

    ''' <summary>
    ''' JSON-RPC response message.
    ''' </summary>
    Public Class JsonRpcResponse
        Inherits JsonRpcMessage

        <JsonPropertyName("id")>
        Public Property Id As Object

        <JsonPropertyName("result")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property Result As JsonElement?

        <JsonPropertyName("error")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property [Error] As JsonRpcError

        Public Shared Function Success(id As JsonRpcId, result As Object) As JsonRpcResponse
            Return New JsonRpcResponse With {
                .Id = id.ToRawValue(),
                .Result = JsonSerializer.SerializeToElement(result, JsonSerializerOptionsProvider.Options)
            }
        End Function

        Public Shared Function CreateError(id As JsonRpcId, code As Integer, message As String, Optional data As Object = Nothing) As JsonRpcResponse
            Dim errorData As JsonElement? = Nothing
            If data IsNot Nothing Then
                errorData = JsonSerializer.SerializeToElement(data, JsonSerializerOptionsProvider.Options)
            End If

            Return New JsonRpcResponse With {
                .Id = id.ToRawValue(),
                .Error = New JsonRpcError With {
                    .Code = code,
                    .Message = message,
                    .Data = errorData
                }
            }
        End Function
    End Class

    ''' <summary>
    ''' JSON-RPC error object.
    ''' </summary>
    Public Class JsonRpcError
        <JsonPropertyName("code")>
        Public Property Code As Integer

        <JsonPropertyName("message")>
        Public Property Message As String = String.Empty

        <JsonPropertyName("data")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property Data As JsonElement?
    End Class

    ''' <summary>
    ''' JSON-RPC error codes as defined in the specification.
    ''' </summary>
    Public NotInheritable Class JsonRpcErrorCodes
        Private Sub New()
        End Sub

        ' JSON-RPC defined errors
        Public Const ParseError As Integer = -32700
        Public Const InvalidRequest As Integer = -32600
        Public Const MethodNotFound As Integer = -32601
        Public Const InvalidParams As Integer = -32602
        Public Const InternalError As Integer = -32603

        ' LSP defined errors (range: -32099 to -32000)
        Public Const ServerNotInitialized As Integer = -32002
        Public Const UnknownErrorCode As Integer = -32001
        Public Const RequestFailed As Integer = -32803
        Public Const ServerCancelled As Integer = -32802
        Public Const ContentModified As Integer = -32801
        Public Const RequestCancelled As Integer = -32800
    End Class

    ''' <summary>
    ''' Represents a JSON-RPC id which can be a string, number, or null.
    ''' </summary>
    Public Structure JsonRpcId
        Implements IEquatable(Of JsonRpcId)

        Private ReadOnly _value As Object

        Public Sub New(value As String)
            _value = value
        End Sub

        Public Sub New(value As Integer)
            _value = value
        End Sub

        Public Sub New(value As Long)
            _value = value
        End Sub

        Public ReadOnly Property IsString As Boolean
            Get
                Return TypeOf _value Is String
            End Get
        End Property

        Public ReadOnly Property IsNumber As Boolean
            Get
                Return TypeOf _value Is Integer OrElse TypeOf _value Is Long
            End Get
        End Property

        Public ReadOnly Property IsNull As Boolean
            Get
                Return _value Is Nothing
            End Get
        End Property

        Public ReadOnly Property StringValue As String
            Get
                Return TryCast(_value, String)
            End Get
        End Property

        Public ReadOnly Property NumberValue As Long?
            Get
                If TypeOf _value Is Integer Then
                    Return CType(_value, Integer)
                End If

                If TypeOf _value Is Long Then
                    Return CType(_value, Long)
                End If

                Return Nothing
            End Get
        End Property

        Public Overrides Function ToString() As String
            If _value Is Nothing Then
                Return "null"
            End If

            Return _value.ToString()
        End Function

        Public Overloads Function Equals(other As JsonRpcId) As Boolean Implements IEquatable(Of JsonRpcId).Equals
            Return Object.Equals(_value, other._value)
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            If TypeOf obj Is JsonRpcId Then
                Return Equals(DirectCast(obj, JsonRpcId))
            End If

            Return False
        End Function

        Public Overrides Function GetHashCode() As Integer
            If _value Is Nothing Then
                Return 0
            End If

            Return _value.GetHashCode()
        End Function

        Public Function ToRawValue() As Object
            Return _value
        End Function

        Public Shared Operator =(left As JsonRpcId, right As JsonRpcId) As Boolean
            Return left.Equals(right)
        End Operator

        Public Shared Operator <>(left As JsonRpcId, right As JsonRpcId) As Boolean
            Return Not left.Equals(right)
        End Operator
    End Structure


    ''' <summary>
    ''' Provides configured JsonSerializerOptions for LSP serialization.
    ''' </summary>
    Public NotInheritable Class JsonSerializerOptionsProvider
        Private Sub New()
        End Sub

        Public Shared ReadOnly Property Options As JsonSerializerOptions = New JsonSerializerOptions With {
            .PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            .DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            .WriteIndented = False
        }
    End Class

End Namespace
