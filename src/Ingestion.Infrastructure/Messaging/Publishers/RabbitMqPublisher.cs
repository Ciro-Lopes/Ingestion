using System.Text;
using System.Text.Json;
using Ingestion.Domain.Ports.Outbound;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Ingestion.Infrastructure.Messaging.Publishers;

/// <summary>
/// RabbitMQ adapter implementing <see cref="IMessagePublisher"/> using RabbitMQ.Client v7 async API.
/// Registered as a <strong>Singleton</strong>; maintains a single channel and serializes concurrent
/// publishes with a <see cref="SemaphoreSlim"/> because RabbitMQ channels are not thread-safe.
/// </summary>
public sealed class RabbitMqPublisher : IMessagePublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IConnection _connection;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly SemaphoreSlim _channelLock = new(1, 1);

    private IChannel? _channel;

    public RabbitMqPublisher(IConnection connection, ILogger<RabbitMqPublisher> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task PublishAsync<T>(
        string exchange,
        string routingKey,
        T message,
        CancellationToken cancellationToken) where T : notnull
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };

        try
        {
            var channel = await GetOrCreateChannelAsync(cancellationToken);

            await _channelLock.WaitAsync(cancellationToken);
            try
            {
                await channel.BasicPublishAsync(
                    exchange: exchange,
                    routingKey: routingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: cancellationToken);
            }
            finally
            {
                _channelLock.Release();
            }

            _logger.LogDebug(
                "Published {MessageType} to exchange '{Exchange}' with routing key '{RoutingKey}'",
                typeof(T).Name, exchange, routingKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Failed to publish {MessageType} to exchange '{Exchange}' with routing key '{RoutingKey}'",
                typeof(T).Name, exchange, routingKey);
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the existing open channel or creates a new one if it is null or closed.
    /// Uses double-checked locking to avoid unnecessary channel creation.
    /// </summary>
    private async Task<IChannel> GetOrCreateChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        await _channelLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true })
                return _channel;

            if (_channel is not null)
            {
                await _channel.CloseAsync(cancellationToken);
                _channel.Dispose();
            }

            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            _logger.LogDebug("RabbitMQ publish channel created");
            return _channel;
        }
        finally
        {
            _channelLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            if (_channel.IsOpen)
                await _channel.CloseAsync();

            _channel.Dispose();
            _channel = null;
        }

        _channelLock.Dispose();
    }
}
