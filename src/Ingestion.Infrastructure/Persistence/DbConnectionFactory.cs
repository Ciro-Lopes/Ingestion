using System.Data;
using Ingestion.Infrastructure.Persistence.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ingestion.Infrastructure.Persistence;

/// <summary>
/// Abstraction over database connection creation — allows mocking in tests.
/// The returned connection is <strong>not</strong> opened; the caller is responsible for opening it.
/// </summary>
public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}

/// <summary>
/// Creates <see cref="NpgsqlConnection"/> instances using the configured connection string.
/// </summary>
public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(IOptions<DatabaseSettings> settings)
    {
        _connectionString = settings.Value.ConnectionString;
    }

    public IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
}
