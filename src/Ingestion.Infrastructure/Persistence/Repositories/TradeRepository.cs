using Dapper;
using Ingestion.Domain.Entities;
using Ingestion.Domain.Ports.Outbound;
using Microsoft.Extensions.Logging;

namespace Ingestion.Infrastructure.Persistence.Repositories;

public sealed class TradeRepository : ITradeRepository
{
    private const string UpsertSql = """
        INSERT INTO trades (id, quantity, reference_date, type, status, raw_message, metadata, created_at, updated_at)
        VALUES (@Id, @Quantity, @ReferenceDate, @Type, @Status, @RawMessage::jsonb, @Metadata::jsonb, @CreatedAt, @UpdatedAt)
        ON CONFLICT (id) DO UPDATE
        SET quantity       = EXCLUDED.quantity,
            status         = EXCLUDED.status,
            raw_message    = EXCLUDED.raw_message,
            metadata       = EXCLUDED.metadata,
            updated_at     = EXCLUDED.updated_at
        WHERE trades.updated_at < EXCLUDED.updated_at;
        """;

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<TradeRepository> _logger;

    public TradeRepository(IDbConnectionFactory connectionFactory, ILogger<TradeRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task UpsertBatchAsync(IEnumerable<Trade> trades, CancellationToken cancellationToken)
    {
        var parameters = trades.Select(t => new
        {
            Id = t.CompositeId.ToString(),
            t.Quantity,
            ReferenceDate = t.ReferenceDate.ToDateTime(TimeOnly.MinValue),
            t.Type,
            t.Status,
            t.RawMessage,
            t.Metadata,
            t.CreatedAt,
            t.UpdatedAt,
        }).ToList();

        _logger.LogDebug("Upserting trade batch with {Count} item(s)", parameters.Count);

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(UpsertSql, parameters, transaction);
            transaction.Commit();

            _logger.LogInformation("Trade batch of {Count} item(s) upserted successfully", parameters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert trade batch of {Count} item(s). Rolling back", parameters.Count);
            transaction.Rollback();
            throw;
        }
    }
}
