// Production connection string must be supplied via environment variables (e.g. Database__ConnectionString).
// The value below matches the local docker-compose.yml setup only.
namespace Ingestion.Infrastructure.Persistence.Configuration;

public class DatabaseSettings
{
    public string ConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=ingestion;Username=postgres;Password=postgres";
}
