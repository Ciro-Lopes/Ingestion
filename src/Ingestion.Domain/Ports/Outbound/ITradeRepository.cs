using Ingestion.Domain.Entities;

namespace Ingestion.Domain.Ports.Outbound;

public interface ITradeRepository
{
    Task UpsertBatchAsync(IEnumerable<Trade> trades, CancellationToken cancellationToken);
}
