using FluentAssertions;
using Ingestion.Infrastructure.Cache;
using Ingestion.Infrastructure.Cache.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace Ingestion.Infrastructure.Tests.Cache;

public sealed class RedisCacheServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _multiplexerMock = new();
    private readonly Mock<IDatabase> _dbMock = new();

    private RedisCacheService CreateService(string prefix = "ingestion")
    {
        _multiplexerMock
            .Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(_dbMock.Object);

        var settings = Options.Create(new RedisSettings { KeyPrefix = prefix });
        return new RedisCacheService(
            _multiplexerMock.Object,
            settings,
            NullLogger<RedisCacheService>.Instance);
    }

    // -------------------------------------------------------------------------
    // GetLastUpdatedAtAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetLastUpdatedAtAsync_WhenKeyNotFound_ShouldReturnNull()
    {
        _dbMock
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var service = CreateService();
        var result = await service.GetLastUpdatedAtAsync("some:key", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLastUpdatedAtAsync_WhenValidDateStored_ShouldReturnParsedDateTime()
    {
        var expected = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var iso = expected.ToString("O");

        _dbMock
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(iso));

        var service = CreateService();
        var result = await service.GetLastUpdatedAtAsync("mykey", CancellationToken.None);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task GetLastUpdatedAtAsync_WhenInvalidDateStored_ShouldReturnNull()
    {
        _dbMock
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue("not-a-date"));

        var service = CreateService();
        var result = await service.GetLastUpdatedAtAsync("mykey", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLastUpdatedAtAsync_WhenRedisThrows_ShouldReturnNull()
    {
        _dbMock
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "fail"));

        var service = CreateService();
        var result = await service.GetLastUpdatedAtAsync("mykey", CancellationToken.None);

        result.Should().BeNull("Redis failures must be fail-safe");
    }

    [Fact]
    public async Task GetLastUpdatedAtAsync_ShouldUseCorrectKeyFormat()
    {
        RedisKey? capturedKey = null;

        _dbMock
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, CommandFlags>((key, _) => capturedKey = key)
            .ReturnsAsync(RedisValue.Null);

        var service = CreateService(prefix: "ingestion");
        await service.GetLastUpdatedAtAsync("TRD001_20240101_SPOT", CancellationToken.None);

        capturedKey!.Value.ToString().Should().Be("ingestion:TRD001_20240101_SPOT:updated_at");
    }

    // -------------------------------------------------------------------------
    // SetLastUpdatedAtAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetLastUpdatedAtAsync_ShouldStoreInIso8601Format()
    {
        _dbMock
            .Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var service = CreateService();
        var updatedAt = new DateTime(2024, 3, 10, 8, 0, 0, DateTimeKind.Utc);
        var expectedValue = updatedAt.ToString("O");

        await service.SetLastUpdatedAtAsync("mycomposite", updatedAt, CancellationToken.None);

        var inv = _dbMock.Invocations.FirstOrDefault(i => i.Method.Name == "StringSetAsync");
        inv.Should().NotBeNull();
        ((RedisValue)inv!.Arguments[1]).ToString().Should().Be(expectedValue);
    }

    [Fact]
    public async Task SetLastUpdatedAtAsync_ShouldNormalizeToUtc()
    {
        _dbMock
            .Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var service = CreateService();
        var unspecified = new DateTime(2024, 3, 10, 8, 0, 0, DateTimeKind.Unspecified);
        var expectedAsUtc = DateTime.SpecifyKind(unspecified, DateTimeKind.Utc).ToString("O");

        await service.SetLastUpdatedAtAsync("mycomposite", unspecified, CancellationToken.None);

        var inv = _dbMock.Invocations.FirstOrDefault(i => i.Method.Name == "StringSetAsync");
        inv.Should().NotBeNull();
        ((RedisValue)inv!.Arguments[1]).ToString().Should().Be(expectedAsUtc);
    }

    [Fact]
    public async Task SetLastUpdatedAtAsync_WhenRedisThrows_ShouldNotPropagateException()
    {
        _dbMock
            .Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "fail"));

        var service = CreateService();
        var act = async () => await service.SetLastUpdatedAtAsync("key", DateTime.UtcNow, CancellationToken.None);

        await act.Should().NotThrowAsync("cache write failures must be fail-safe");
    }

    [Fact]
    public async Task SetLastUpdatedAtAsync_ShouldUseCorrectKeyFormat()
    {
        _dbMock
            .Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var service = CreateService(prefix: "ingestion");
        await service.SetLastUpdatedAtAsync("TRD001_20240101_SPOT", DateTime.UtcNow, CancellationToken.None);

        var inv = _dbMock.Invocations.FirstOrDefault(i => i.Method.Name == "StringSetAsync");
        inv.Should().NotBeNull();
        ((RedisKey)inv!.Arguments[0]).ToString().Should().Be("ingestion:TRD001_20240101_SPOT:updated_at");
    }

    // -------------------------------------------------------------------------
    // GetQueueConfigAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetQueueConfigAsync_WhenKeyNotFound_ShouldReturnDefaults()
    {
        _dbMock
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var service = CreateService();
        var result = await service.GetQueueConfigAsync("ingestion.trade", CancellationToken.None);

        result.QueueName.Should().Be("ingestion.trade");
        result.BatchSize.Should().Be(100);
        result.ParallelConsumers.Should().Be(1);
        result.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetQueueConfigAsync_WhenValidJsonStored_ShouldReturnParsedConfig()
    {
        var json = """{"queueName":"ingestion.trade","batchSize":50,"parallelConsumers":3,"isEnabled":false}""";

        _dbMock
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(json));

        var service = CreateService();
        var result = await service.GetQueueConfigAsync("ingestion.trade", CancellationToken.None);

        result.BatchSize.Should().Be(50);
        result.ParallelConsumers.Should().Be(3);
        result.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetQueueConfigAsync_WhenRedisThrows_ShouldReturnDefaults()
    {
        _dbMock
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "fail"));

        var service = CreateService();
        var result = await service.GetQueueConfigAsync("ingestion.trade", CancellationToken.None);

        result.Should().NotBeNull("should return safe defaults on failure");
        result.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetQueueConfigAsync_WhenJsonIsNull_ShouldReturnDefaults()
    {
        _dbMock
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue("null"));

        var service = CreateService();
        var result = await service.GetQueueConfigAsync("ingestion.trade", CancellationToken.None);

        result.Should().NotBeNull();
        result.BatchSize.Should().Be(100);
    }
}
