// VB.NET Language Server entry point
// Host/CLI layer as defined in docs/architecture.md Section 5.5

using Microsoft.Build.Locator;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Core;
using VbNet.LanguageServer.Protocol;

namespace VbNet.LanguageServer;

/// <summary>
/// Entry point for the VB.NET Language Server.
/// Handles command-line argument parsing, logging configuration, and server startup.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Parse command line arguments
        var options = ParseArguments(args);

        // Configure logging
        using var loggerFactory = CreateLoggerFactory(options);
        var logger = loggerFactory.CreateLogger("VbNet.LanguageServer");

        try
        {
            // Register MSBuild (required for MSBuildWorkspace)
            RegisterMSBuild(logger);

            // Wait for debugger if requested
            if (options.WaitForDebugger)
            {
                logger.LogInformation("Waiting for debugger to attach...");
                while (!System.Diagnostics.Debugger.IsAttached)
                {
                    await Task.Delay(100);
                }
                logger.LogInformation("Debugger attached");
            }

            // Create transport based on options
            ITransport transport = options.TransportKind switch
            {
                TransportKind.NamedPipe => new NamedPipeTransport(
                    loggerFactory.CreateLogger<NamedPipeTransport>()),
                TransportKind.Stdio => StdioTransport.CreateFromConsole(
                    loggerFactory.CreateLogger<StdioTransport>()),
                _ => throw new InvalidOperationException($"Unknown transport: {options.TransportKind}")
            };

            // Create and run the language server
            await using var server = new Core.LanguageServer(transport, loggerFactory);

            logger.LogInformation("VB.NET Language Server v{Version} starting with {Transport} transport",
                Core.LanguageServer.ServerVersion,
                options.TransportKind);

            await server.RunAsync();

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Language server crashed");
            return 1;
        }
    }

    /// <summary>
    /// Registers MSBuild for MSBuildWorkspace.
    /// Must be called before any Roslyn types are loaded.
    /// </summary>
    private static void RegisterMSBuild(ILogger logger)
    {
        // Find and register the default MSBuild instance
        var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();

        if (instances.Count == 0)
        {
            logger.LogWarning("No MSBuild instances found. Project loading may not work.");
            return;
        }

        // Use the most recent instance
        var instance = instances.OrderByDescending(i => i.Version).First();
        MSBuildLocator.RegisterInstance(instance);

        logger.LogInformation("Registered MSBuild from: {Path} (version {Version})",
            instance.MSBuildPath, instance.Version);
    }

    /// <summary>
    /// Creates the logger factory based on options.
    /// </summary>
    private static ILoggerFactory CreateLoggerFactory(ServerOptions options)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(options.LogLevel);

            // For named pipe transport, we can log to console (stderr)
            // For stdio transport, we must only log to stderr to avoid corrupting the protocol
            builder.AddConsole(consoleOptions =>
            {
                consoleOptions.LogToStandardErrorThreshold = LogLevel.Trace;
            });
        });
    }

    /// <summary>
    /// Parses command line arguments.
    /// </summary>
    private static ServerOptions ParseArguments(string[] args)
    {
        var options = new ServerOptions();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "--pipe":
                    options.TransportKind = TransportKind.NamedPipe;
                    break;

                case "--stdio":
                    options.TransportKind = TransportKind.Stdio;
                    break;

                case "--debug":
                    options.WaitForDebugger = true;
                    break;

                case "--logLevel" when i + 1 < args.Length:
                    i++;
                    options.LogLevel = ParseLogLevel(args[i]);
                    break;

                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;

                case "--version":
                case "-v":
                    Console.WriteLine($"VbNet.LanguageServer {Core.LanguageServer.ServerVersion}");
                    Environment.Exit(0);
                    break;

                default:
                    if (arg.StartsWith("--"))
                    {
                        Console.Error.WriteLine($"Unknown option: {arg}");
                    }
                    break;
            }
        }

        return options;
    }

    private static LogLevel ParseLogLevel(string level)
    {
        return level.ToLowerInvariant() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "information" or "info" => LogLevel.Information,
            "warning" or "warn" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "critical" => LogLevel.Critical,
            "none" => LogLevel.None,
            _ => LogLevel.Information
        };
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"VB.NET Language Server

Usage: VbNet.LanguageServer [options]

Options:
  --pipe              Use named pipe transport (default)
  --stdio             Use stdio transport
  --logLevel <level>  Set log level (Trace, Debug, Information, Warning, Error, Critical)
  --debug             Wait for debugger to attach before starting
  --version, -v       Show version information
  --help, -h          Show this help message

Transport:
  Named pipe transport (default) outputs the pipe name as JSON to stdout:
    {""pipeName"":""\\\\.\\pipe\\vbnet-lsp-XXXXXXXX""}

  The client should connect to this pipe for bidirectional LSP communication.

  Stdio transport uses stdin/stdout for LSP messages with Content-Length headers.

Examples:
  VbNet.LanguageServer --pipe --logLevel Debug
  VbNet.LanguageServer --stdio
");
    }
}

/// <summary>
/// Command-line options for the language server.
/// </summary>
internal class ServerOptions
{
    /// <summary>
    /// Transport to use (named pipe is primary per architecture decision).
    /// </summary>
    public TransportKind TransportKind { get; set; } = TransportKind.NamedPipe;

    /// <summary>
    /// Minimum log level.
    /// </summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Wait for debugger to attach before starting.
    /// </summary>
    public bool WaitForDebugger { get; set; }
}

/// <summary>
/// Transport type selection.
/// </summary>
internal enum TransportKind
{
    /// <summary>Named pipe transport (primary).</summary>
    NamedPipe,

    /// <summary>Stdio transport (secondary).</summary>
    Stdio
}
