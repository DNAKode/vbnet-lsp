' Named pipe transport implementation for LSP communication
' Primary transport - follows C# extension pattern (outputs pipe name as JSON to stdout)

Imports System.IO.Pipes
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.Json
Imports Microsoft.Extensions.Logging

Namespace Protocol

    ''' <summary>
    ''' LSP transport using named pipes (Windows) or Unix domain sockets (macOS/Linux).
    ''' This is the primary transport, matching the C# extension behavior.
    '''
    ''' Protocol:
    ''' 1. Server creates a named pipe with a unique name
    ''' 2. Server outputs {"pipeName":"..."} to stdout
    ''' 3. Client reads this and connects to the pipe
    ''' 4. Bidirectional LSP communication over the pipe
    ''' </summary>
    Public NotInheritable Class NamedPipeTransport
        Implements ITransport

        Private ReadOnly _logger As ILogger(Of NamedPipeTransport)
        Private ReadOnly _pipeName As String
        Private ReadOnly _writeLock As SemaphoreSlim = New SemaphoreSlim(1, 1)

        Private _pipeServer As NamedPipeServerStream
        Private _readStream As Stream
        Private _writeStream As Stream
        Private _isConnected As Boolean

        Private Const ContentLengthHeader As String = "Content-Length: "

        Public Sub New(logger As ILogger(Of NamedPipeTransport), Optional pipeName As String = Nothing)
            If logger Is Nothing Then
                Throw New ArgumentNullException(NameOf(logger))
            End If

            _logger = logger
            _pipeName = If(String.IsNullOrWhiteSpace(pipeName), GeneratePipeName(), pipeName)
        End Sub

        ''' <summary>
        ''' Gets the pipe name for this transport.
        ''' </summary>
        Public ReadOnly Property PipeName As String
            Get
                Return _pipeName
            End Get
        End Property

        ''' <summary>
        ''' Generates a unique pipe name appropriate for the current platform.
        ''' </summary>
        Private Shared Function GeneratePipeName() As String
            Dim uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8)

            If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
                ' Windows named pipes don't need a path prefix in the API
                Return $"vbnet-lsp-{uniqueId}"
            End If

            ' Unix domain sockets need a file path
            Dim tempDir = Path.GetTempPath()
            Return Path.Combine(tempDir, $"vbnet-lsp-{uniqueId}.sock")
        End Function

        Public Async Function StartAsync(Optional cancellationToken As CancellationToken = Nothing) As Task Implements ITransport.StartAsync
            _logger.LogDebug("Creating named pipe: {PipeName}", _pipeName)

            _pipeServer = New NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances:=1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous)

            _logger.LogDebug("Starting to listen on pipe: {PipeName}", _pipeName)
            Dim connectionTask = _pipeServer.WaitForConnectionAsync(cancellationToken)

            Await Task.Delay(50, cancellationToken).ConfigureAwait(False)

            Dim pipeInfo = JsonSerializer.Serialize(New With {.pipeName = GetFullPipePath()})
            Await System.Console.Out.WriteLineAsync(pipeInfo).ConfigureAwait(False)
            Await System.Console.Out.FlushAsync().ConfigureAwait(False)
            _logger.LogInformation("Pipe ready, name output to stdout: {PipeInfo}", pipeInfo)

            _logger.LogDebug("Waiting for client connection on pipe: {PipeName}", _pipeName)
            Await connectionTask.ConfigureAwait(False)

            _readStream = _pipeServer
            _writeStream = _pipeServer
            _isConnected = True

            _logger.LogInformation("Client connected to named pipe")
        End Function

        ''' <summary>
        ''' Gets the full pipe path that clients should use to connect.
        ''' </summary>
        Private Function GetFullPipePath() As String
            If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
                Return $"\\.\pipe\{_pipeName}"
            End If

            ' On Unix, the pipe name is already the full socket path
            Return _pipeName
        End Function

        Public Async Function ReadMessageAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of String) Implements ITransport.ReadMessageAsync
            If Not _isConnected OrElse _readStream Is Nothing Then
                Throw New InvalidOperationException("Transport is not connected")
            End If

            Try
                Dim contentLength = Await ReadContentLengthAsync(cancellationToken).ConfigureAwait(False)
                If contentLength Is Nothing Then
                    _logger.LogDebug("End of pipe stream reached")
                    Return Nothing
                End If

                Dim buffer(contentLength.Value - 1) As Byte
                Dim totalRead = 0
                While totalRead < contentLength.Value
                    Dim bytesRead = Await _readStream.ReadAsync(buffer, totalRead, contentLength.Value - totalRead, cancellationToken).ConfigureAwait(False)
                    If bytesRead = 0 Then
                        _logger.LogWarning("Unexpected end of stream while reading message body")
                        Return Nothing
                    End If

                    totalRead += bytesRead
                End While

                Dim message = Encoding.UTF8.GetString(buffer)
                _logger.LogTrace("Received message via pipe: {Message}", message)
                Return message
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error reading message from named pipe")
                Throw
            End Try
        End Function

        Public Async Function WriteMessageAsync(message As String, Optional cancellationToken As CancellationToken = Nothing) As Task Implements ITransport.WriteMessageAsync
            If Not _isConnected OrElse _writeStream Is Nothing Then
                Throw New InvalidOperationException("Transport is not connected")
            End If

            Dim messageBytes = Encoding.UTF8.GetBytes(message)
            Dim header = $"{ContentLengthHeader}{messageBytes.Length}{vbCrLf}{vbCrLf}"
            Dim headerBytes = Encoding.UTF8.GetBytes(header)

            Await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(False)
            Try
                Await _writeStream.WriteAsync(headerBytes, 0, headerBytes.Length, cancellationToken).ConfigureAwait(False)
                Await _writeStream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken).ConfigureAwait(False)
                Await _writeStream.FlushAsync(cancellationToken).ConfigureAwait(False)
                _logger.LogTrace("Sent message via pipe: {Message}", message)
            Finally
                _writeLock.Release()
            End Try
        End Function

        Private Async Function ReadContentLengthAsync(cancellationToken As CancellationToken) As Task(Of Integer?)
            If _readStream Is Nothing Then
                Return Nothing
            End If

            Dim headerBuilder As New StringBuilder()
            Dim buffer(0) As Byte

            While True
                Dim bytesRead = Await _readStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(False)
                If bytesRead = 0 Then
                    Return Nothing
                End If

                headerBuilder.Append(ChrW(buffer(0)))
                Dim headerText = headerBuilder.ToString()

                Dim headerTerminator = vbCrLf & vbCrLf
                If headerText.EndsWith(headerTerminator, StringComparison.Ordinal) Then
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

        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            Return New ValueTask(DisposeAsyncCore())
        End Function

        Private Async Function DisposeAsyncCore() As Task
            _isConnected = False
            _writeLock.Dispose()

            If _pipeServer IsNot Nothing Then
                Await _pipeServer.DisposeAsync().ConfigureAwait(False)
            End If

            If Not RuntimeInformation.IsOSPlatform(OSPlatform.Windows) AndAlso File.Exists(_pipeName) Then
                Try
                    File.Delete(_pipeName)
                Catch ex As Exception
                    _logger.LogWarning(ex, "Failed to delete socket file: {Path}", _pipeName)
                End Try
            End If
        End Function
    End Class

End Namespace
