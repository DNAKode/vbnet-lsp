// JSON-RPC message dispatcher for routing LSP requests and notifications

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VbNet.LanguageServer.Protocol;

/// <summary>
/// Dispatches incoming JSON-RPC messages to registered handlers.
/// Parses raw JSON into typed messages and routes based on method name.
/// </summary>
public sealed class MessageDispatcher
{
    private readonly ILogger<MessageDispatcher> _logger;
    private readonly ITransport _transport;

    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<object?>>> _requestHandlers = new();
    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task>> _notificationHandlers = new();
    private readonly ConcurrentDictionary<JsonRpcId, CancellationTokenSource> _requestCancellation = new();

    private const string CancelRequestMethod = "$/cancelRequest";

    public MessageDispatcher(ITransport transport, ILogger<MessageDispatcher> logger)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers a handler for an LSP request method.
    /// </summary>
    public void RegisterRequest<TParams, TResult>(
        string method,
        Func<TParams?, CancellationToken, Task<TResult>> handler)
    {
        _requestHandlers[method] = async (paramsElement, ct) =>
        {
            var parameters = paramsElement.HasValue
                ? JsonSerializer.Deserialize<TParams>(paramsElement.Value.GetRawText(), JsonSerializerOptionsProvider.Options)
                : default;
            return await handler(parameters, ct);
        };
        _logger.LogDebug("Registered request handler for: {Method}", method);
    }

    /// <summary>
    /// Registers a handler for an LSP notification method.
    /// </summary>
    public void RegisterNotification<TParams>(
        string method,
        Func<TParams?, CancellationToken, Task> handler)
    {
        _notificationHandlers[method] = async (paramsElement, ct) =>
        {
            var parameters = paramsElement.HasValue
                ? JsonSerializer.Deserialize<TParams>(paramsElement.Value.GetRawText(), JsonSerializerOptionsProvider.Options)
                : default;
            await handler(parameters, ct);
        };
        _logger.LogDebug("Registered notification handler for: {Method}", method);
    }

    /// <summary>
    /// Registers a handler for a notification with no parameters.
    /// </summary>
    public void RegisterNotification(string method, Func<CancellationToken, Task> handler)
    {
        _notificationHandlers[method] = async (_, ct) => await handler(ct);
        _logger.LogDebug("Registered notification handler for: {Method}", method);
    }

    /// <summary>
    /// Sends a notification to the client.
    /// </summary>
    public async Task SendNotificationAsync<TParams>(string method, TParams parameters, CancellationToken cancellationToken = default)
    {
        var notification = new JsonRpcNotification
        {
            Method = method,
            Params = JsonSerializer.SerializeToElement(parameters, JsonSerializerOptionsProvider.Options)
        };

        var json = JsonSerializer.Serialize(notification, JsonSerializerOptionsProvider.Options);
        await _transport.WriteMessageAsync(json, cancellationToken);
        _logger.LogDebug("Sent notification: {Method}", method);
    }

    /// <summary>
    /// Sends a notification with no parameters to the client.
    /// </summary>
    public async Task SendNotificationAsync(string method, CancellationToken cancellationToken = default)
    {
        var notification = new JsonRpcNotification { Method = method };
        var json = JsonSerializer.Serialize(notification, JsonSerializerOptionsProvider.Options);
        await _transport.WriteMessageAsync(json, cancellationToken);
        _logger.LogDebug("Sent notification: {Method}", method);
    }

    /// <summary>
    /// Starts the message processing loop.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Message dispatcher started");
        var inFlightTasks = new ConcurrentBag<Task>();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var message = await _transport.ReadMessageAsync(cancellationToken);
                if (message == null)
                {
                    _logger.LogInformation("Transport closed, stopping message dispatcher");
                    break;
                }

