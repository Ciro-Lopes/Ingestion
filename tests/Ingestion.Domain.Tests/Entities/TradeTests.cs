using FluentAssertions;
using Ingestion.Domain.Entities;
using Ingestion.Domain.ValueObjects;

namespace Ingestion.Domain.Tests.Entities;

public sealed class TradeTests
{
    private static readonly CompositeId DefaultCompositeId =
        new("ID1", new DateOnly(2024, 1, 15), "SPOT");

    private static Trade BuildTrade(
        CompositeId? compositeId = null,
        decimal quantity = 100m,
        DateOnly referenceDate = default,
        string type = "SPOT",
        string status = "ACTIVE",
        string rawMessage = "{}",
        string metadata = "{}",
        DateTime createdAt = default,
        DateTime updatedAt = default)
    {
        return new Trade(
            compositeId: compositeId ?? DefaultCompositeId,
            quantity: quantity,
            referenceDate: referenceDate == default ? new DateOnly(2024, 1, 15) : referenceDate,
            type: type,
            status: status,
            rawMessage: rawMessage,
            metadata: metadata,
            createdAt: createdAt == default ? DateTime.UtcNow : createdAt,
            updatedAt: updatedAt == default ? DateTime.UtcNow : updatedAt
        );
    }

    [Fact]
    public void Constructor_ShouldSetAllProperties()
    {
        var compositeId = new CompositeId("TRD01", new DateOnly(2024, 5, 10), "FUT");
        var refDate = new DateOnly(2024, 5, 10);
        var createdAt = new DateTime(2024, 5, 10, 12, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2024, 5, 10, 13, 0, 0, DateTimeKind.Utc);

        var trade = new Trade(
            compositeId: compositeId,
            quantity: 250.5m,
            referenceDate: refDate,
            type: "FUT",
            status: "CLOSED",
            rawMessage: "{\"key\":\"value\"}",
            metadata: "{\"header\":\"h1\"}",
            createdAt: createdAt,
            updatedAt: updatedAt);

        trade.CompositeId.Should().Be(compositeId);
        trade.Quantity.Should().Be(250.5m);
        trade.ReferenceDate.Should().Be(refDate);
        trade.Type.Should().Be("FUT");
        trade.Status.Should().Be("CLOSED");
        trade.RawMessage.Should().Be("{\"key\":\"value\"}");
        trade.Metadata.Should().Be("{\"header\":\"h1\"}");
        trade.CreatedAt.Should().Be(createdAt);
        trade.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void CompositeId_ShouldMatchConstructorValue()
    {
        var compositeId = new CompositeId("TRD02", new DateOnly(2024, 6, 1), "SWAP");
        var trade = BuildTrade(compositeId: compositeId);
        trade.CompositeId.Should().Be(compositeId);
    }

    [Fact]
    public void Quantity_ShouldSupportHighPrecisionDecimal()
    {
        var trade = BuildTrade(quantity: 1234567890.12345678m);
        trade.Quantity.Should().Be(1234567890.12345678m);
    }

    [Fact]
    public void Status_ShouldBeSetCorrectly()
    {
        var trade = BuildTrade(status: "PENDING");
        trade.Status.Should().Be("PENDING");
    }

    [Fact]
    public void UpdatedAt_ShouldBePreserved()
    {
        var updatedAt = new DateTime(2024, 3, 15, 10, 30, 0, DateTimeKind.Utc);
        var trade = BuildTrade(updatedAt: updatedAt);
        trade.UpdatedAt.Should().Be(updatedAt);
    }
}
