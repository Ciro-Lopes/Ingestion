using Ingestion.Domain.Ports.Contracts;

namespace Ingestion.Domain.Ports.Outbound;

public interface ICacheService
{
    Task<DateTime?> GetLastUpdatedAtAsync(string compositeKey, CancellationToken cancellationToken);
    Task SetLastUpdatedAtAsync(string compositeKey, DateTime updatedAt, CancellationToken cancellationToken);
    Task<QueueConsumerConfigDto> GetQueueConfigAsync(string queueName, CancellationToken cancellationToken);
}
