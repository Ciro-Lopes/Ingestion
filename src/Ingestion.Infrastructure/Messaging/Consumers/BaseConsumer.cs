using System.Text;
using System.Text.Json;
using Ingestion.Infrastructure.Messaging.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Ingestion.Infrastructure.Messaging.Consumers;

/// <summary>
/// Abstract base class for all RabbitMQ consumers.
/// Handles channel creation, queue/DLQ declaration, QoS, message dispatch,
/// ACK/NACK and routing to the DLQ on unhandled exceptions.
///
/// Each call to <see cref="StartConsumingAsync"/> creates its own dedicated <see cref="IChannel"/>
/// — RabbitMQ channels are not thread-safe and must not be shared across consumers.
/// </summary>
public abstract class BaseConsumer<TDto>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string DlxExchange = "ingestion.dlx";

    private readonly IConnection _connection;
    private readonly QueueConsumerConfigStore _configStore;
    private readonly QueueDefinition _queueDefinition;
    private readonly ExchangeSettings _exchanges;

    protected ILogger Logger { get; }

    protected BaseConsumer(
        IConnection connection,
        QueueConsumerConfigStore configStore,
        ILogger logger,
        QueueDefinition queueDefinition,
        ExchangeSettings exchanges)
    {
        _connection = connection;
        _configStore = configStore;
        Logger = logger;
        _queueDefinition = queueDefinition;
        _exchanges = exchanges;
    }

    /// <summary>
    /// Declares exchanges and queues, registers the async consumer, and blocks until
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public async Task StartConsumingAsync(CancellationToken cancellationToken)
    {
        var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await DeclareTopologyAsync(channel, cancellationToken);

        var config = _configStore.GetConfig(_queueDefinition.Name);
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: (ushort)config.BatchSize,
            global: false,
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (_, ea) =>
        {
            var currentConfig = _configStore.GetConfig(_queueDefinition.Name);

            if (!currentConfig.IsEnabled)
            {
                Logger.LogDebug("Consumer for '{Queue}' is disabled — nacking with requeue", _queueDefinition.Name);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: CancellationToken.None);
                await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
                return;
            }

            TDto? dto = default;

            try
            {
                dto = DeserializeMessage(ea.Body.ToArray());
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "Deserialization failed for message from '{Queue}'. Sending to DLQ",
                    _queueDefinition.Name);

                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
                return;
            }

            try
            {
                await ProcessAsync(dto!, cancellationToken);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Service is shutting down — nack with requeue so the message is not lost.
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "Unhandled exception processing message from '{Queue}'. Sending to DLQ",
                    _queueDefinition.Name);

                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
            }
        };

        await channel.BasicConsumeAsync(
            queue: _queueDefinition.Name,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        Logger.LogInformation("Started consuming from '{Queue}'", _queueDefinition.Name);

        // Hold the task open until the host requests shutdown.
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation("Consumer for '{Queue}' is stopping", _queueDefinition.Name);
        }
        finally
        {
            if (channel.IsOpen)
                await channel.CloseAsync();

            channel.Dispose();
        }
    }

    /// <summary>Override to process a successfully deserialized message.</summary>
    protected abstract Task ProcessAsync(TDto dto, CancellationToken cancellationToken);

    /// <summary>Deserializes the raw message body into <typeparamref name="TDto"/>.</summary>
    protected virtual TDto DeserializeMessage(byte[] body)
    {
        var json = Encoding.UTF8.GetString(body);
        TDto dto = JsonSerializer.Deserialize<TDto>(json, JsonOptions)
               ?? throw new InvalidOperationException(
                   $"Deserialization of {typeof(TDto).Name} returned null for payload: {json}");
        return dto;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken ct)
    {
        // Declare dead-letter exchange
        await channel.ExchangeDeclareAsync(
            exchange: DlxExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        // Declare DLQ
        await channel.QueueDeclareAsync(
            queue: _queueDefinition.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: ct);

        // Bind DLQ to DLX exchange
        await channel.QueueBindAsync(
            queue: _queueDefinition.DeadLetterQueue,
            exchange: DlxExchange,
            routingKey: _queueDefinition.DeadLetterQueue,
            cancellationToken: ct);

        // Declare main queue with DLX routing arguments
        var queueArgs = new Dictionary<string, object?>
        {
            { "x-dead-letter-exchange", DlxExchange },
            { "x-dead-letter-routing-key", _queueDefinition.DeadLetterQueue }
        };

        await channel.QueueDeclareAsync(
            queue: _queueDefinition.Name,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArgs,
            cancellationToken: ct);

        // Declare inbound exchange and bind the main queue to it
        await channel.ExchangeDeclareAsync(
            exchange: _exchanges.Inbound,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: _queueDefinition.Name,
            exchange: _exchanges.Inbound,
            routingKey: _queueDefinition.InboundRoutingKey,
            cancellationToken: ct);

        // Declare outbound exchange so it exists before the publisher uses it
        await channel.ExchangeDeclareAsync(
            exchange: _queueDefinition.OutboundExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        Logger.LogDebug(
            "Topology declared — queue: '{Queue}', inbound: '{Inbound}' -> '{RoutingKey}', outbound: '{Outbound}', DLQ: '{Dlq}'",
            _queueDefinition.Name, _exchanges.Inbound, _queueDefinition.InboundRoutingKey,
            _queueDefinition.OutboundExchange, _queueDefinition.DeadLetterQueue);
    }
}
