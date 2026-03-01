using Ingestion.Infrastructure.Messaging.Consumers;

namespace Ingestion.Worker.Workers;

public sealed class TradeWorker : BackgroundService
{
    private readonly TradeConsumer _consumer;
    private readonly ILogger<TradeWorker> _logger;

    public TradeWorker(TradeConsumer consumer, ILogger<TradeWorker> logger)
    {
        _consumer = consumer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TradeWorker starting");
        try
        {
            await _consumer.StartConsumingAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on graceful shutdown — no action needed.
        }
        _logger.LogInformation("TradeWorker stopped");
    }
}
