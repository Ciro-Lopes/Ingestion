using System.Collections.Concurrent;
using Ingestion.Domain.Ports.Contracts;

namespace Ingestion.Infrastructure.Messaging.Configuration;

/// <summary>
/// Thread-safe in-memory registry of per-queue consumer configuration.
/// Updated periodically by <c>QueueConfigPollingWorker</c> and read by consumers at runtime.
/// </summary>
public sealed class QueueConsumerConfigStore
{
    private readonly ConcurrentDictionary<string, QueueConsumerConfigDto> _store = new();

    /// <summary>Adds or replaces the configuration for <paramref name="queueName"/>.</summary>
    public void UpdateConfig(string queueName, QueueConsumerConfigDto config)
        => _store[queueName] = config;

    /// <summary>
    /// Returns the stored configuration for <paramref name="queueName"/>,
    /// or safe defaults (<c>BatchSize=100, ParallelConsumers=1, IsEnabled=true</c>) if not found.
    /// </summary>
    public QueueConsumerConfigDto GetConfig(string queueName)
        => _store.TryGetValue(queueName, out var config)
            ? config
            : new QueueConsumerConfigDto(queueName, BatchSize: 100, ParallelConsumers: 1, IsEnabled: true);
}
