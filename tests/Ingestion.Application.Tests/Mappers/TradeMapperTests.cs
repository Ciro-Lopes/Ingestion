using FluentAssertions;
using Ingestion.Application.DTOs;
using Ingestion.Application.Mappers;
using Ingestion.Domain.ValueObjects;

namespace Ingestion.Application.Tests.Mappers;

public sealed class TradeMapperTests
{
    private static TradeMessageDto BuildDto(
        string id = "TRD001",
        decimal quantity = 500m,
        DateOnly referenceDate = default,
        string type = "SPOT",
        string status = "ACTIVE",
        DateTime updatedAt = default,
        string rawPayload = "{\"raw\":true}",
        string metadataPayload = "{\"meta\":true}")
    {
        return new TradeMessageDto(
            Id: id,
            Quantity: quantity,
            ReferenceDate: referenceDate == default ? new DateOnly(2024, 3, 15) : referenceDate,
            Type: type,
            Status: status,
            UpdatedAt: updatedAt == default ? new DateTime(2024, 3, 15, 10, 0, 0, DateTimeKind.Utc) : updatedAt,
            RawPayload: rawPayload,
            MetadataPayload: metadataPayload
        );
    }

    [Fact]
    public void ToEntity_ShouldBuildCompositeIdFromDtoFields()
    {
        var dto = BuildDto(id: "ABC", type: "FUT", referenceDate: new DateOnly(2024, 6, 1));
        var expected = new CompositeId("ABC", new DateOnly(2024, 6, 1), "FUT");

        var entity = TradeMapper.ToEntity(dto);

        entity.CompositeId.Should().Be(expected);
    }

    [Fact]
    public void ToEntity_ShouldMapQuantityCorrectly()
    {
        var dto = BuildDto(quantity: 999.99m);
        var entity = TradeMapper.ToEntity(dto);
        entity.Quantity.Should().Be(999.99m);
    }

    [Fact]
    public void ToEntity_ShouldMapReferenceDateCorrectly()
    {
        var refDate = new DateOnly(2025, 12, 31);
        var dto = BuildDto(referenceDate: refDate);
        var entity = TradeMapper.ToEntity(dto);
        entity.ReferenceDate.Should().Be(refDate);
    }

    [Fact]
    public void ToEntity_ShouldMapTypeCorrectly()
    {
        var dto = BuildDto(type: "SWAP");
        var entity = TradeMapper.ToEntity(dto);
        entity.Type.Should().Be("SWAP");
    }

    [Fact]
    public void ToEntity_ShouldMapStatusCorrectly()
    {
        var dto = BuildDto(status: "PENDING");
        var entity = TradeMapper.ToEntity(dto);
        entity.Status.Should().Be("PENDING");
    }

    [Fact]
    public void ToEntity_ShouldSetUpdatedAtAsUtc()
    {
        var updatedAt = new DateTime(2024, 5, 10, 8, 0, 0, DateTimeKind.Unspecified);
        var dto = BuildDto(updatedAt: updatedAt);

        var entity = TradeMapper.ToEntity(dto);

        entity.UpdatedAt.Kind.Should().Be(DateTimeKind.Utc);
        entity.UpdatedAt.Should().Be(DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc));
    }

    [Fact]
    public void ToEntity_ShouldSetCreatedAtAsUtc()
    {
        var dto = BuildDto();
        var before = DateTime.UtcNow;
        var entity = TradeMapper.ToEntity(dto);
        var after = DateTime.UtcNow;

        entity.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void ToEntity_ShouldSerializeRawMessage()
    {
        var dto = BuildDto();
        var entity = TradeMapper.ToEntity(dto);
        entity.RawMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ToEntity_ShouldSetMetadataFromDto()
    {
        var dto = BuildDto(metadataPayload: "{\"source\":\"queue\"}");
        var entity = TradeMapper.ToEntity(dto);
        entity.Metadata.Should().Be("{\"source\":\"queue\"}");
    }
}
