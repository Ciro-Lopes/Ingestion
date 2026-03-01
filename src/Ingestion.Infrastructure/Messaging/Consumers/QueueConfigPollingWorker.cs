using Ingestion.Domain.Ports.Outbound;
using Ingestion.Infrastructure.Cache.Configuration;
using Ingestion.Infrastructure.Messaging.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMqConfig = Ingestion.Infrastructure.Messaging.Configuration.RabbitMqSettings;

namespace Ingestion.Infrastructure.Messaging.Consumers;

/// <summary>
/// Background service that polls Redis periodically to refresh queue consumer configuration
/// in the <see cref="QueueConsumerConfigStore"/> without requiring a service restart.
/// </summary>
public sealed class QueueConfigPollingWorker : BackgroundService
{
    private readonly ICacheService _cacheService;
    private readonly QueueConsumerConfigStore _store;
    private readonly RabbitMqConfig _rabbitMqSettings;
    private readonly TimeSpan _pollingInterval;
    private readonly ILogger<QueueConfigPollingWorker> _logger;

    public QueueConfigPollingWorker(
        ICacheService cacheService,
        QueueConsumerConfigStore store,
        IOptions<RabbitMqConfig> rabbitMqSettings,
        IOptions<RedisSettings> redisSettings,
        ILogger<QueueConfigPollingWorker> logger)
    {
        _cacheService = cacheService;
        _store = store;
        _rabbitMqSettings = rabbitMqSettings.Value;
        _pollingInterval = TimeSpan.FromSeconds(redisSettings.Value.ConfigPollingIntervalSeconds);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "QueueConfigPollingWorker started. Polling interval: {Interval}s",
            _pollingInterval.TotalSeconds);

        using var timer = new PeriodicTimer(_pollingInterval);

        // Run once immediately at startup, then follow the timer cadence.
        await PollAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PollAsync(stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        foreach (var queue in _rabbitMqSettings.Queues)
        {
            try
            {
                var config = await _cacheService.GetQueueConfigAsync(queue.Name, ct);
                _store.UpdateConfig(queue.Name, config);

                _logger.LogDebug(
                    "Queue config refreshed for '{Queue}': BatchSize={BatchSize}, ParallelConsumers={Parallel}, IsEnabled={Enabled}",
                    queue.Name, config.BatchSize, config.ParallelConsumers, config.IsEnabled);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh queue config for '{Queue}'. Keeping previous value", queue.Name);
            }
        }
    }
}
