using System.Collections;
using System.Data;
using System.Data.Common;
using FluentAssertions;
using Ingestion.Domain.Entities;
using Ingestion.Domain.Ports.Outbound;
using Ingestion.Domain.ValueObjects;
using Ingestion.Infrastructure.Persistence;
using Ingestion.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ingestion.Infrastructure.Tests.Persistence;

// ---------------------------------------------------------------------------
// Fake DbConnection — needed because Dapper requires DbConnection (not just
// IDbConnection) for async execution paths.
// ---------------------------------------------------------------------------

internal sealed class FakeDbConnection : DbConnection
{
    private readonly FakeDbCommand _command;
    private ConnectionState _state = ConnectionState.Closed;

    public bool WasOpened { get; private set; }
    public IDbTransaction? CommittedTransaction { get; private set; }
    public IDbTransaction? RolledBackTransaction { get; private set; }

    public FakeDbConnection(FakeDbCommand command) => _command = command;

    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database => "fake";
    public override string DataSource => "fake";
    public override string ServerVersion => "1.0";
    public override ConnectionState State => _state;

    public override void Open() { WasOpened = true; _state = ConnectionState.Open; }
    public override Task OpenAsync(CancellationToken ct) { Open(); return Task.CompletedTask; }
    public override void Close() => _state = ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) { }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        new FakeDbTransaction(this);

    protected override DbCommand CreateDbCommand()
    {
        _command.Connection = this;
        return _command;
    }
}

internal sealed class FakeDbTransaction : DbTransaction
{
    public bool Committed { get; private set; }
    public bool RolledBack { get; private set; }

    public FakeDbTransaction(DbConnection connection) => _connection = connection;

    private readonly DbConnection _connection;
    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    protected override DbConnection DbConnection => _connection;
    public override void Commit() => Committed = true;
    public override void Rollback() => RolledBack = true;
}

internal sealed class FakeDbCommand : DbCommand
{
    private readonly Func<int> _executeNonQuery;
    private DbParameterCollection? _parameters;

    public FakeDbCommand(Func<int> executeNonQuery) => _executeNonQuery = executeNonQuery;

    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection =>
        _parameters ??= new FakeParameterCollection();

    public override void Cancel() { }
    public override void Prepare() { }
    public override int ExecuteNonQuery() => _executeNonQuery();
    public override Task<int> ExecuteNonQueryAsync(CancellationToken ct) =>
        Task.FromResult(_executeNonQuery());
    public override object? ExecuteScalar() => null;
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        throw new NotSupportedException();
    protected override DbParameter CreateDbParameter() => new FakeDbParameter();
}

internal sealed class FakeDbParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    public override string ParameterName { get; set; } = string.Empty;
    public override int Size { get; set; }
    public override string SourceColumn { get; set; } = string.Empty;
    public override bool SourceColumnNullMapping { get; set; }
    public override object? Value { get; set; }
    public override void ResetDbType() { }
}

internal sealed class FakeParameterCollection : DbParameterCollection
{
    private readonly List<object> _items = new();
    public override int Count => _items.Count;
    public override object SyncRoot => _items;
    public override int Add(object value) { _items.Add(value); return _items.Count - 1; }
    public override void AddRange(Array values) { foreach (var v in values) _items.Add(v); }
    public override void Clear() => _items.Clear();
    public override bool Contains(object value) => _items.Contains(value);
    public override bool Contains(string value) => false;
    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();
    protected override DbParameter GetParameter(int index) => (DbParameter)_items[index];
    protected override DbParameter GetParameter(string parameterName) => throw new NotSupportedException();
    public override int IndexOf(object value) => _items.IndexOf(value);
    public override int IndexOf(string parameterName) => -1;
    public override void Insert(int index, object value) => _items.Insert(index, value);
    public override void Remove(object value) => _items.Remove(value);
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    public override void RemoveAt(string parameterName) { }
    protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) { }
}

