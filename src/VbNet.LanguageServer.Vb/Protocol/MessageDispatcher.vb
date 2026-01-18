' JSON-RPC message dispatcher for routing LSP requests and notifications

Imports System.Collections.Concurrent
Imports System.Text.Json
Imports Microsoft.Extensions.Logging

Namespace Protocol

    ''' <summary>
    ''' Dispatches incoming JSON-RPC messages to registered handlers.
    ''' Parses raw JSON into typed messages and routes based on method name.
    ''' </summary>
    Public NotInheritable Class MessageDispatcher
        Private ReadOnly _logger As ILogger(Of MessageDispatcher)
        Private ReadOnly _transport As ITransport

        Private ReadOnly _requestHandlers As Dictionary(Of String, Func(Of JsonElement?, CancellationToken, Task(Of Object))) =
            New Dictionary(Of String, Func(Of JsonElement?, CancellationToken, Task(Of Object)))()
        Private ReadOnly _notificationHandlers As Dictionary(Of String, Func(Of JsonElement?, CancellationToken, Task)) =
            New Dictionary(Of String, Func(Of JsonElement?, CancellationToken, Task))()
        Private ReadOnly _requestCancellation As ConcurrentDictionary(Of JsonRpcId, CancellationTokenSource) =
            New ConcurrentDictionary(Of JsonRpcId, CancellationTokenSource)()
        Private ReadOnly _requestTasks As ConcurrentBag(Of Task) = New ConcurrentBag(Of Task)()

        Private Const CancelRequestMethod As String = "$/cancelRequest"

        Public Sub New(transport As ITransport, logger As ILogger(Of MessageDispatcher))
            If transport Is Nothing Then
                Throw New ArgumentNullException(NameOf(transport))
            End If
            If logger Is Nothing Then
                Throw New ArgumentNullException(NameOf(logger))
            End If

            _transport = transport
            _logger = logger
        End Sub

        ''' <summary>
        ''' Registers a handler for an LSP request method.
        ''' </summary>
        Public Sub RegisterRequest(Of TParams, TResult)(method As String, handler As Func(Of TParams, CancellationToken, Task(Of TResult)))
            _requestHandlers(method) = Async Function(paramsElement, ct)
                                           Dim parameters As TParams = Nothing
                                           If paramsElement.HasValue Then
                                               parameters = JsonSerializer.Deserialize(Of TParams)(paramsElement.Value.GetRawText(), JsonSerializerOptionsProvider.Options)
                                           End If

                                           Dim result = Await handler(parameters, ct).ConfigureAwait(False)
                                           Return CType(result, Object)
                                       End Function
            _logger.LogDebug("Registered request handler for: {Method}", method)
        End Sub

        ''' <summary>
        ''' Registers a handler for an LSP notification method.
        ''' </summary>
        Public Sub RegisterNotification(Of TParams)(method As String, handler As Func(Of TParams, CancellationToken, Task))
            _notificationHandlers(method) = Async Function(paramsElement, ct)
                                                Dim parameters As TParams = Nothing
                                                If paramsElement.HasValue Then
                                                    parameters = JsonSerializer.Deserialize(Of TParams)(paramsElement.Value.GetRawText(), JsonSerializerOptionsProvider.Options)
                                                End If

                                                Await handler(parameters, ct).ConfigureAwait(False)
                                            End Function
            _logger.LogDebug("Registered notification handler for: {Method}", method)
        End Sub

        ''' <summary>
        ''' Registers a handler for a notification with no parameters.
        ''' </summary>
        Public Sub RegisterNotification(method As String, handler As Func(Of CancellationToken, Task))
            _notificationHandlers(method) = Async Function(ignoredParams, ct)
                                                Await handler(ct).ConfigureAwait(False)
                                            End Function
            _logger.LogDebug("Registered notification handler for: {Method}", method)
        End Sub

        ''' <summary>
        ''' Sends a notification to the client.
        ''' </summary>
        Public Async Function SendNotificationAsync(Of TParams)(method As String, parameters As TParams, Optional cancellationToken As CancellationToken = Nothing) As Task
            Dim notification = New JsonRpcNotification With {
                .Method = method,
                .Params = JsonSerializer.SerializeToElement(parameters, JsonSerializerOptionsProvider.Options)
            }

            Dim json = JsonSerializer.Serialize(notification, JsonSerializerOptionsProvider.Options)
            Await _transport.WriteMessageAsync(json, cancellationToken).ConfigureAwait(False)
            _logger.LogDebug("Sent notification: {Method}", method)
        End Function

        ''' <summary>
        ''' Sends a notification with no parameters to the client.
        ''' </summary>
        Public Async Function SendNotificationAsync(method As String, Optional cancellationToken As CancellationToken = Nothing) As Task
            Dim notification = New JsonRpcNotification With {.Method = method}
            Dim json = JsonSerializer.Serialize(notification, JsonSerializerOptionsProvider.Options)
            Await _transport.WriteMessageAsync(json, cancellationToken).ConfigureAwait(False)
            _logger.LogDebug("Sent notification: {Method}", method)
        End Function

        ''' <summary>
        ''' Starts the message processing loop.
        ''' </summary>
        Public Async Function RunAsync(cancellationToken As CancellationToken) As Task
            _logger.LogInformation("Message dispatcher started")

            While Not cancellationToken.IsCancellationRequested
                Try
                    Dim message = Await _transport.ReadMessageAsync(cancellationToken).ConfigureAwait(False)
                    If message Is Nothing Then
                        _logger.LogInformation("Transport closed, stopping message dispatcher")
                        Exit While
                    End If

                    Await ProcessMessageWithHandlingAsync(message, cancellationToken).ConfigureAwait(False)
                Catch ex As OperationCanceledException When cancellationToken.IsCancellationRequested
                    _logger.LogDebug("Message dispatcher cancelled")
                    Exit While
                Catch ex As Exception
                    _logger.LogError(ex, "Error processing message")
                End Try
            End While

            _logger.LogInformation("Message dispatcher stopped")
        End Function

        Private Async Function ProcessMessageWithHandlingAsync(messageJson As String, cancellationToken As CancellationToken) As Task
            Try
                Await ProcessMessageAsync(messageJson, cancellationToken).ConfigureAwait(False)
            Catch ex As OperationCanceledException When cancellationToken.IsCancellationRequested
                ' Ignore cancellations from shutdown.
            Catch ex As Exception
                _logger.LogError(ex, "Error processing message")
            End Try
        End Function

        Private Async Function ProcessMessageAsync(messageJson As String, cancellationToken As CancellationToken) As Task
            Dim document As JsonDocument
            Try
                document = JsonDocument.Parse(messageJson)
            Catch ex As JsonException
                _logger.LogError(ex, "Failed to parse JSON-RPC message")
                Return
            End Try

            Using document
                Dim root = document.RootElement

                ' Check if this is a request (has id) or notification (no id)
                Dim idElement As JsonElement
                Dim hasId = root.TryGetProperty("id", idElement)
                Dim methodElement As JsonElement
                Dim hasMethod = root.TryGetProperty("method", methodElement)

                If Not hasMethod Then
                    _logger.LogWarning("Received message without method property")
                    Return
                End If

                Dim method = methodElement.GetString()
                Dim paramsElement As JsonElement? = Nothing
                Dim paramsValue As JsonElement
                If root.TryGetProperty("params", paramsValue) Then
                    paramsElement = paramsValue
                End If

                If String.Equals(method, CancelRequestMethod, StringComparison.Ordinal) Then
                    Await HandleCancelRequestAsync(paramsElement).ConfigureAwait(False)
                    Return
                End If

                If hasId Then
                    Dim id = ParseId(idElement)
                    Dim task = HandleRequestAsync(id, method, paramsElement, cancellationToken)
                    _requestTasks.Add(task)
                    Dim ignored = task.ContinueWith(
                        Sub(t)
                            _logger.LogError(t.Exception, "Request handler failed for: {Method} (id: {Id})", method, id)
                        End Sub,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default)
                Else
                    Await HandleNotificationAsync(method, paramsElement, cancellationToken).ConfigureAwait(False)
                End If
            End Using
        End Function

        Private Async Function HandleRequestAsync(id As JsonRpcId, method As String, paramsElement As JsonElement?, cancellationToken As CancellationToken) As Task
            _logger.LogDebug("Handling request: {Method} (id: {Id})", method, id)

            Dim response As JsonRpcResponse
            Dim requestCts As CancellationTokenSource = Nothing
            Dim linkedCts As CancellationTokenSource = Nothing
            Dim requestCancellationToken = cancellationToken

            If Not id.IsNull Then
                requestCts = New CancellationTokenSource()
                If Not _requestCancellation.TryAdd(id, requestCts) Then
                    requestCts.Dispose()
                    requestCts = Nothing
                End If
            End If

            If requestCts IsNot Nothing Then
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, requestCts.Token)
                requestCancellationToken = linkedCts.Token
            End If

            Dim handler As Func(Of JsonElement?, CancellationToken, Task(Of Object)) = Nothing
            If _requestHandlers.TryGetValue(method, handler) Then
                Try
                    Dim result = Await handler(paramsElement, requestCancellationToken).ConfigureAwait(False)
                    response = JsonRpcResponse.Success(id, result)
                Catch ex As OperationCanceledException
                    response = JsonRpcResponse.CreateError(id, JsonRpcErrorCodes.RequestCancelled, "Request cancelled")
                Catch ex As Exception
                    _logger.LogError(ex, "Error handling request: {Method}", method)
                    response = JsonRpcResponse.CreateError(id, JsonRpcErrorCodes.InternalError, ex.Message)
                End Try
            Else
                _logger.LogWarning("No handler registered for request: {Method}", method)
                response = JsonRpcResponse.CreateError(id, JsonRpcErrorCodes.MethodNotFound, $"Method not found: {method}")
            End If

            Try
                Dim json = JsonSerializer.Serialize(response, JsonSerializerOptionsProvider.Options)
                Await _transport.WriteMessageAsync(json, cancellationToken).ConfigureAwait(False)
            Finally
                If Not id.IsNull Then
                    Dim removed As CancellationTokenSource = Nothing
                    If _requestCancellation.TryRemove(id, removed) Then
                        removed.Dispose()
                    End If
                End If

                If linkedCts IsNot Nothing Then
                    linkedCts.Dispose()
                End If
            End Try
        End Function

        Private Async Function HandleNotificationAsync(method As String, paramsElement As JsonElement?, cancellationToken As CancellationToken) As Task
            _logger.LogDebug("Handling notification: {Method}", method)

            Dim handler As Func(Of JsonElement?, CancellationToken, Task) = Nothing
            If _notificationHandlers.TryGetValue(method, handler) Then
                Try
                    Await handler(paramsElement, cancellationToken).ConfigureAwait(False)
                Catch ex As Exception
                    _logger.LogError(ex, "Error handling notification: {Method}", method)
                End Try
            Else
                _logger.LogTrace("No handler registered for notification: {Method}", method)
            End If
        End Function

        Private Function HandleCancelRequestAsync(paramsElement As JsonElement?) As Task
            If Not paramsElement.HasValue Then
                Return Task.CompletedTask
            End If

            Try
                Dim idElement As JsonElement
                If paramsElement.Value.ValueKind = JsonValueKind.Object AndAlso paramsElement.Value.TryGetProperty("id", idElement) Then
                    Dim id = ParseId(idElement)
                    If id.IsNull Then
                        Return Task.CompletedTask
                    End If

                    Dim cts As CancellationTokenSource = Nothing
                    If _requestCancellation.TryRemove(id, cts) Then
                        cts.Cancel()
                        cts.Dispose()
                        _logger.LogDebug("Cancelled request: {Id}", id)
                    End If
                End If
            Catch ex As Exception
                _logger.LogWarning(ex, "Failed to process $/cancelRequest")
            End Try

            Return Task.CompletedTask
        End Function

        Private Shared Function ParseId(element As JsonElement) As JsonRpcId
            Select Case element.ValueKind
                Case JsonValueKind.String
                    Return New JsonRpcId(element.GetString())
                Case JsonValueKind.Number
                    Return New JsonRpcId(element.GetInt64())
                Case Else
                    Return New JsonRpcId()
            End Select
        End Function
    End Class

End Namespace
