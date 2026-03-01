using Ingestion.Application.DTOs;
using Ingestion.Application.Mappers;
using Ingestion.Domain.Entities;
using Ingestion.Domain.Ports.Inbound;
using Ingestion.Domain.Ports.Outbound;
using Ingestion.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingestion.Application.UseCases;

public sealed class ProcessTradeUseCase : IProcessMessageUseCase<TradeMessageDto>
{
    private readonly ICacheService _cacheService;
    private readonly ITradeRepository _tradeRepository;
    private readonly IMessagePublisher _messagePublisher;
    private readonly ILogger<ProcessTradeUseCase> _logger;
    private readonly OutboundSettings _outbound;
    private readonly BatchProcessor<Trade> _batchProcessor;

    public ProcessTradeUseCase(
        ICacheService cacheService,
        ITradeRepository tradeRepository,
        IMessagePublisher messagePublisher,
        IOptions<BatchSettings> batchSettings,
        IOptions<OutboundSettings> outboundSettings,
        ILogger<ProcessTradeUseCase> logger)
    {
        _cacheService = cacheService;
        _tradeRepository = tradeRepository;
        _messagePublisher = messagePublisher;
        _logger = logger;
        _outbound = outboundSettings.Value;

        var settings = batchSettings.Value;

        _batchProcessor = new BatchProcessor<Trade>(
            batchSize: settings.DefaultSize,
            flushIntervalSeconds: settings.FlushIntervalSeconds,
            flushDelegate: FlushBatchAsync);
    }

    /// <summary>
    /// Starts the internal batch processing loop. Must be called once by the hosting Worker.
    /// Runs until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
        => _batchProcessor.StartProcessingAsync(cancellationToken);

    public async Task ExecuteAsync(TradeMessageDto message, CancellationToken cancellationToken)
    {
        var compositeId = new CompositeId(message.Id, message.ReferenceDate, message.Type);
        var key = compositeId.ToString();

        var cachedUpdatedAt = await _cacheService.GetLastUpdatedAtAsync(key, cancellationToken);

        if (cachedUpdatedAt.HasValue && message.UpdatedAt <= cachedUpdatedAt.Value)
        {
            _logger.LogDebug(
                "Trade {CompositeId} discarded — incoming UpdatedAt {IncomingUpdatedAt} is not newer than cached {CachedUpdatedAt}",
                key, message.UpdatedAt, cachedUpdatedAt.Value);
            return;
        }

        var entity = TradeMapper.ToEntity(message);

        var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await _batchProcessor.EnqueueAsync(entity, ack, cancellationToken);

        // Await here so the consumer ACKs the RabbitMQ message only after the batch is persisted.
        await ack.Task;
    }

    private async Task FlushBatchAsync(IReadOnlyList<Trade> batch, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Flushing trade batch with {Count} item(s)", batch.Count);

        await _tradeRepository.UpsertBatchAsync(batch, cancellationToken);

        foreach (var trade in batch)
        {
            var key = trade.CompositeId.ToString();

            await _cacheService.SetLastUpdatedAtAsync(key, trade.UpdatedAt, cancellationToken);

            var outboundDto = new TradeOutboundDto(key, trade.UpdatedAt);

            await _messagePublisher.PublishAsync(
                _outbound.TradeExchange,
                _outbound.TradeRoutingKey,
                outboundDto,
                cancellationToken);
        }

        _logger.LogInformation("Trade batch of {Count} item(s) persisted and published", batch.Count);
    }
}
