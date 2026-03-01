using FluentAssertions;
using Ingestion.Application.DTOs;
using Ingestion.Application.UseCases;
using Ingestion.Domain.Entities;
using Ingestion.Domain.Ports.Inbound;
using Ingestion.Domain.Ports.Outbound;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Ingestion.Application.Tests.UseCases;

public sealed class ProcessTradeUseCaseTests : IAsyncDisposable
{
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly Mock<ITradeRepository> _repoMock = new();
    private readonly Mock<IMessagePublisher> _publisherMock = new();

    private readonly IOptions<BatchSettings> _batchSettings =
        Options.Create(new BatchSettings { DefaultSize = 10, FlushIntervalSeconds = 1 });

    private readonly IOptions<OutboundSettings> _outboundSettings =
        Options.Create(new OutboundSettings { TradeExchange = "test.exchange", TradeRoutingKey = "test.key" });

    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    private ProcessTradeUseCase CreateUseCase()
    {
        return new ProcessTradeUseCase(
            _cacheMock.Object,
            _repoMock.Object,
            _publisherMock.Object,
            _batchSettings,
            _outboundSettings,
            NullLogger<ProcessTradeUseCase>.Instance);
    }

    private static TradeMessageDto BuildMessage(
        string id = "TRD001",
        string type = "SPOT",
        DateOnly referenceDate = default,
        DateTime updatedAt = default)
    {
        return new TradeMessageDto(
            Id: id,
            Quantity: 100m,
            ReferenceDate: referenceDate == default ? new DateOnly(2024, 1, 1) : referenceDate,
            Type: type,
            Status: "ACTIVE",
            UpdatedAt: updatedAt == default ? new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc) : updatedAt,
            RawPayload: "{}",
            MetadataPayload: "{}"
        );
    }

    private async Task StartUseCaseAsync(ProcessTradeUseCase useCase)
    {
        _loopTask = Task.Run(() => useCase.StartAsync(_cts.Token));
        await Task.Delay(50); // allow loop to start
    }

    // -------------------------------------------------------------------------
    // Cache discard logic
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenMessageIsOlderThanCache_ShouldDiscard()
    {
        var cachedAt = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var incomingAt = new DateTime(2024, 6, 1, 9, 0, 0, DateTimeKind.Utc); // older

        _cacheMock
            .Setup(c => c.GetLastUpdatedAtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedAt);

        var useCase = CreateUseCase();
        await StartUseCaseAsync(useCase);

        await useCase.ExecuteAsync(BuildMessage(updatedAt: incomingAt), CancellationToken.None);

        _repoMock.Verify(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<Trade>>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisherMock.Verify(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMessageHasSameUpdatedAtAsCache_ShouldDiscard()
    {
        var sameAt = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        _cacheMock
            .Setup(c => c.GetLastUpdatedAtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sameAt);

        var useCase = CreateUseCase();
        await StartUseCaseAsync(useCase);

        await useCase.ExecuteAsync(BuildMessage(updatedAt: sameAt), CancellationToken.None);

        _repoMock.Verify(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<Trade>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCacheIsEmpty_ShouldProcess()
    {
        _cacheMock
            .Setup(c => c.GetLastUpdatedAtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)null);

        _repoMock
            .Setup(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<Trade>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var useCase = CreateUseCase();
        await StartUseCaseAsync(useCase);

        await useCase.ExecuteAsync(BuildMessage(), CancellationToken.None);

        _repoMock.Verify(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<Trade>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMessageIsNewer_ShouldProcess()
    {
        var cachedAt = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var newerAt = new DateTime(2024, 6, 1, 11, 0, 0, DateTimeKind.Utc);

        _cacheMock
            .Setup(c => c.GetLastUpdatedAtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedAt);

        _repoMock
            .Setup(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<Trade>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var useCase = CreateUseCase();
        await StartUseCaseAsync(useCase);

        await useCase.ExecuteAsync(BuildMessage(updatedAt: newerAt), CancellationToken.None);

        _repoMock.Verify(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<Trade>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Persistence and publishing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ShouldPersistTrade()
    {
        IEnumerable<Trade>? capturedBatch = null;

        _cacheMock
            .Setup(c => c.GetLastUpdatedAtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)null);

        _repoMock
            .Setup(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<Trade>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Trade>, CancellationToken>((batch, _) => capturedBatch = batch)
            .Returns(Task.CompletedTask);

        var useCase = CreateUseCase();
        await StartUseCaseAsync(useCase);

        var msg = BuildMessage(id: "TRD123", type: "FUT");
        await useCase.ExecuteAsync(msg, CancellationToken.None);

        capturedBatch.Should().NotBeNull();
        capturedBatch!.Should().ContainSingle();
        capturedBatch!.First().CompositeId.Id.Should().Be("TRD123");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUpdateCacheAfterPersistence()
    {
        _cacheMock
            .Setup(c => c.GetLastUpdatedAtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)null);

        _repoMock
            .Setup(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<Trade>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var useCase = CreateUseCase();
        await StartUseCaseAsync(useCase);

        await useCase.ExecuteAsync(BuildMessage(), CancellationToken.None);

        _cacheMock.Verify(
            c => c.SetLastUpdatedAtAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPublishToOutboundExchange()
    {
        _cacheMock
            .Setup(c => c.GetLastUpdatedAtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)null);

        _repoMock
            .Setup(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<Trade>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var useCase = CreateUseCase();
        await StartUseCaseAsync(useCase);

        await useCase.ExecuteAsync(BuildMessage(), CancellationToken.None);

        _publisherMock.Verify(
            p => p.PublishAsync("test.exchange", "test.key", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleMsgs_ShouldPersistAsBatch()
    {
        var capturedBatches = new List<IEnumerable<Trade>>();

        _cacheMock
            .Setup(c => c.GetLastUpdatedAtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)null);

        _repoMock
            .Setup(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<Trade>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Trade>, CancellationToken>((b, _) => capturedBatches.Add(b.ToList()))
            .Returns(Task.CompletedTask);

        var settings = Options.Create(new BatchSettings { DefaultSize = 3, FlushIntervalSeconds = 10 });
        var useCase = new ProcessTradeUseCase(
            _cacheMock.Object, _repoMock.Object, _publisherMock.Object,
            settings, _outboundSettings, NullLogger<ProcessTradeUseCase>.Instance);

        await StartUseCaseAsync(useCase);

        var tasks = new[]
        {
            useCase.ExecuteAsync(BuildMessage("A"), CancellationToken.None),
            useCase.ExecuteAsync(BuildMessage("B"), CancellationToken.None),
            useCase.ExecuteAsync(BuildMessage("C"), CancellationToken.None),
        };

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        capturedBatches.Should().HaveCount(1);
        capturedBatches.First().Should().HaveCount(3);
    }

    // -------------------------------------------------------------------------
    // Error propagation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenRepositoryThrows_ShouldPropagateException()
    {
        _cacheMock
            .Setup(c => c.GetLastUpdatedAtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)null);

        _repoMock
            .Setup(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<Trade>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var settings = Options.Create(new BatchSettings { DefaultSize = 1, FlushIntervalSeconds = 10 });
        var useCase = new ProcessTradeUseCase(
            _cacheMock.Object, _repoMock.Object, _publisherMock.Object,
            settings, _outboundSettings, NullLogger<ProcessTradeUseCase>.Instance);

        await StartUseCaseAsync(useCase);

        var act = async () => await useCase.ExecuteAsync(BuildMessage(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB error");
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_loopTask is not null)
        {
            try { await _loopTask.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { /* ignore */ }
        }
        _cts.Dispose();
    }
}
