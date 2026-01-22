' VB.NET Language Server entry point
' Host/CLI layer as defined in docs/architecture.md Section 5.5

Imports System.IO
Imports Microsoft.Build.Locator
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Core
Imports VbNet.LanguageServer.Protocol

''' <summary>
''' Entry point for the VB.NET Language Server.
''' Handles command-line argument parsing, logging configuration, and server startup.
''' </summary>
Public Module Program
    Public Sub Main(args As String())
        Dim exitCode = MainAsync(args).GetAwaiter().GetResult()
        Environment.ExitCode = exitCode
    End Sub

    Private Async Function MainAsync(args As String()) As Task(Of Integer)
        Dim options = ParseArguments(args)

        Using loggerFactory = CreateLoggerFactory(options)
            Dim logger = loggerFactory.CreateLogger("VbNet.LanguageServer")

            Try
                RegisterMSBuild(logger, options.MsbuildPath)

                If options.WaitForDebugger Then
                    logger.LogInformation("Waiting for debugger to attach...")
                    While Not System.Diagnostics.Debugger.IsAttached
                        Await Task.Delay(100).ConfigureAwait(False)
                    End While
                    logger.LogInformation("Debugger attached")
                End If

                Dim transport As ITransport
                Select Case options.TransportKind
                    Case TransportKind.NamedPipe
                        transport = New NamedPipeTransport(loggerFactory.CreateLogger(Of NamedPipeTransport)())
                    Case TransportKind.Stdio
                        transport = StdioTransport.CreateFromConsole(loggerFactory.CreateLogger(Of StdioTransport)())
                    Case Else
                        Throw New InvalidOperationException($"Unknown transport: {options.TransportKind}")
                End Select

                Dim server As New Core.LanguageServer(transport, loggerFactory)
                Try
                    logger.LogInformation("VB.NET Language Server v{Version} starting with {Transport} transport", Core.LanguageServer.ServerVersion, options.TransportKind)
                    Await server.RunAsync().ConfigureAwait(False)
                Finally
                    server.DisposeAsync().AsTask().GetAwaiter().GetResult()
                End Try

                Return 0
            Catch ex As Exception
                logger.LogCritical(ex, "Language server crashed")
                Return 1
            End Try
        End Using
    End Function

    ''' <summary>
    ''' Registers MSBuild for MSBuildWorkspace.
    ''' Must be called before any Roslyn types are loaded.
    ''' </summary>
    Private Sub RegisterMSBuild(logger As ILogger, msbuildPathOverride As String)
        Dim configuredPath = If(String.IsNullOrWhiteSpace(msbuildPathOverride),
                                Environment.GetEnvironmentVariable("VBNET_MSBUILD_PATH"),
                                msbuildPathOverride)

        If Not String.IsNullOrWhiteSpace(configuredPath) Then
            Dim normalized = configuredPath.Trim()
            Dim resolved = If(Directory.Exists(normalized), Path.GetFullPath(normalized), Nothing)

            If resolved Is Nothing AndAlso File.Exists(normalized) Then
                resolved = Path.GetDirectoryName(Path.GetFullPath(normalized))
            End If

            If String.IsNullOrWhiteSpace(resolved) OrElse Not Directory.Exists(resolved) Then
                logger.LogWarning("Configured MSBuild path not found: {Path}", normalized)
            Else
                MSBuildLocator.RegisterMSBuildPath(resolved)
                Environment.SetEnvironmentVariable("VBNET_MSBUILD_PATH_ACTIVE", resolved)
                logger.LogInformation("Registered MSBuild from override path: {Path}", resolved)
                Return
            End If
        End If

        Dim instances = MSBuildLocator.QueryVisualStudioInstances().ToList()

        If instances.Count = 0 Then
            logger.LogWarning("No MSBuild instances found. Project loading may not work.")
            Return
        End If

        Dim instance = instances.OrderByDescending(Function(i) i.Version).First()
        MSBuildLocator.RegisterInstance(instance)
        Environment.SetEnvironmentVariable("VBNET_MSBUILD_PATH_ACTIVE", instance.MSBuildPath)

        logger.LogInformation("Registered MSBuild from: {Path} (version {Version})", instance.MSBuildPath, instance.Version)
    End Sub

    ''' <summary>
    ''' Creates the logger factory based on options.
    ''' </summary>
    Private Function CreateLoggerFactory(options As ServerOptions) As ILoggerFactory
        Return LoggerFactory.Create(Sub(builder)
                                        builder.SetMinimumLevel(options.LogLevel)
                                        builder.AddConsole(Sub(consoleOptions)
                                                               consoleOptions.LogToStandardErrorThreshold = LogLevel.Trace
                                                           End Sub)
                                    End Sub)
    End Function

    ''' <summary>
    ''' Parses command line arguments.
    ''' </summary>
    Private Function ParseArguments(args As String()) As ServerOptions
        Dim options As New ServerOptions()

        Dim i = 0
        While i < args.Length
            Dim arg = args(i)
            Select Case arg
                Case "--pipe"
                    options.TransportKind = TransportKind.NamedPipe
                Case "--stdio"
                    options.TransportKind = TransportKind.Stdio
                Case "--debug"
                    options.WaitForDebugger = True
                Case "--logLevel"
                    If i + 1 < args.Length Then
                        i += 1
                        options.LogLevel = ParseLogLevel(args(i))
                    End If
                Case "--msbuildPath"
                    If i + 1 < args.Length Then
                        i += 1
                        options.MsbuildPath = args(i)
                    End If
                Case "--help", "-h"
                    PrintHelp()
                    Environment.Exit(0)
                Case "--version", "-v"
                    System.Console.WriteLine($"VbNet.LanguageServer {Core.LanguageServer.ServerVersion}")
                    Environment.Exit(0)
                Case Else
                    If arg.StartsWith("--") Then
                        System.Console.Error.WriteLine($"Unknown option: {arg}")
                    End If
            End Select

            i += 1
        End While

        Return options
    End Function

    Private Function ParseLogLevel(level As String) As LogLevel
        Select Case level.ToLowerInvariant()
            Case "trace"
                Return LogLevel.Trace
            Case "debug"
                Return LogLevel.Debug
            Case "information", "info"
                Return LogLevel.Information
            Case "warning", "warn"
                Return LogLevel.Warning
            Case "error"
                Return LogLevel.Error
            Case "critical"
                Return LogLevel.Critical
            Case "none"
                Return LogLevel.None
            Case Else
                Return LogLevel.Information
        End Select
    End Function

    Private Sub PrintHelp()
        System.Console.WriteLine("VB.NET Language Server" & Environment.NewLine & Environment.NewLine &
                          "Usage: VbNet.LanguageServer [options]" & Environment.NewLine & Environment.NewLine &
                          "Options:" & Environment.NewLine &
                          "  --pipe              Use named pipe transport (default)" & Environment.NewLine &
                          "  --stdio             Use stdio transport" & Environment.NewLine &
                          "  --logLevel <level>  Set log level (Trace, Debug, Information, Warning, Error, Critical)" & Environment.NewLine &
                          "  --msbuildPath <path>  Use a specific MSBuild path for project loading" & Environment.NewLine &
                          "  --debug             Wait for debugger to attach before starting" & Environment.NewLine &
                          "  --version, -v       Show version information" & Environment.NewLine &
                          "  --help, -h          Show this help message" & Environment.NewLine & Environment.NewLine &
                          "Transport:" & Environment.NewLine &
                          "  Named pipe transport (default) outputs the pipe name as JSON to stdout:" & Environment.NewLine &
                          "    {""pipeName"": ""\\\\.\\pipe\\vbnet-lsp-XXXXXXXX""}" & Environment.NewLine & Environment.NewLine &
                          "  The client should connect to this pipe for bidirectional LSP communication." & Environment.NewLine & Environment.NewLine &
                          "  Stdio transport uses stdin/stdout for LSP messages with Content-Length headers." & Environment.NewLine & Environment.NewLine &
                          "Examples:" & Environment.NewLine &
                          "  VbNet.LanguageServer --pipe --logLevel Debug" & Environment.NewLine &
                          "  VbNet.LanguageServer --stdio" & Environment.NewLine)
    End Sub
End Module

''' <summary>
''' Command-line options for the language server.
''' </summary>
Friend Class ServerOptions
    ''' <summary>
    ''' Transport to use (named pipe is primary per architecture decision).
    ''' </summary>
    Public Property TransportKind As TransportKind = TransportKind.NamedPipe

    ''' <summary>
    ''' Minimum log level.
    ''' </summary>
    Public Property LogLevel As LogLevel = LogLevel.Information

    ''' <summary>
    ''' MSBuild path override.
    ''' </summary>
    Public Property MsbuildPath As String

    ''' <summary>
    ''' Wait for debugger to attach before starting.
    ''' </summary>
    Public Property WaitForDebugger As Boolean
End Class

''' <summary>
''' Transport type selection.
''' </summary>
Friend Enum TransportKind
    ''' <summary>Named pipe transport (primary).</summary>
    NamedPipe

    ''' <summary>Stdio transport (secondary).</summary>
    Stdio
End Enum