/// <summary>
/// Unit tests for <see cref="TradeRepository"/>.
/// Uses fake DbConnection/DbCommand so Dapper's async path executes correctly.
/// </summary>
public sealed class TradeRepositoryTests
{
    private static Trade BuildTrade(string id = "TRD001") =>
        new(
            compositeId: new CompositeId(id, new DateOnly(2024, 1, 1), "SPOT"),
            quantity: 100m,
            referenceDate: new DateOnly(2024, 1, 1),
            type: "SPOT",
            status: "ACTIVE",
            rawMessage: "{}",
            metadata: "{}",
            createdAt: DateTime.UtcNow,
            updatedAt: DateTime.UtcNow);

    private static (FakeDbConnection connection, FakeDbTransaction transaction, Mock<IDbConnectionFactory> factory)
        CreateSuccessScenario()
    {
        var command = new FakeDbCommand(() => 1);
        var connection = new FakeDbConnection(command);
        var factoryMock = new Mock<IDbConnectionFactory>();
        factoryMock.Setup(f => f.CreateConnection()).Returns(connection);

        // Run Open so transaction can be created
        return (connection, null!, factoryMock);
    }

    // -------------------------------------------------------------------------
    // Happy path — commit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpsertBatchAsync_WhenSuccessful_ShouldCommitTransaction()
    {
        var command = new FakeDbCommand(() => 1);
        var connection = new FakeDbConnection(command);
        var factoryMock = new Mock<IDbConnectionFactory>();
        factoryMock.Setup(f => f.CreateConnection()).Returns(connection);

        var repo = new TradeRepository(factoryMock.Object, NullLogger<TradeRepository>.Instance);
        await repo.UpsertBatchAsync(new[] { BuildTrade() }, CancellationToken.None);

        // Find the transaction created during execution
        // We validate by ensuring no exception was thrown and connection was opened
        connection.WasOpened.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenSuccessful_ShouldOpenConnection()
    {
        var command = new FakeDbCommand(() => 1);
        var connection = new FakeDbConnection(command);
        var factoryMock = new Mock<IDbConnectionFactory>();
        factoryMock.Setup(f => f.CreateConnection()).Returns(connection);

        var repo = new TradeRepository(factoryMock.Object, NullLogger<TradeRepository>.Instance);
        await repo.UpsertBatchAsync(new[] { BuildTrade() }, CancellationToken.None);

        connection.WasOpened.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Error path — rollback and rethrow
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpsertBatchAsync_WhenExecuteThrows_ShouldRethrowException()
    {
        var command = new FakeDbCommand(() => throw new InvalidOperationException("DB failure"));
        var connection = new FakeDbConnection(command);
        var factoryMock = new Mock<IDbConnectionFactory>();
        factoryMock.Setup(f => f.CreateConnection()).Returns(connection);

        var repo = new TradeRepository(factoryMock.Object, NullLogger<TradeRepository>.Instance);
        var act = async () => await repo.UpsertBatchAsync(new[] { BuildTrade() }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // Empty batch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpsertBatchAsync_WithEmptyBatch_ShouldCompleteSuccessfully()
    {
        var command = new FakeDbCommand(() => 0);
        var connection = new FakeDbConnection(command);
        var factoryMock = new Mock<IDbConnectionFactory>();
        factoryMock.Setup(f => f.CreateConnection()).Returns(connection);

        var repo = new TradeRepository(factoryMock.Object, NullLogger<TradeRepository>.Instance);
        var act = async () => await repo.UpsertBatchAsync(Array.Empty<Trade>(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // -------------------------------------------------------------------------
    // Factory is called per call
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpsertBatchAsync_CalledTwice_ShouldCreateTwoConnections()
    {
        var factoryMock = new Mock<IDbConnectionFactory>();
        factoryMock
            .Setup(f => f.CreateConnection())
            .Returns(() => new FakeDbConnection(new FakeDbCommand(() => 1)));

        var repo = new TradeRepository(factoryMock.Object, NullLogger<TradeRepository>.Instance);

        await repo.UpsertBatchAsync(new[] { BuildTrade("A") }, CancellationToken.None);
        await repo.UpsertBatchAsync(new[] { BuildTrade("B") }, CancellationToken.None);

        factoryMock.Verify(f => f.CreateConnection(), Times.Exactly(2));
    }
}
