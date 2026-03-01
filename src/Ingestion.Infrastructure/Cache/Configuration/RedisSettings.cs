// Production connection string must be supplied via environment variables (e.g. Redis__ConnectionString).
// The value below matches the local docker-compose.yml setup only.
namespace Ingestion.Infrastructure.Cache.Configuration;

public class RedisSettings
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public int ConfigPollingIntervalSeconds { get; set; } = 30;
    public string KeyPrefix { get; set; } = "ingestion";
}