                var task = ProcessMessageWithHandlingAsync(message, cancellationToken);
                inFlightTasks.Add(task);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Message dispatcher cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                // Continue processing other messages
            }
        }

        await AwaitInFlightTasksAsync(inFlightTasks).ConfigureAwait(false);
        _logger.LogInformation("Message dispatcher stopped");
    }

    private async Task ProcessMessageWithHandlingAsync(string messageJson, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessMessageAsync(messageJson, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ignore cancellations from shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
        }
    }

    private static async Task AwaitInFlightTasksAsync(ConcurrentBag<Task> tasks)
    {
        if (tasks.IsEmpty)
        {
            return;
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            // Individual tasks log their own errors.
        }
    }

    private async Task ProcessMessageAsync(string messageJson, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(messageJson);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON-RPC message");
            return;
        }

        using (document)
        {
            var root = document.RootElement;

            // Check if this is a request (has id) or notification (no id)
            var hasId = root.TryGetProperty("id", out var idElement);
            var hasMethod = root.TryGetProperty("method", out var methodElement);

            if (!hasMethod)
            {
                _logger.LogWarning("Received message without method property");
                return;
            }

            var method = methodElement.GetString()!;
            var paramsElement = root.TryGetProperty("params", out var p) ? p : (JsonElement?)null;

            if (string.Equals(method, CancelRequestMethod, StringComparison.Ordinal))
            {
                await HandleCancelRequestAsync(paramsElement).ConfigureAwait(false);
                return;
            }

            if (hasId)
            {
                // This is a request
                var id = ParseId(idElement);
                await HandleRequestAsync(id, method, paramsElement, cancellationToken);
            }
            else
            {
                // This is a notification
                await HandleNotificationAsync(method, paramsElement, cancellationToken);
            }
        }
    }

    private async Task HandleRequestAsync(JsonRpcId id, string method, JsonElement? paramsElement, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling request: {Method} (id: {Id})", method, id);

        JsonRpcResponse response;
        CancellationTokenSource? requestCts = null;
        CancellationTokenSource? linkedCts = null;
        var requestCancellationToken = cancellationToken;

        if (!id.IsNull)
        {
            requestCts = new CancellationTokenSource();
            if (!_requestCancellation.TryAdd(id, requestCts))
            {
                requestCts.Dispose();
                requestCts = null;
            }
        }

        if (requestCts != null)
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, requestCts.Token);
            requestCancellationToken = linkedCts.Token;
        }

        if (_requestHandlers.TryGetValue(method, out var handler))
        {
            try
            {
                var result = await handler(paramsElement, requestCancellationToken);
                response = JsonRpcResponse.Success(id, result);
            }
            catch (OperationCanceledException)
            {
                response = JsonRpcResponse.CreateError(id, JsonRpcErrorCodes.RequestCancelled, "Request cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling request: {Method}", method);
                response = JsonRpcResponse.CreateError(id, JsonRpcErrorCodes.InternalError, ex.Message);
            }
        }
        else
        {
            _logger.LogWarning("No handler registered for request: {Method}", method);
            response = JsonRpcResponse.CreateError(id, JsonRpcErrorCodes.MethodNotFound, $"Method not found: {method}");
        }

        try
        {
            var json = JsonSerializer.Serialize(response, JsonSerializerOptionsProvider.Options);
            await _transport.WriteMessageAsync(json, cancellationToken);
        }
        finally
        {
            if (!id.IsNull && _requestCancellation.TryRemove(id, out var cts))
            {
                cts.Dispose();
            }

            linkedCts?.Dispose();
        }
    }

    private async Task HandleNotificationAsync(string method, JsonElement? paramsElement, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling notification: {Method}", method);

        if (_notificationHandlers.TryGetValue(method, out var handler))
        {
            try
            {
                await handler(paramsElement, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling notification: {Method}", method);
            }
        }
        else
        {
            _logger.LogTrace("No handler registered for notification: {Method}", method);
        }
    }

    private Task HandleCancelRequestAsync(JsonElement? paramsElement)
    {
        if (!paramsElement.HasValue)
        {
            return Task.CompletedTask;
        }

        try
        {
            var cancelParams = JsonSerializer.Deserialize<CancelParams>(
                paramsElement.Value.GetRawText(),
                JsonSerializerOptionsProvider.Options);
            if (cancelParams == null || cancelParams.Id.IsNull)
            {
                return Task.CompletedTask;
            }

            if (_requestCancellation.TryRemove(cancelParams.Id, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _logger.LogDebug("Cancelled request: {Id}", cancelParams.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process $/cancelRequest");
        }

        return Task.CompletedTask;
    }

    private static JsonRpcId ParseId(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => new JsonRpcId(element.GetString()!),
            JsonValueKind.Number => new JsonRpcId(element.GetInt64()),
            _ => default
        };
    }
}
