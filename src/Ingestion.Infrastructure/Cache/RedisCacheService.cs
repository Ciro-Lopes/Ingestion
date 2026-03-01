using System.Text.Json;
using Ingestion.Domain.Ports.Contracts;
using Ingestion.Domain.Ports.Outbound;
using Ingestion.Infrastructure.Cache.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Ingestion.Infrastructure.Cache;

/// <summary>
/// Redis-backed implementation of <see cref="ICacheService"/>.
/// All methods are fail-safe: Redis exceptions are caught, logged, and never propagated
/// to the application layer so that a cache failure never blocks the processing flow.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly string _prefix;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public RedisCacheService(
        IConnectionMultiplexer redis,
        IOptions<RedisSettings> settings,
        ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
        _prefix = settings.Value.KeyPrefix;
    }

    // -------------------------------------------------------------------------
    // ICacheService
    // -------------------------------------------------------------------------

    public async Task<DateTime?> GetLastUpdatedAtAsync(string compositeKey, CancellationToken cancellationToken)
    {
        var key = BuildVersioningKey(compositeKey);

        try
        {
            var db = GetDatabase();
            var value = await db.StringGetAsync(key);

            if (!value.HasValue)
            {
                _logger.LogDebug("Cache miss for key '{Key}'", key);
                return null;
            }

            if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed;
            }

            _logger.LogWarning("Unable to parse cached DateTime for key '{Key}': '{Value}'", key, (string?)value);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis error in GetLastUpdatedAtAsync for key '{Key}'", key);
            return null;
        }
    }

    public async Task SetLastUpdatedAtAsync(string compositeKey, DateTime updatedAt, CancellationToken cancellationToken)
    {
        var key = BuildVersioningKey(compositeKey);
        var value = DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc).ToString("O"); // ISO 8601 round-trip

        try
        {
            var db = GetDatabase();
            await db.StringSetAsync(key, value);

            _logger.LogDebug("Cache updated for key '{Key}' with value '{Value}'", key, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis error in SetLastUpdatedAtAsync for key '{Key}'", key);
            // do not rethrow — a cache write failure must not interrupt the flow
        }
    }

    public async Task<QueueConsumerConfigDto> GetQueueConfigAsync(string queueName, CancellationToken cancellationToken)
    {
        var key = BuildQueueConfigKey(queueName);
        var fallback = DefaultQueueConfig(queueName);

        try
        {
            var db = GetDatabase();
            var json = await db.StringGetAsync(key);

            if (!json.HasValue)
            {
                _logger.LogDebug("No queue config found in Redis for key '{Key}', using defaults", key);
                return fallback;
            }

            var config = JsonSerializer.Deserialize<QueueConsumerConfigDto>(json!, _jsonOptions);

            if (config is null)
            {
                _logger.LogWarning("Deserialized null queue config for key '{Key}', using defaults", key);
                return fallback;
            }

            _logger.LogDebug("Queue config loaded from Redis for key '{Key}'", key);
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis error in GetQueueConfigAsync for key '{Key}', returning defaults", key);
            return fallback;
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private IDatabase GetDatabase() => _redis.GetDatabase();

    /// <summary>
    /// Builds the Redis versioning key: <c>{prefix}:{compositeKey}:updated_at</c>.
    /// The compositeKey is expected to be the string representation of a <c>CompositeId</c>
    /// (e.g. <c>ACME_20240101_equity</c>) as formatted by the use case before calling this service.
    /// </summary>
    private string BuildVersioningKey(string compositeKey) =>
        $"{_prefix}:{compositeKey}:updated_at";

    /// <summary>
    /// Builds the Redis queue config key: <c>{prefix}:config:{queueName}</c>.
    /// </summary>
    private string BuildQueueConfigKey(string queueName) =>
        $"{_prefix}:config:{queueName}";

    private static QueueConsumerConfigDto DefaultQueueConfig(string queueName) =>
        new(queueName, BatchSize: 100, ParallelConsumers: 1, IsEnabled: true);
}
