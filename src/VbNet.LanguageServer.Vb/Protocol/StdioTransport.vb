' Stdio transport implementation for LSP communication
' Secondary transport option - simpler but supported for compatibility

Imports System.Text
Imports Microsoft.Extensions.Logging

Namespace Protocol

    ''' <summary>
    ''' LSP transport using standard input/output streams.
    ''' Implements Content-Length header-based message framing per LSP specification.
    ''' </summary>
    Public NotInheritable Class StdioTransport
        Implements ITransport

        Private ReadOnly _input As Stream
        Private ReadOnly _output As Stream
        Private ReadOnly _logger As ILogger(Of StdioTransport)
        Private ReadOnly _writeLock As SemaphoreSlim = New SemaphoreSlim(1, 1)
        Private Shared ReadOnly TraceTransport As Boolean = IsTransportTraceEnabled()

        Private Const ContentLengthHeader As String = "Content-Length: "
        Private Const HeaderTerminator As String = vbCrLf & vbCrLf

        Public Sub New(input As Stream, output As Stream, logger As ILogger(Of StdioTransport))
            If input Is Nothing Then
                Throw New ArgumentNullException(NameOf(input))
            End If
            If output Is Nothing Then
                Throw New ArgumentNullException(NameOf(output))
            End If
            If logger Is Nothing Then
                Throw New ArgumentNullException(NameOf(logger))
            End If

            _input = input
            _output = output
            _logger = logger
            If TraceTransport Then
                _logger.LogInformation("Stdio transport tracing enabled")
                _logger.LogInformation("Console redirect flags: stdin={InRedirected}, stdout={OutRedirected}, stderr={ErrRedirected}",
                                       System.Console.IsInputRedirected,
                                       System.Console.IsOutputRedirected,
                                       System.Console.IsErrorRedirected)
            End If
        End Sub

        ''' <summary>
        ''' Creates a StdioTransport using Console.OpenStandardInput/Output.
        ''' </summary>
        Public Shared Function CreateFromConsole(logger As ILogger(Of StdioTransport)) As StdioTransport
            Return New StdioTransport(
                System.Console.OpenStandardInput(8192),
                System.Console.OpenStandardOutput(),
                logger)
        End Function

        Public Function StartAsync(Optional cancellationToken As CancellationToken = Nothing) As Task Implements ITransport.StartAsync
            _logger.LogDebug("Stdio transport started")
            Return Task.CompletedTask
        End Function

        Public Async Function ReadMessageAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of String) Implements ITransport.ReadMessageAsync
            Try
                If TraceTransport Then
                    _logger.LogInformation("Stdio awaiting message header")
                End If
                Dim contentLength = Await ReadContentLengthAsync(cancellationToken).ConfigureAwait(False)
                If contentLength Is Nothing Then
                    _logger.LogDebug("End of input stream reached")
                    Return Nothing
                End If
                If TraceTransport Then
                    _logger.LogInformation("Stdio received Content-Length: {Length}", contentLength.Value)
                End If

                Dim buffer(contentLength.Value - 1) As Byte
                Dim totalRead = 0
                While totalRead < contentLength.Value
                    Dim bytesRead = Await _input.ReadAsync(buffer, totalRead, contentLength.Value - totalRead, cancellationToken).ConfigureAwait(False)
                    If bytesRead = 0 Then
                        _logger.LogWarning("Unexpected end of stream while reading message body")
                        Return Nothing
                    End If

                    totalRead += bytesRead
                End While

                Dim message = Encoding.UTF8.GetString(buffer)
                If TraceTransport Then
                    _logger.LogInformation("Stdio received message body ({Length} bytes)", buffer.Length)
                End If
                _logger.LogTrace("Received message: {Message}", message)
                Return message
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error reading message from stdio")
                Throw
            End Try
        End Function

        Public Async Function WriteMessageAsync(message As String, Optional cancellationToken As CancellationToken = Nothing) As Task Implements ITransport.WriteMessageAsync
            Dim messageBytes = Encoding.UTF8.GetBytes(message)
            Dim header = $"{ContentLengthHeader}{messageBytes.Length}" & vbCrLf & vbCrLf
            Dim headerBytes = Encoding.UTF8.GetBytes(header)

            Await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(False)
            Try
                If TraceTransport Then
                    Dim preview = If(message.Length > 200, message.Substring(0, 200) & "...", message)
                    _logger.LogInformation("Stdio sending message length {Length}", messageBytes.Length)
                    _logger.LogInformation("Stdio sending message preview: {Preview}", preview)
                    Dim jsonrpcIndex = message.IndexOf("""jsonrpc""", StringComparison.OrdinalIgnoreCase)
                    _logger.LogInformation("Stdio jsonrpc index: {Index}", jsonrpcIndex)
                    If messageBytes.Length <= 2048 Then
                        _logger.LogInformation("Stdio sending message: {Message}", message)
                    End If
                    If Not message.Contains("""jsonrpc""", StringComparison.OrdinalIgnoreCase) Then
                        _logger.LogWarning("Stdio response missing jsonrpc field")
                    End If
                End If
                Await _output.WriteAsync(headerBytes, 0, headerBytes.Length, cancellationToken).ConfigureAwait(False)
                Await _output.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken).ConfigureAwait(False)
                Await _output.FlushAsync(cancellationToken).ConfigureAwait(False)
                _logger.LogTrace("Sent message: {Message}", message)
            Finally
                _writeLock.Release()
            End Try
        End Function

        Private Async Function ReadContentLengthAsync(cancellationToken As CancellationToken) As Task(Of Integer?)
            Dim headerBuilder As New StringBuilder()
            Dim buffer(0) As Byte
            Dim bytesSeen = 0
            Dim previewBytes As New List(Of Byte)(64)

            While True
                Dim bytesRead = Await _input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(False)
                If bytesRead = 0 Then
                    Return Nothing
                End If

                bytesSeen += bytesRead
                If TraceTransport AndAlso previewBytes.Count < 64 Then
                    previewBytes.Add(buffer(0))
                    If previewBytes.Count = 64 Then
                        _logger.LogInformation("Stdio header first 64 bytes (hex): {Hex}",
                                               BitConverter.ToString(previewBytes.ToArray()))
                    End If
                End If
                headerBuilder.Append(ChrW(buffer(0)))
                If TraceTransport Then
                    If bytesSeen <= 8 Then
                        _logger.LogInformation("Stdio header byte {Index}: {Byte}", bytesSeen, buffer(0))
                    ElseIf bytesSeen Mod 16 = 0 Then
                        _logger.LogInformation("Stdio header bytes read: {Count}", bytesSeen)
                    End If
                End If
                Dim headerText = headerBuilder.ToString()
                If TraceTransport AndAlso bytesSeen = 24 Then
                    Dim tail = headerText.Substring(Math.Max(0, headerText.Length - 4))
                    Dim codes As New List(Of String)(tail.Length)
                    For Each ch In tail
                        codes.Add(AscW(ch).ToString())
                    Next
                    _logger.LogInformation("Stdio header tail: {Tail} [{Codes}]",
                                           tail.Replace(vbCr, "<CR>").Replace(vbLf, "<LF>"),
                                           String.Join(",", codes))
                End If

                If headerText.EndsWith(HeaderTerminator, StringComparison.Ordinal) Then
                    If TraceTransport Then
                        _logger.LogInformation("Stdio header terminator received ({Length} bytes)", headerText.Length)
                    End If
                    Dim lines = headerText.Split({vbCrLf}, StringSplitOptions.RemoveEmptyEntries)
                    For Each line In lines
                        If line.StartsWith(ContentLengthHeader, StringComparison.OrdinalIgnoreCase) Then
                            Dim lengthStr = line.Substring(ContentLengthHeader.Length)
                            Dim length As Integer
                            If Integer.TryParse(lengthStr, length) Then
                                Return length
                            End If

                            _logger.LogError("Invalid Content-Length value: {Value}", lengthStr)
                            Throw New InvalidOperationException($"Invalid Content-Length: {lengthStr}")
                        End If
                    Next

                    _logger.LogError("No Content-Length header found in: {Headers}", headerText)
                    Throw New InvalidOperationException("Missing Content-Length header")
                End If

                If headerBuilder.Length > 8192 Then
                    Throw New InvalidOperationException("Header too large")
                End If
            End While

            Return Nothing
        End Function

        Private Shared Function IsTransportTraceEnabled() As Boolean
            Dim value = Environment.GetEnvironmentVariable("VBNET_LS_TRACE_TRANSPORT")
            If String.IsNullOrWhiteSpace(value) Then
                Return False
            End If

            Return value.Equals("1", StringComparison.OrdinalIgnoreCase) OrElse
                value.Equals("true", StringComparison.OrdinalIgnoreCase) OrElse
                value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        End Function

        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            Return New ValueTask(DisposeAsyncCore())
        End Function

        Private Async Function DisposeAsyncCore() As Task
            _writeLock.Dispose()
            Await _input.DisposeAsync().ConfigureAwait(False)
            Await _output.DisposeAsync().ConfigureAwait(False)
        End Function
    End Class

End Namespace
