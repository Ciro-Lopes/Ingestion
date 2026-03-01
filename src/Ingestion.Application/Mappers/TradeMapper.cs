using System.Text.Json;
using Ingestion.Application.DTOs;
using Ingestion.Domain.Entities;
using Ingestion.Domain.ValueObjects;

namespace Ingestion.Application.Mappers;

public static class TradeMapper
{
    public static Trade ToEntity(TradeMessageDto dto)
    {
        var compositeId = new CompositeId(dto.Id, dto.ReferenceDate, dto.Type);
        var rawMessage = JsonSerializer.Serialize(dto);

        var updatedAt = DateTime.SpecifyKind(dto.UpdatedAt, DateTimeKind.Utc);
        var createdAt = DateTime.UtcNow;

        return new Trade(
            compositeId: compositeId,
            quantity: dto.Quantity,
            referenceDate: dto.ReferenceDate,
            type: dto.Type,
            status: dto.Status,
            rawMessage: rawMessage,
            metadata: dto.MetadataPayload,
            createdAt: createdAt,
            updatedAt: updatedAt
        );
    }
}
