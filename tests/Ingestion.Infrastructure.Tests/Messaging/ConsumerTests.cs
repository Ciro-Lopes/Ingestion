using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ingestion.Application.DTOs;
using Ingestion.Domain.Ports.Contracts;
using Ingestion.Domain.Ports.Inbound;
using Ingestion.Infrastructure.Messaging.Configuration;
using Ingestion.Infrastructure.Messaging.Consumers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RabbitMQ.Client;

namespace Ingestion.Infrastructure.Tests.Messaging;

/// <summary>
/// Exposes protected members of BaseConsumer for unit testing.
/// </summary>
internal sealed class TestableTradeConsumer : BaseConsumer<TradeMessageDto>
{
    private readonly IProcessMessageUseCase<TradeMessageDto> _useCase;

    public TestableTradeConsumer(
        IConnection connection,
        QueueConsumerConfigStore configStore,
        QueueDefinition queueDefinition,
        ExchangeSettings exchanges,
        IProcessMessageUseCase<TradeMessageDto> useCase)
        : base(connection, configStore, NullLogger<TestableTradeConsumer>.Instance, queueDefinition, exchanges)
    {
        _useCase = useCase;
    }

    protected override Task ProcessAsync(TradeMessageDto dto, CancellationToken cancellationToken)
        => _useCase.ExecuteAsync(dto, cancellationToken);

    /// <summary>Exposes the protected DeserializeMessage for testing.</summary>
    public new TradeMessageDto DeserializeMessage(byte[] body) => base.DeserializeMessage(body);
}

public sealed class BaseConsumerDeserializationTests
{
    private static QueueDefinition DefaultQueueDef() => new()
    {
        Name = "ingestion.trade",
        DeadLetterQueue = "ingestion.trade.dead-letter",
        InboundRoutingKey = "trade",
        OutboundExchange = "ingestion.outbound",
        OutboundRoutingKey = "trade.processed",
        FlowName = "trade"
    };

    private static ExchangeSettings DefaultExchanges() => new()
    {
        Inbound = "ingestion.inbound"
    };

    private TestableTradeConsumer CreateConsumer(
        IProcessMessageUseCase<TradeMessageDto>? useCase = null)
    {
        var connectionMock = new Mock<IConnection>();
        var configStore = new QueueConsumerConfigStore();
        useCase ??= new Mock<IProcessMessageUseCase<TradeMessageDto>>().Object;

        return new TestableTradeConsumer(
            connectionMock.Object,
            configStore,
            DefaultQueueDef(),
            DefaultExchanges(),
            useCase);
    }

    // -------------------------------------------------------------------------
    // DeserializeMessage
    // -------------------------------------------------------------------------

    [Fact]
    public void DeserializeMessage_WithValidJson_ShouldReturnDto()
    {
        var dto = new TradeMessageDto(
            Id: "TRD001",
            Quantity: 200m,
            ReferenceDate: new DateOnly(2024, 5, 1),
            Type: "SPOT",
            Status: "ACTIVE",
            UpdatedAt: new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            RawPayload: "{}",
            MetadataPayload: "{}");

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var body = Encoding.UTF8.GetBytes(json);

        var consumer = CreateConsumer();
        var result = consumer.DeserializeMessage(body);

        result.Id.Should().Be("TRD001");
        result.Quantity.Should().Be(200m);
        result.Type.Should().Be("SPOT");
        result.Status.Should().Be("ACTIVE");
    }

    [Fact]
    public void DeserializeMessage_WithInvalidJson_ShouldThrow()
    {
        var body = Encoding.UTF8.GetBytes("not json at all");
        var consumer = CreateConsumer();

        var act = () => consumer.DeserializeMessage(body);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void DeserializeMessage_WithNullJson_ShouldThrowInvalidOperationException()
    {
        var body = Encoding.UTF8.GetBytes("null");
        var consumer = CreateConsumer();

        var act = () => consumer.DeserializeMessage(body);
        act.Should().Throw<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // ProcessAsync delegation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ShouldDelegateToUseCase()
    {
        var useCaseMock = new Mock<IProcessMessageUseCase<TradeMessageDto>>();
        useCaseMock
            .Setup(u => u.ExecuteAsync(It.IsAny<TradeMessageDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = CreateConsumer(useCaseMock.Object);

        var dto = new TradeMessageDto("ID", 1, new DateOnly(2024, 1, 1), "T", "S",
            DateTime.UtcNow, "{}", "{}");

        // Call via reflection since ProcessAsync is protected
        var method = typeof(TestableTradeConsumer)
            .GetMethod("ProcessAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(consumer, new object[] { dto, CancellationToken.None })!;

        useCaseMock.Verify(u => u.ExecuteAsync(dto, CancellationToken.None), Times.Once);
    }
}

public sealed class QueueConsumerConfigStoreTests
{
    [Fact]
    public void GetConfig_WhenQueueNotRegistered_ShouldReturnDefaults()
    {
        var store = new QueueConsumerConfigStore();
        var config = store.GetConfig("unknown.queue");

        config.QueueName.Should().Be("unknown.queue");
        config.BatchSize.Should().Be(100);
        config.ParallelConsumers.Should().Be(1);
        config.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void GetConfig_AfterUpdateConfig_ShouldReturnUpdatedConfig()
    {
        var store = new QueueConsumerConfigStore();
        var dto = new QueueConsumerConfigDto("ingestion.trade", BatchSize: 50, ParallelConsumers: 3, IsEnabled: false);

        store.UpdateConfig("ingestion.trade", dto);
        var result = store.GetConfig("ingestion.trade");

        result.BatchSize.Should().Be(50);
        result.ParallelConsumers.Should().Be(3);
        result.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void UpdateConfig_CalledTwice_ShouldReplaceConfig()
    {
        var store = new QueueConsumerConfigStore();

        store.UpdateConfig("q", new QueueConsumerConfigDto("q", 10, 1, true));
        store.UpdateConfig("q", new QueueConsumerConfigDto("q", 99, 5, false));

        var result = store.GetConfig("q");
        result.BatchSize.Should().Be(99);
        result.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void GetConfig_ForDifferentQueues_ShouldReturnCorrectValues()
    {
        var store = new QueueConsumerConfigStore();

        store.UpdateConfig("queue-a", new QueueConsumerConfigDto("queue-a", 20, 2, true));
        store.UpdateConfig("queue-b", new QueueConsumerConfigDto("queue-b", 40, 4, false));

        store.GetConfig("queue-a").BatchSize.Should().Be(20);
        store.GetConfig("queue-b").BatchSize.Should().Be(40);
    }

    [Fact]
    public void GetConfig_IsThreadSafe_ShouldNotThrow()
    {
        var store = new QueueConsumerConfigStore();

        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            store.UpdateConfig($"queue-{i % 5}", new QueueConsumerConfigDto($"queue-{i % 5}", i, 1, true));
            _ = store.GetConfig($"queue-{i % 5}");
        }));

        var act = async () => await Task.WhenAll(tasks);
        act.Should().NotThrowAsync();
    }
}
